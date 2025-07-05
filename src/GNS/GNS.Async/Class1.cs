using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks.Dataflow;

namespace GNS.Async;

public interface IServer : IStateDrivenEntity
{
    IRuntimeContext RuntimeContext { get; }
}

public interface IRuntimeContext
{
    string ServerName { get; }
    IComponentContainer Components { get; set; }
    IServer Server { get; }
    IInputPort InputPort { get; }
    IOutputPort OutputPort { get; }
}

public interface IRecipient
{
    Task HandleNotificationAsync(Notification notification, CancellationToken cancellationToken = default);
}

public interface IPublisher
{
    void Subscribe(IRecipient recipient, IRule rule);
    void Unsubscribe(IRecipient recipient, IRule rule);
    void Unsubscribe(IRecipient recipient);
}

public interface IOutputPort
{
    Task SendAsync<T>(T data, CancellationToken cancellationToken = default);
}

public interface IInputPort
{
    Task<T> ReceiveAsync<T>(CancellationToken cancellationToken = default);
}

public interface IEvent : ICloneable, IValidatable
{
    string ToString();
}

public interface IValidatable
{
}

public interface IComponentContainer : ICollection<IComponent>
{
    void RemoveByName(string name);

    IComponent GetByName(string name);
}

public interface IComponentBuilder
{
    void BuildComponents(IRuntimeContext context);
}

public interface IComponent : IStateDrivenEntity
{
    string UniqueName { get; }
    IRuntimeContext RuntimeContext { get; set; }
    bool IsRoot { get; }
    IEnumerable<IComponent> GetAttachedComponents();
}

public enum State
{
    Uninitialized,
    Initialized,
    Started,
    Invalid
}

public interface IStateDrivenEntity
{
    State CurrentState { get; }
    Task TransformToAsync(State state, CancellationToken cancellationToken = default);

    event EventHandler<StateTransformEventArgs> StateTransforming;
    event EventHandler<StateTransformEventArgs> StateTransformed;
}

public class StateTransformEventArgs : EventArgs
{
    public State PreviousState { get; set; }
    public State NextState { get; set; }
}

public abstract class StateDrivenEntity : IStateDrivenEntity
{
    private State _currentState = State.Uninitialized;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    public State CurrentState => _currentState;

    public virtual async Task TransformToAsync(State state, CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            var previousState = _currentState;
            if (previousState == state) return;

            var args = new StateTransformEventArgs { PreviousState = previousState, NextState = state };
            StateTransforming?.Invoke(this, args);

            await ExecuteStateTransitionAsync(previousState, state, cancellationToken);

            _currentState = state;
            StateTransformed?.Invoke(this, args);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task ExecuteStateTransitionAsync(State from, State to, CancellationToken cancellationToken)
    {
        switch ((from, to))
        {
            case (State.Uninitialized, State.Initialized):
                await InnerUninitializedToInitializedAsync(cancellationToken);
                break;
            case (State.Initialized, State.Uninitialized):
                await InnerInitializedToUninitializedAsync(cancellationToken);
                break;
            case (State.Initialized, State.Started):
                await InnerInitializedToStartedAsync(cancellationToken);
                break;
            case (State.Started, State.Initialized):
                await InnerStartedToInitializedAsync(cancellationToken);
                break;
            case (_, State.Invalid):
                await InnerAnyToInvalidAsync(cancellationToken);
                break;
            case (State.Invalid, State.Uninitialized):
                await InnerInvalidToUninitializedAsync(cancellationToken);
                break;
        }
    }

    public event EventHandler<StateTransformEventArgs> StateTransforming;
    public event EventHandler<StateTransformEventArgs> StateTransformed;

    protected abstract Task InnerUninitializedToInitializedAsync(CancellationToken cancellationToken);
    protected abstract Task InnerInitializedToUninitializedAsync(CancellationToken cancellationToken);
    protected abstract Task InnerInitializedToStartedAsync(CancellationToken cancellationToken);
    protected abstract Task InnerStartedToInitializedAsync(CancellationToken cancellationToken);
    protected abstract Task InnerAnyToInvalidAsync(CancellationToken cancellationToken);
    protected abstract Task InnerInvalidToUninitializedAsync(CancellationToken cancellationToken);

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stateLock?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

[Serializable]
public class Notification
{
    public Notification(EventGroup eventGroup, IRule rule)
    {
        EventGroup = eventGroup ?? throw new ArgumentNullException(nameof(eventGroup));
        Rule = rule ?? throw new ArgumentNullException(nameof(rule));
    }

    public EventGroup EventGroup { get; }
    public IRule Rule { get; }
}

[Serializable]
public class EventGroup : List<IEvent>, IEvent
{
    public EventGroup() { }

    protected EventGroup(IEnumerable<IEvent> collection) : base(collection) { }

    public object Clone()
    {
        var clonedEvents = new List<IEvent>();
        foreach (var evt in this)
        {
            if (evt.Clone() is IEvent clonedEvent)
                clonedEvents.Add(clonedEvent);
        }
        return new EventGroup(clonedEvents);
    }
}

public abstract class Logic : Component
{
    protected Logic(string uniqueName) : base(uniqueName, 1, 1000)
    {
    }

    protected override async Task ConsumeAsync(EventGroup eventGroup, CancellationToken cancellationToken)
    {
        var processed = await ProcessEventGroupAsync(eventGroup, cancellationToken);

        if (processed != null && processed.Count > 0)
        {
            await PublishAsync(processed, cancellationToken);
        }
    }

    public virtual async Task<EventGroup> ProcessEventGroupAsync(EventGroup eventGroup, CancellationToken cancellationToken)
    {
        var result = new EventGroup();
        var processingTasks = new List<Task<IEvent>>();

        foreach (var currentEvent in eventGroup)
        {
            processingTasks.Add(ProcessEventAsync(currentEvent, cancellationToken));
        }

        var processedEvents = await Task.WhenAll(processingTasks);

        foreach (var processed in processedEvents)
        {
            if (processed != null)
            {
                if (processed is EventGroup group)
                {
                    result.AddRange(group);
                }
                else
                {
                    result.Add(processed);
                }
            }
        }

        return result;
    }

    public abstract Task<IEvent> ProcessEventAsync(IEvent eventToProcess, CancellationToken cancellationToken);
}

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

    protected ProcessorLogic(string uniqueName) : base(uniqueName)
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

public class Server : StateDrivenEntity, IServer
{
    private readonly IEnumerable<IComponent> _orderedByHierarchy;
    private readonly RuntimeContext _runtimeContext = new RuntimeContext();

    public Server(string serverName, IComponentBuilder builder)
    {
        _runtimeContext.ServerName = serverName;
        _runtimeContext.Server = this;
        _runtimeContext.Components = new ComponentContainer();

        builder.BuildComponents(this._runtimeContext);

        foreach (IComponent component in RuntimeContext.Components)
        {
            component.RuntimeContext = this._runtimeContext;
        }

        _orderedByHierarchy = ComputeComponentsOrderedByHierarchy();
    }

    public IRuntimeContext RuntimeContext => _runtimeContext;

    /// <summary>
    /// Returns components ordered by hierarchy based on component dependencies
    /// Root components first, followed by their dependencies
    /// </summary>
    private IEnumerable<IComponent> ComputeComponentsOrderedByHierarchy()
    {
        var visited = new HashSet<string>();
        var result = new List<IComponent>();

        // First add all root components
        foreach (var component in RuntimeContext.Components.Where(c => c.IsRoot))
        {
            if (!visited.Contains(component.UniqueName))
            {
                VisitComponentHierarchy(component, visited, result);
            }
        }

        // Then add any components that weren't visited (in case of circular dependencies or orphaned components)
        foreach (var component in RuntimeContext.Components)
        {
            if (!visited.Contains(component.UniqueName))
            {
                VisitComponentHierarchy(component, visited, result, nonRoot: true);
            }
        }

        return result;
    }

    private void VisitComponentHierarchy(IComponent component, HashSet<string> visited, List<IComponent> result,
        bool nonRoot = false)
    {
        visited.Add(component.UniqueName);

        // First add all attached components
        foreach (var dependency in component.GetAttachedComponents())
        {
            if (!visited.Contains(dependency.UniqueName))
            {
                VisitComponentHierarchy(dependency, visited, result, nonRoot);
            }
            else
            {
                if (nonRoot)
                {
                    throw new InvalidOperationException(
                        "A circular dependency has been found but no component was declared as root.");
                }
            }
        }

        // Then add this component
        result.Add(component);
    }

    protected override async Task InnerUninitializedToInitializedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var component in _orderedByHierarchy)
        {
            await component.TransformToAsync(State.Initialized, cancellationToken);
        }
    }

    protected override async Task InnerInitializedToUninitializedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var component in _orderedByHierarchy.Reverse())
        {
            await component.TransformToAsync(State.Uninitialized, cancellationToken);
        }
    }

    protected override async Task InnerInitializedToStartedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var component in _orderedByHierarchy)
        {
            await component.TransformToAsync(State.Started, cancellationToken);
        }
    }

    protected override async Task InnerStartedToInitializedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var component in _orderedByHierarchy.Reverse())
        {
            await component.TransformToAsync(State.Initialized, cancellationToken);
        }
    }

    protected override async Task InnerAnyToInvalidAsync(CancellationToken cancellationToken = default)
    {
        foreach (var component in _orderedByHierarchy)
        {
            await component.TransformToAsync(State.Invalid, cancellationToken);
        }
    }

    protected override async Task InnerInvalidToUninitializedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var component in _orderedByHierarchy)
        {
            await component.TransformToAsync(State.Uninitialized, cancellationToken);
        }
    }
}

// Helper class for thread-safe dictionary operations
internal class ConcurrentHashMap<TKey, TValue> : Dictionary<TKey, TValue>
{
    private readonly ReaderWriterLockSlim _lock = new();

    public new ICollection<TKey> Keys
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return base.Keys.ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    public bool TryAdd(TKey key, TValue value)
    {
        _lock.EnterWriteLock();
        try
        {
            if (ContainsKey(key)) return false;
            Add(key, value);
            return true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool TryRemove(TKey key, out TValue value)
    {
        _lock.EnterWriteLock();
        try
        {
            if (TryGetValue(key, out value))
            {
                Remove(key);
                return true;
            }
            return false;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
}

// Placeholder interfaces referenced but not defined in original code
public interface IRule
{
    bool IsActivated(IEvent @event);
}

internal class RuntimeContext : IRuntimeContext
{
    public string ServerName { get; set; }
    public IComponentContainer Components { get; set; }
    public IServer Server { get; set; }
    public IInputPort InputPort { get; set; }
    public IOutputPort OutputPort { get; set; }
}

/// <summary>
/// Logging interface for the framework
/// </summary>
public interface ILog
{
    void Info(string message);
    void Info(string message, params object[] args);
    void Warn(string message);
    void Warn(string message, params object[] args);
    void Error(string message);
    void Error(string message, params object[] args);
    void Error(Exception exception, string message);
    void Error(Exception exception, string message, params object[] args);
}

/// <summary>
/// Default console-based logging implementation
/// </summary>
public class ConsoleLog : ILog
{
    private readonly string _componentName;

    public ConsoleLog(string componentName = null)
    {
        _componentName = componentName ?? "Unknown";
    }

    public void Info(string message)
    {
        Console.WriteLine($"[INFO] [{_componentName}] {message}");
    }

    public void Info(string message, params object[] args)
    {
        Console.WriteLine($"[INFO] [{_componentName}] {string.Format(message, args)}");
    }

    public void Warn(string message)
    {
        Console.WriteLine($"[WARN] [{_componentName}] {message}");
    }

    public void Warn(string message, params object[] args)
    {
        Console.WriteLine($"[WARN] [{_componentName}] {string.Format(message, args)}");
    }

    public void Error(string message)
    {
        Console.WriteLine($"[ERROR] [{_componentName}] {message}");
    }

    public void Error(string message, params object[] args)
    {
        Console.WriteLine($"[ERROR] [{_componentName}] {string.Format(message, args)}");
    }

    public void Error(Exception exception, string message)
    {
        Console.WriteLine($"[ERROR] [{_componentName}] {message}: {exception}");
    }

    public void Error(Exception exception, string message, params object[] args)
    {
        Console.WriteLine($"[ERROR] [{_componentName}] {string.Format(message, args)}: {exception}");
    }
}

/// <summary>
/// Performance counters for component monitoring
/// </summary>
public class ComponentCounters
{
    private long _eventProcessedCount;
    private long _eventErrorCount;
    private long _notificationReceivedCount;
    private long _notificationProcessedCount;
    private long _stateTransitionCount;

    public long EventProcessedCount => _eventProcessedCount;
    public long EventErrorCount => _eventErrorCount;
    public long NotificationReceivedCount => _notificationReceivedCount;
    public long NotificationProcessedCount => _notificationProcessedCount;
    public long StateTransitionCount => _stateTransitionCount;

    internal void IncrementEventProcessed() => Interlocked.Increment(ref _eventProcessedCount);
    internal void IncrementEventError() => Interlocked.Increment(ref _eventErrorCount);
    internal void IncrementNotificationReceived() => Interlocked.Increment(ref _notificationReceivedCount);
    internal void IncrementNotificationProcessed() => Interlocked.Increment(ref _notificationProcessedCount);
    internal void IncrementStateTransition() => Interlocked.Increment(ref _stateTransitionCount);

    public void Reset()
    {
        Interlocked.Exchange(ref _eventProcessedCount, 0);
        Interlocked.Exchange(ref _eventErrorCount, 0);
        Interlocked.Exchange(ref _notificationReceivedCount, 0);
        Interlocked.Exchange(ref _notificationProcessedCount, 0);
        Interlocked.Exchange(ref _stateTransitionCount, 0);
    }
}

/// <summary>
/// Enhanced Component implementation with optional ActionBlock for message dispatching,
/// logging support, and performance counters.
/// </summary>
public abstract class Component : IComponent, IRecipient, IPublisher, IStateDrivenEntity
{
    private readonly Publisher _publisher;
    private readonly StateDrivenEntityHelper _stateDrivenEntityHelper;
    private readonly ActionBlock<Notification> _notificationProcessor;
    private readonly ComponentCounters _counters;
    private readonly ILog _log;
    private readonly bool _useActionBlock;

    protected Component(string uniqueName, int numOfOwnThreads, int queueMaxSize, ILog log = null,
        ExecutionDataflowBlockOptions dataflowOptions = null)
    {
        UniqueName = uniqueName ?? throw new ArgumentNullException(nameof(uniqueName));
        _log = log ?? new ConsoleLog(uniqueName);
        _counters = new ComponentCounters();
        _publisher = new Publisher(uniqueName);
        _useActionBlock = numOfOwnThreads > 0;

        // Only create ActionBlock if we have threads to work with
        if (_useActionBlock)
        {
            // Configure ActionBlock options
            var options = dataflowOptions ?? new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = numOfOwnThreads, // Sequential processing by default
                BoundedCapacity = queueMaxSize,     // Bounded queue to prevent memory issues
                CancellationToken = CancellationToken.None
            };

            // Create ActionBlock for processing notifications
            _notificationProcessor = new ActionBlock<Notification>(ProcessNotificationAsync, options);
        }

        _stateDrivenEntityHelper = new StateDrivenEntityHelper(
            innerUninitializedToInitialized: InnerUninitializedToInitializedAsync,
            innerInitializedToUninitialized: InnerInitializedToUninitializedAsync,
            innerInitializedToStarted: InnerInitializedToStartedAsync,
            innerStartedToInitialized: InnerStartedToInitializedAsync,
            innerAnyToInvalid: InnerAnyToInvalidAsync,
            innerInvalidToUninitialized: InnerInvalidToUninitializedAsync);

        // Subscribe to state change events for logging and counting
        _stateDrivenEntityHelper.StateTransforming += OnStateTransforming;
        _stateDrivenEntityHelper.StateTransformed += OnStateTransformed;

        _log.Info("Component created (ActionBlock mode: {0})", _useActionBlock);
    }

    #region Properties
    public string UniqueName { get; }
    public IRuntimeContext RuntimeContext { get; set; }
    public virtual bool IsRoot { get; set; }
    public ComponentCounters Counters => _counters;
    protected ILog Log => _log;

    /// <summary>
    /// Gets the current queue count in the ActionBlock (0 if not using ActionBlock)
    /// </summary>
    public int QueueCount => _useActionBlock ? _notificationProcessor.InputCount : 0;

    /// <summary>
    /// Gets whether the component is accepting new messages
    /// </summary>
    public bool IsAcceptingMessages => _useActionBlock ? !_notificationProcessor.Completion.IsCompleted : true;
    #endregion

    #region IComponent Implementation
    public virtual IEnumerable<IComponent> GetAttachedComponents()
    {
        return _publisher.Subscribers().OfType<IComponent>();
    }
    #endregion

    #region IPublisher Implementation
    public void Subscribe(IRecipient recipient, IRule rule)
    {
        if (recipient == null) throw new ArgumentNullException(nameof(recipient));
        if (rule == null) throw new ArgumentNullException(nameof(rule));

        _publisher.Subscribe(recipient, rule);
        _log.Info("Subscribed recipient: {0}", recipient.GetType().Name);
    }

    public void Unsubscribe(IRecipient recipient, IRule rule)
    {
        if (recipient == null) throw new ArgumentNullException(nameof(recipient));
        if (rule == null) throw new ArgumentNullException(nameof(rule));

        _publisher.Unsubscribe(recipient, rule);
        _log.Info("Unsubscribed recipient: {0}", recipient.GetType().Name);
    }

    public void Unsubscribe(IRecipient recipient)
    {
        if (recipient == null) throw new ArgumentNullException(nameof(recipient));

        _publisher.Unsubscribe(recipient);
        _log.Info("Unsubscribed all rules for recipient: {0}", recipient.GetType().Name);
    }

    /// <summary>
    /// Gets or sets the maximum size of event groups that will be sent to recipients.
    /// </summary>
    public int EventGroupMaxSize
    {
        get => _publisher.EventGroupMaxSize;
        set => _publisher.EventGroupMaxSize = value;
    }

    /// <summary>
    /// Publishes an event group to all subscribed recipients asynchronously.
    /// </summary>
    protected Task PublishAsync(EventGroup eventGroup, CancellationToken cancellationToken)
    {
        if (eventGroup == null || eventGroup.Count == 0) return Task.CompletedTask;

        _log.Info("Publishing event group with {0} events", eventGroup.Count);
        return _publisher.PublishAsync(eventGroup, cancellationToken);
    }
    #endregion

    #region IRecipient Implementation
    public async Task HandleNotificationAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        if (notification == null)
            throw new ArgumentNullException(nameof(notification));

        _counters.IncrementNotificationReceived();
        _log.Info("Notification received with {0} events", notification.EventGroup.Count);

        if (_useActionBlock)
        {
            // Post notification to ActionBlock for processing
            var posted = await _notificationProcessor.SendAsync(notification, cancellationToken);

            if (!posted)
            {
                _log.Warn("Failed to queue notification - ActionBlock may be shutting down");
                throw new InvalidOperationException("Component is not accepting new notifications");
            }
        }
        else
        {
            // Process notification directly without ActionBlock
            await ProcessNotificationDirectlyAsync(notification, cancellationToken);
        }
    }

    /// <summary>
    /// Internal method called by ActionBlock to process notifications
    /// </summary>
    private async Task ProcessNotificationAsync(Notification notification)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Process each event in the notification
            foreach (var evt in notification.EventGroup)
            {
                _counters.IncrementEventProcessed();
            }

            // Call the abstract ConsumeAsync method
            await ConsumeAsync(notification.EventGroup, CancellationToken.None);

            _counters.IncrementNotificationProcessed();
            stopwatch.Stop();

            _log.Info("Processed notification with {0} events in {1}ms",
                     notification.EventGroup.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _counters.IncrementEventError();
            _log.Error(ex, "Error processing notification");

            try
            {
                await HandleNotificationErrorAsync(ex, notification, CancellationToken.None);
            }
            catch (Exception handlerEx)
            {
                _log.Error(handlerEx, "Error in notification error handler");
            }
        }
    }

    /// <summary>
    /// Direct processing method used when not using ActionBlock
    /// </summary>
    private async Task ProcessNotificationDirectlyAsync(Notification notification, CancellationToken cancellationToken)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Process each event in the notification
            foreach (var evt in notification.EventGroup)
            {
                _counters.IncrementEventProcessed();
            }

            // Call the abstract ConsumeAsync method
            await ConsumeAsync(notification.EventGroup, cancellationToken);

            _counters.IncrementNotificationProcessed();
            stopwatch.Stop();

            _log.Info("Processed notification with {0} events in {1}ms",
                     notification.EventGroup.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _counters.IncrementEventError();
            _log.Error(ex, "Error processing notification");

            try
            {
                await HandleNotificationErrorAsync(ex, notification, cancellationToken);
            }
            catch (Exception handlerEx)
            {
                _log.Error(handlerEx, "Error in notification error handler");
            }
        }
    }

    /// <summary>
    /// Override this method to implement component-specific event consumption logic.
    /// </summary>
    protected abstract Task ConsumeAsync(EventGroup eventGroup, CancellationToken cancellationToken);

    /// <summary>
    /// Override this method to handle errors that occur during notification processing.
    /// </summary>
    protected virtual Task HandleNotificationErrorAsync(Exception exception, Notification notification, CancellationToken cancellationToken)
    {
        _log.Error(exception, "Unhandled error in ConsumeAsync");
        return Task.CompletedTask;
    }
    #endregion

    #region IStateDrivenEntity Implementation
    public State CurrentState => _stateDrivenEntityHelper.CurrentState;

    public async Task TransformToAsync(State state, CancellationToken cancellationToken = default)
    {
        await _stateDrivenEntityHelper.TransformToAsync(state, cancellationToken);
    }

    public event EventHandler<StateTransformEventArgs> StateTransforming
    {
        add => _stateDrivenEntityHelper.StateTransforming += value;
        remove => _stateDrivenEntityHelper.StateTransforming -= value;
    }

    public event EventHandler<StateTransformEventArgs> StateTransformed
    {
        add => _stateDrivenEntityHelper.StateTransformed += value;
        remove => _stateDrivenEntityHelper.StateTransformed -= value;
    }
    #endregion

    #region State Event Handlers
    private void OnStateTransforming(object sender, StateTransformEventArgs e)
    {
        _log.Info("State transforming from {0} to {1}", e.PreviousState, e.NextState);
    }

    private void OnStateTransformed(object sender, StateTransformEventArgs e)
    {
        _counters.IncrementStateTransition();
        _log.Info("State transformed from {0} to {1}", e.PreviousState, e.NextState);
    }
    #endregion

    #region State Transition Methods
    protected virtual Task InnerUninitializedToInitializedAsync(CancellationToken cancellationToken)
    {
        _log.Info("Initializing component");
        return Task.CompletedTask;
    }

    protected virtual async Task InnerInitializedToUninitializedAsync(CancellationToken cancellationToken)
    {
        _log.Info("Uninitializing component");

        // Complete ActionBlock and wait for pending notifications only if using ActionBlock
        if (_useActionBlock)
        {
            _notificationProcessor.Complete();
            try
            {
                await _notificationProcessor.Completion;
                _log.Info("ActionBlock completed successfully");
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error completing ActionBlock");
            }
        }
    }

    protected virtual Task InnerInitializedToStartedAsync(CancellationToken cancellationToken)
    {
        _log.Info("Starting component");
        return Task.CompletedTask;
    }

    protected virtual Task InnerStartedToInitializedAsync(CancellationToken cancellationToken)
    {
        _log.Info("Stopping component");
        return Task.CompletedTask;
    }

    protected virtual Task InnerAnyToInvalidAsync(CancellationToken cancellationToken)
    {
        _log.Warn("Component transitioning to Invalid state");

        // Complete ActionBlock when going invalid only if using ActionBlock
        if (_useActionBlock)
        {
            _notificationProcessor.Complete();
        }
        return Task.CompletedTask;
    }

    protected virtual Task InnerInvalidToUninitializedAsync(CancellationToken cancellationToken)
    {
        _log.Info("Recovering from Invalid state");
        return Task.CompletedTask;
    }
    #endregion

    #region Utility Methods
    /// <summary>
    /// Gets a summary of component performance metrics
    /// </summary>
    public string GetMetricsSummary()
    {
        return $"Component: {UniqueName}, " +
               $"Events Processed: {_counters.EventProcessedCount}, " +
               $"Events Errors: {_counters.EventErrorCount}, " +
               $"Notifications Received: {_counters.NotificationReceivedCount}, " +
               $"Notifications Processed: {_counters.NotificationProcessedCount}, " +
               $"State Transitions: {_counters.StateTransitionCount}, " +
               $"Queue Count: {QueueCount}, " +
               $"ActionBlock Mode: {_useActionBlock}, " +
               $"Current State: {CurrentState}";
    }

    /// <summary>
    /// Waits for all pending notifications to be processed
    /// </summary>
    public async Task WaitForCompletionAsync(TimeSpan timeout = default)
    {
        if (!_useActionBlock)
        {
            _log.Info("No ActionBlock to wait for - operating in direct mode");
            return;
        }

        if (timeout == default) timeout = TimeSpan.FromSeconds(30);

        _notificationProcessor.Complete();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _notificationProcessor.Completion.WaitAsync(cts.Token);
            _log.Info("All notifications processed successfully");
        }
        catch (OperationCanceledException)
        {
            _log.Warn("Timeout waiting for notifications to complete");
            throw;
        }
    }
    #endregion

    #region IDisposable Implementation
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _log.Info("Disposing component");

            // Complete and dispose ActionBlock only if using ActionBlock
            if (_useActionBlock)
            {
                _notificationProcessor.Complete();
                try
                {
                    _notificationProcessor.Completion.Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error waiting for ActionBlock completion during dispose");
                }
            }

            _stateDrivenEntityHelper?.Dispose();
            _log.Info("Component disposed");
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    #endregion
}
/// <summary>
/// Helper class that encapsulates state-driven entity behavior using composition.
/// This allows the async state management to be reused across different entity types.
/// </summary>
public class StateDrivenEntityHelper : IStateDrivenEntity, IDisposable
{
    private State _currentState = State.Uninitialized;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    // Delegate types for state transition callbacks
    private readonly Func<CancellationToken, Task> _innerUninitializedToInitialized;
    private readonly Func<CancellationToken, Task> _innerInitializedToUninitialized;
    private readonly Func<CancellationToken, Task> _innerInitializedToStarted;
    private readonly Func<CancellationToken, Task> _innerStartedToInitialized;
    private readonly Func<CancellationToken, Task> _innerAnyToInvalid;
    private readonly Func<CancellationToken, Task> _innerInvalidToUninitialized;

    public StateDrivenEntityHelper(
        Func<CancellationToken, Task> innerUninitializedToInitialized,
        Func<CancellationToken, Task> innerInitializedToUninitialized,
        Func<CancellationToken, Task> innerInitializedToStarted,
        Func<CancellationToken, Task> innerStartedToInitialized,
        Func<CancellationToken, Task> innerAnyToInvalid,
        Func<CancellationToken, Task> innerInvalidToUninitialized)
    {
        _innerUninitializedToInitialized = innerUninitializedToInitialized ?? throw new ArgumentNullException(nameof(innerUninitializedToInitialized));
        _innerInitializedToUninitialized = innerInitializedToUninitialized ?? throw new ArgumentNullException(nameof(innerInitializedToUninitialized));
        _innerInitializedToStarted = innerInitializedToStarted ?? throw new ArgumentNullException(nameof(innerInitializedToStarted));
        _innerStartedToInitialized = innerStartedToInitialized ?? throw new ArgumentNullException(nameof(innerStartedToInitialized));
        _innerAnyToInvalid = innerAnyToInvalid ?? throw new ArgumentNullException(nameof(innerAnyToInvalid));
        _innerInvalidToUninitialized = innerInvalidToUninitialized ?? throw new ArgumentNullException(nameof(innerInvalidToUninitialized));
    }

    public State CurrentState => _currentState;

    public virtual async Task TransformToAsync(State state, CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            var previousState = _currentState;
            if (previousState == state) return;

            var args = new StateTransformEventArgs { PreviousState = previousState, NextState = state };
            StateTransforming?.Invoke(this, args);

            await ExecuteStateTransitionAsync(previousState, state, cancellationToken);

            _currentState = state;
            StateTransformed?.Invoke(this, args);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task ExecuteStateTransitionAsync(State from, State to, CancellationToken cancellationToken)
    {
        switch ((from, to))
        {
            case (State.Uninitialized, State.Initialized):
                await _innerUninitializedToInitialized(cancellationToken);
                break;
            case (State.Initialized, State.Uninitialized):
                await _innerInitializedToUninitialized(cancellationToken);
                break;
            case (State.Initialized, State.Started):
                await _innerInitializedToStarted(cancellationToken);
                break;
            case (State.Started, State.Initialized):
                await _innerStartedToInitialized(cancellationToken);
                break;
            case (_, State.Invalid):
                await _innerAnyToInvalid(cancellationToken);
                break;
            case (State.Invalid, State.Uninitialized):
                await _innerInvalidToUninitialized(cancellationToken);
                break;
        }
    }

    public event EventHandler<StateTransformEventArgs> StateTransforming;
    public event EventHandler<StateTransformEventArgs> StateTransformed;

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stateLock?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

internal class Publisher : IPublisher
{
    private readonly string _uniqueName;
    // Primary subscription index by rule for efficient matching
    private readonly SwapDictionary<IRule, ISet<IRecipient>> _subscriptionsByRule = new();
    // Secondary index by recipient for efficient unsubscribe operations
    private readonly SwapDictionary<IRecipient, ISet<IRule>> _rulesByRecipient = new();

    public Publisher(string uniqueName)
    {
        _uniqueName = uniqueName ?? throw new ArgumentException("Publisher name cannot be null or empty", nameof(uniqueName));
    }

    public string UniqueName => _uniqueName;

    /// <summary>
    /// Gets or sets the maximum size of event groups that will be sent to recipients.
    /// If set to a positive value, larger event groups will be split into smaller chunks.
    /// If set to 0 or negative, no splitting will occur.
    /// </summary>
    public int EventGroupMaxSize { get; set; }

    public async Task PublishAsync(EventGroup eventGroup, CancellationToken cancellationToken)
    {
        if (eventGroup == null || eventGroup.Count == 0)
            return;

        // First, create a dictionary to collect events by rule
        var eventsByRule = new Dictionary<IRule, EventGroup>();

        // Process each event individually against each rule
        foreach (IEvent evt in eventGroup)
        {
            foreach (var rulePair in _subscriptionsByRule)
            {
                IRule rule = rulePair.Key;

                // Check if the event matches this rule (only once per rule)
                if (rule.IsActivated(evt))
                {
                    // Add this event to the group for this rule
                    if (!eventsByRule.TryGetValue(rule, out EventGroup matchedEvents))
                    {
                        matchedEvents = new EventGroup();
                        eventsByRule.Add(rule, matchedEvents);
                    }
                    matchedEvents.Add(evt);
                }
            }
        }

        // Now notify all recipients with the matched events for each rule
        foreach (var eventsByRulePair in eventsByRule)
        {
            IRule rule = eventsByRulePair.Key;
            EventGroup matchedEvents = eventsByRulePair.Value;

            if (matchedEvents.Count > 0 && _subscriptionsByRule.TryGetValue(rule, out ISet<IRecipient> recipients))
            {
                // First split events into chunks (if needed)
                List<EventGroup> chunks = new List<EventGroup>();

                // If EventGroupMaxSize is positive and the events exceed that size, split into chunks
                if (EventGroupMaxSize > 0 && matchedEvents.Count > EventGroupMaxSize)
                {
                    // Split the matched events into chunks of the specified maximum size
                    for (int i = 0; i < matchedEvents.Count; i += EventGroupMaxSize)
                    {
                        // Create a new event group for this chunk
                        EventGroup chunk = new EventGroup();

                        // Add events to the chunk (up to EventGroupMaxSize)
                        int eventsToAdd = Math.Min(EventGroupMaxSize, matchedEvents.Count - i);
                        for (int j = 0; j < eventsToAdd; j++)
                        {
                            chunk.Add(matchedEvents[i + j]);
                        }

                        chunks.Add(chunk);
                    }
                }
                else
                {
                    // If no splitting is needed, use the full event group
                    chunks.Add(matchedEvents);
                }

                // Now send each chunk to all recipients asynchronously
                var notificationTasks = new List<Task>();
                foreach (EventGroup chunk in chunks)
                {
                    foreach (IRecipient recipient in recipients)
                    {
                        var notification = new Notification(chunk, rule);
                        notificationTasks.Add(recipient.HandleNotificationAsync(notification, cancellationToken));
                    }
                }

                // Wait for all notifications to complete
                try
                {
                    await Task.WhenAll(notificationTasks);
                }
                catch (Exception)
                {
                    // Log aggregated exceptions or handle as needed
                    // In a real implementation, you'd want proper logging here
                    throw; // Re-throw to allow caller to handle
                }
            }
        }
    }

    public void Subscribe(IRecipient recipient, IRule rule)
    {
        if (recipient == null)
            throw new ArgumentNullException(nameof(recipient));
        if (rule == null)
            throw new ArgumentNullException(nameof(rule));

        // Add to rule-based index
        if (!_subscriptionsByRule.TryGetValue(rule, out ISet<IRecipient> recipients))
        {
            recipients = new SwapHashSet<IRecipient>();
            _subscriptionsByRule.Add(rule, recipients);
        }
        recipients.Add(recipient);

        // Add to recipient-based index
        if (!_rulesByRecipient.TryGetValue(recipient, out ISet<IRule> rules))
        {
            rules = new SwapHashSet<IRule>();
            _rulesByRecipient.Add(recipient, rules);
        }
        rules.Add(rule);
    }

    public void Unsubscribe(IRecipient recipient, IRule rule)
    {
        if (recipient == null)
            throw new ArgumentNullException(nameof(recipient));
        if (rule == null)
            throw new ArgumentNullException(nameof(rule));

        // Remove from rule-based index
        if (_subscriptionsByRule.TryGetValue(rule, out ISet<IRecipient> recipients))
        {
            recipients.Remove(recipient);
            if (recipients.Count == 0)
            {
                _subscriptionsByRule.Remove(rule);
            }
        }

        // Remove from recipient-based index
        if (_rulesByRecipient.TryGetValue(recipient, out ISet<IRule> rules))
        {
            rules.Remove(rule);
            if (rules.Count == 0)
            {
                _rulesByRecipient.Remove(recipient);
            }
        }
    }

    public void Unsubscribe(IRecipient recipient)
    {
        if (recipient == null)
            throw new ArgumentNullException(nameof(recipient));

        // Get all rules this recipient has subscribed to
        if (_rulesByRecipient.TryGetValue(recipient, out ISet<IRule> rules))
        {
            // Make a copy to avoid modification during enumeration
            var rulesCopy = rules.ToList();

            // Remove recipient from each rule's subscribers
            foreach (IRule rule in rulesCopy)
            {
                if (_subscriptionsByRule.TryGetValue(rule, out ISet<IRecipient> recipients))
                {
                    recipients.Remove(recipient);
                    if (recipients.Count == 0)
                    {
                        _subscriptionsByRule.Remove(rule);
                    }
                }
            }

            // Remove recipient from index
            _rulesByRecipient.Remove(recipient);
        }
    }

    public void UnsubscribeAll()
    {
        var recipientsCopy = _rulesByRecipient.Keys.ToList();
        foreach (IRecipient currentSubscriber in recipientsCopy)
        {
            Unsubscribe(currentSubscriber);
        }
    }

    public IEnumerable<IRecipient> Subscribers()
    {
        return _rulesByRecipient.Keys;
    }
}