using System.Collections.Concurrent;
using System.Reflection;

namespace GNS.Async;

/// <summary>
/// Attribute used to mark methods as event processors.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class ProcessorAttribute : Attribute
{
}

/// <summary>
/// A Logic implementation that automatically dispatches events to methods decorated with [Processor] attribute.
/// Supports both synchronous and asynchronous processor methods.
/// </summary>
public class ProcessorLogic : Logic
{
    // Maps event types to method info for handlers
    private readonly Dictionary<Type, MethodInfo> _handlers = new();

    // Cache of computed handler mappings for concrete event types
    private readonly ConcurrentDictionary<Type, Func<IEvent, CancellationToken, Task<IEvent>>> _handlerCache = new();

    protected ProcessorLogic(string uniqueName, Func<Func<EventGroup, Task>, IThreadDispatcher<EventGroup>> dispatcherFactory) : base(uniqueName, dispatcherFactory)
    {
        InitializeHandlers();
    }

    /// <summary>
    /// Initializes the event handler mappings by examining the current type for methods
    /// decorated with the [Processor] attribute.
    /// </summary>
    private void InitializeHandlers()
    {
        // Get all methods in this type with the Processor attribute
        var processorMethods = GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.GetCustomAttribute<ProcessorAttribute>() != null);

        foreach (var method in processorMethods)
        {
            ValidateProcessorMethod(method);

            // Get the event type this processor handles (first parameter)
            var eventType = method.GetParameters()[0].ParameterType;

            if (_handlers.ContainsKey(eventType))
            {
                throw new InvalidOperationException(
                    $"Multiple processor methods found for event type {eventType.Name}. Only one processor per event type is allowed.");
            }

            _handlers[eventType] = method;
        }
    }

    /// <summary>
    /// Validates that a processor method has the correct signature
    /// </summary>
    private static void ValidateProcessorMethod(MethodInfo method)
    {
        var parameters = method.GetParameters();

        // Must have 1 or 2 parameters
        if (parameters.Length < 1 || parameters.Length > 2)
        {
            throw new InvalidOperationException(
                $"Method {method.Name} marked with [Processor] attribute must accept 1-2 parameters: " +
                "IEvent (or derived type) and optionally CancellationToken.");
        }

        // First parameter must be IEvent or derived
        if (!typeof(IEvent).IsAssignableFrom(parameters[0].ParameterType))
        {
            throw new InvalidOperationException(
                $"Method {method.Name} marked with [Processor] attribute must have first parameter of type IEvent or a derived type.");
        }

        // Second parameter (if present) must be CancellationToken
        if (parameters.Length == 2 && parameters[1].ParameterType != typeof(CancellationToken))
        {
            throw new InvalidOperationException(
                $"Method {method.Name} marked with [Processor] attribute must have second parameter of type CancellationToken if present.");
        }

        // Validate return type
        var returnType = method.ReturnType;
        var isAsync = returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>);
        var isVoidAsync = returnType == typeof(Task);
        var isSync = returnType == typeof(void) || typeof(IEvent).IsAssignableFrom(returnType);

        if (!isAsync && !isVoidAsync && !isSync)
        {
            throw new InvalidOperationException(
                $"Method {method.Name} marked with [Processor] attribute must return void, IEvent, Task, or Task<IEvent>.");
        }

        // If async, validate the generic type
        if (isAsync)
        {
            var asyncReturnType = returnType.GetGenericArguments()[0];
            if (!typeof(IEvent).IsAssignableFrom(asyncReturnType))
            {
                throw new InvalidOperationException(
                    $"Method {method.Name} marked with [Processor] attribute returning Task<T> must have T as IEvent or derived type.");
            }
        }
    }

    /// <summary>
    /// Creates a strongly-typed processor handler for the given event type and method
    /// </summary>
    private Func<IEvent, CancellationToken, Task<IEvent>> CreateProcessorHandler(MethodInfo method, Type eventType)
    {
        var parameters = method.GetParameters();
        var hasCancellationToken = parameters.Length == 2;
        var returnType = method.ReturnType;

        // Determine method signature pattern
        var isAsync = returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>);
        var isVoidAsync = returnType == typeof(Task);
        var isVoidSync = returnType == typeof(void);

        return (evt, cancellationToken) =>
        {
            try
            {
                object[] args = hasCancellationToken
                    ? new object[] { evt, cancellationToken }
                    : new object[] { evt };

                var result = method.Invoke(this, args);

                return returnType switch
                {
                    // Async methods returning Task<IEvent>
                    _ when isAsync => (Task<IEvent>)result,

                    // Async void methods returning Task
                    _ when isVoidAsync => ((Task)result).ContinueWith(_ => (IEvent)null, cancellationToken),

                    // Sync void methods
                    _ when isVoidSync => Task.FromResult((IEvent)null),

                    // Sync methods returning IEvent
                    _ => Task.FromResult((IEvent)result)
                };
            }
            catch (Exception ex)
            {
                return Task.FromException<IEvent>(ex);
            }
        };
    }

    /// <summary>
    /// Processes an event by finding and invoking the most specific handler for the event type.
    /// </summary>
    public override async Task<IEvent> ProcessEventAsync(IEvent eventToProcess, CancellationToken cancellationToken)
    {
        if (eventToProcess == null)
            return null;

        var eventType = eventToProcess.GetType();

        // Get or create cached handler for this event type
        var handler = _handlerCache.GetOrAdd(eventType, GenerateHandlerForEventType);

        return await handler(eventToProcess, cancellationToken);
    }

    /// <summary>
    /// Generates a handler function for the specified event type
    /// </summary>
    private Func<IEvent, CancellationToken, Task<IEvent>> GenerateHandlerForEventType(Type eventType)
    {
        // Find all handlers that could handle this event type
        var possibleHandlers = _handlers
            .Where(entry => entry.Key.IsAssignableFrom(eventType))
            .ToList();

        if (possibleHandlers.Count == 0)
        {
            // No handler found - return a no-op handler
            return (_, _) => Task.FromResult((IEvent)null);
        }

        // Find the most specific handler
        var bestMatch = possibleHandlers
            .OrderBy(entry => GetTypeHierarchyDepth(eventType, entry.Key))
            .First();

        return CreateProcessorHandler(bestMatch.Value, bestMatch.Key);
    }

    /// <summary>
    /// Calculates the inheritance depth between two types for handler resolution
    /// </summary>
    private static int GetTypeHierarchyDepth(Type derivedType, Type baseType)
    {
        if (!baseType.IsAssignableFrom(derivedType))
            return int.MaxValue;

        int depth = 0;
        var currentType = derivedType;

        while (currentType != null && currentType != baseType)
        {
            depth++;
            currentType = currentType.BaseType;

            // Also check interfaces
            if (currentType == null && baseType.IsInterface)
            {
                var interfaces = derivedType.GetInterfaces();
                if (interfaces.Contains(baseType))
                {
                    return depth;
                }
            }
        }

        return currentType == baseType ? depth : int.MaxValue;
    }
}