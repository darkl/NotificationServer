using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GNS.Architecture.Utilities;

namespace GNS.Architecture
{
    /// <summary>
    /// Attribute used to mark methods as event processors.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class ProcessorAttribute : Attribute
    {
    }

    /// <summary>
    /// A Logic implementation that automatically dispatches events to methods decorated with [Processor] attribute.
    /// </summary>
    public class ProcessorLogic : Logic
    {
        // Maps event types to delegate handlers
        private readonly Dictionary<Type, MethodInfo> _handlers = new Dictionary<Type, MethodInfo>();

        // Cache of computed handler mappings for concrete event types
        private readonly SwapDictionary<Type, Func<IEvent, IEvent>> _handlerCache = new SwapDictionary<Type, Func<IEvent, IEvent>>();

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
                .Where(m => m.GetCustomAttributes(typeof(ProcessorAttribute), true).Length > 0);

            foreach (var method in processorMethods)
            {
                // Validate the method signature
                var parameters = method.GetParameters();
                if (parameters.Length != 1 || !typeof(IEvent).IsAssignableFrom(parameters[0].ParameterType))
                {
                    throw new InvalidOperationException(
                        $"Method {method.Name} marked with [Processor] attribute must accept exactly one parameter of type IEvent or a derived type.");
                }

                // Get the event type this processor handles
                var eventType = parameters[0].ParameterType;
                // Add to our handler map
                _handlers[eventType] = method;
            }
        }

        /// <summary>
        /// Gets the appropriate delegate type for a given event type and return type.
        /// </summary>
        private Type GetDelegateType(Type eventType, Type returnType)
        {
            // If return type is IEvent, create a Func<TEvent, IEvent>
            if (returnType == typeof(IEvent) || typeof(IEvent).IsAssignableFrom(returnType))
            {
                return typeof(Func<,>).MakeGenericType(eventType, typeof(IEvent));
            }

            // Otherwise create an Action<TEvent>
            return typeof(Action<>).MakeGenericType(eventType);
        }

        /// <summary>
        /// Creates a strongly-typed processor handler for the given event type and method
        /// </summary>
        private Func<IEvent, IEvent> CreateProcessorHandler<TEvent>(MethodInfo method) where TEvent : IEvent
        {
            // Check if method returns void or IEvent
            if (method.ReturnType == typeof(void))
            {
                // Create an Action<TEvent> delegate
                var action = (Action<TEvent>)Delegate.CreateDelegate(typeof(Action<TEvent>), this, method);

                // Wrap it in a Func<IEvent, IEvent> that returns null
                return (IEvent e) => {
                    action((TEvent)e);
                    return null;
                };
            }
            else
            {
                // Create a Func<TEvent, IEvent> delegate
                var func = (Func<TEvent, IEvent>)Delegate.CreateDelegate(typeof(Func<TEvent, IEvent>), this, method);

                // Wrap it in a Func<IEvent, IEvent>
                return (IEvent e) => func((TEvent)e);
            }
        }

        /// <summary>
        /// Creates a processor handler for the given method and eventType using reflection
        /// </summary>
        private Func<IEvent, IEvent> CreateProcessorHandlerViaReflection(MethodInfo method, Type eventType)
        {
            // Get the generic method
            var genericMethod = typeof(ProcessorLogic).GetMethod(nameof(CreateProcessorHandler),
                BindingFlags.Instance | BindingFlags.NonPublic);

            // Create the specific generic method for this event type
            var specificMethod = genericMethod.MakeGenericMethod(eventType);

            // Invoke it to create our handler
            return (Func<IEvent, IEvent>)specificMethod.Invoke(this, new object[] { method });
        }

        /// <summary>
        /// Processes an event by finding and invoking the most specific handler for the event type.
        /// </summary>
        public override IEvent ProcessEvent(IEvent eventToProcess)
        {
            if (eventToProcess == null)
                return null;

            var eventType = eventToProcess.GetType();

            // Check if we already have a cached handler for this exact event type
            if (_handlerCache.TryGetValue(eventType, out var cachedHandler))
            {
                return cachedHandler(eventToProcess);
            }

            // We need to find the best handler
            var possibleHandlers = new List<MethodInfo>();
            var handlerTypes = new List<Type>();

            // Find all handlers that could handle this event type
            foreach (var entry in _handlers)
            {
                if (entry.Key.IsAssignableFrom(eventType))
                {
                    handlerTypes.Add(entry.Key);
                    possibleHandlers.Add(entry.Value);
                }
            }

            if (possibleHandlers.Count == 0)
            {
                // No handler found - cache a null handler to avoid future lookups
                _handlerCache[eventType] = _ => null;
                return null;
            }

            // Find the most specific handler using DefaultBinder.SelectMethod
            var binder = Type.DefaultBinder;
            var selectedMethod = (MethodInfo)binder.SelectMethod(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                possibleHandlers.ToArray(),
                new[] { eventType },
                null);

            if (selectedMethod == null)
            {
                // No handler selected - cache a null handler to avoid future lookups
                _handlerCache[eventType] = _ => null;
                return null;
            }

            // Create a handler using reflection and cache it
            var handler = CreateProcessorHandlerViaReflection(selectedMethod, eventType);
            _handlerCache[eventType] = handler;

            // Invoke the handler
            return handler(eventToProcess);
        }
    }
}