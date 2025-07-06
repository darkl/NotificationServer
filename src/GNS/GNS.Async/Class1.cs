using System.Diagnostics.Metrics;
using System.Text;
using System.Threading.Tasks.Dataflow;
using System.Xml.Linq;

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
    protected Logic(string uniqueName, Func<Func<EventGroup, Task>, IThreadDispatcher<EventGroup>> dispatcherFactory) : base(uniqueName, dispatcherFactory)
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
            base.IncrementEventProcessed();
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

    public override CollectedNode CollectData()
    {
        CollectedNode result = base.CollectData();
        result.AddProperty("EventsProcessed", _counters.EventProcessedCount);
        return result;
    }

    public abstract Task<IEvent> ProcessEventAsync(IEvent eventToProcess, CancellationToken cancellationToken);
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
public interface IRule : ICloneable
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
/// Generic thread dispatcher interface for handling asynchronous operations
/// </summary>
/// <typeparam name="T">The type of value to dispatch</typeparam>
public interface IThreadDispatcher<T> : IDisposable
{
    /// <summary>
    /// Dispatches a value for processing
    /// </summary>
    /// <param name="value">The value to dispatch</param>
    /// <returns>A task representing the dispatch operation</returns>
    Task DispatchAsync(T value);

    /// <summary>
    /// Completes the dispatcher, preventing new dispatches
    /// </summary>
    void Complete();

    /// <summary>
    /// Gets a task that completes when all dispatched items are processed
    /// </summary>
    Task Completion { get; }

    /// <summary>
    /// Gets the current number of items queued for processing
    /// </summary>
    int QueueCount { get; }

    /// <summary>
    /// Gets whether the dispatcher is accepting new items
    /// </summary>
    bool IsAcceptingItems { get; }
}

/// <summary>
/// Synchronous dispatcher that processes items immediately on the calling thread
/// </summary>
/// <typeparam name="T">The type of value to dispatch</typeparam>
public class SynchronousDispatcher<T> : IThreadDispatcher<T>
{
    private readonly Func<T, Task> _processor;
    private volatile bool _isCompleted;
    private readonly TaskCompletionSource<bool> _completionSource = new();

    public SynchronousDispatcher(Func<T, Task> processor)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _completionSource.SetResult(true); // Always completed for synchronous processing
    }

    public async Task DispatchAsync(T value)
    {
        if (_isCompleted)
            throw new InvalidOperationException("Dispatcher has been completed");

        await _processor(value);
    }

    public void Complete()
    {
        _isCompleted = true;
    }

    public Task Completion => _completionSource.Task;
    public int QueueCount => 0; // No queue for synchronous processing
    public bool IsAcceptingItems => !_isCompleted;

    public void Dispose()
    {
        Complete();
    }
}

/// <summary>
/// ActionBlock-based dispatcher that processes items asynchronously using TPL Dataflow
/// </summary>
/// <typeparam name="T">The type of value to dispatch</typeparam>
public class ActionBlockDispatcher<T> : IThreadDispatcher<T>
{
    private readonly ActionBlock<T> _actionBlock;
    private readonly Func<T, Task> _processor;

    public ActionBlockDispatcher(Func<T, Task> processor, ExecutionDataflowBlockOptions options = null)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));

        var blockOptions = options ?? new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = 1,
            BoundedCapacity = 1000
        };

        _actionBlock = new ActionBlock<T>(_processor, blockOptions);
    }

    public async Task DispatchAsync(T value)
    {
        var posted = await _actionBlock.SendAsync(value);
        if (!posted)
            throw new InvalidOperationException("Failed to dispatch item - dispatcher may be shutting down");
    }

    public void Complete()
    {
        _actionBlock.Complete();
    }

    public Task Completion => _actionBlock.Completion;
    public int QueueCount => _actionBlock.InputCount;
    public bool IsAcceptingItems => !_actionBlock.Completion.IsCompleted;

    public void Dispose()
    {
        Complete();
        try
        {
            _actionBlock.Completion.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            // Log or handle timeout/cancellation as needed
        }
    }
}


/// <summary>
/// Represents a node in the collected data tree
/// </summary>
public class CollectedNode
{
    private readonly Dictionary<string, CollectedNode> _children = new();
    private readonly Dictionary<string, object> _properties = new();

    public CollectedNode(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string Name { get; }

    /// <summary>
    /// Gets all child nodes
    /// </summary>
    public IReadOnlyDictionary<string, CollectedNode> Children => _children;

    /// <summary>
    /// Gets all properties
    /// </summary>
    public IReadOnlyDictionary<string, object> Properties => _properties;

    /// <summary>
    /// Adds a child node
    /// </summary>
    public void AddChild(string key, CollectedNode child)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));
        if (child == null)
            throw new ArgumentNullException(nameof(child));

        _children[key] = child;
    }

    /// <summary>
    /// Adds a property
    /// </summary>
    public void AddProperty(string key, object value)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        _properties[key] = value;
    }

    /// <summary>
    /// Gets a child node by key
    /// </summary>
    public CollectedNode GetChild(string key)
    {
        return _children.TryGetValue(key, out var child) ? child : null;
    }

    /// <summary>
    /// Gets a property by key
    /// </summary>
    public T GetProperty<T>(string key)
    {
        if (_properties.TryGetValue(key, out var value) && value is T)
            return (T)value;
        return default(T);
    }

    /// <summary>
    /// Converts the node tree to a readable string representation
    /// </summary>
    public string ToTreeString(int indent = 0)
    {
        var sb = new StringBuilder();
        var indentStr = new string(' ', indent * 2);

        sb.AppendLine($"{indentStr}{Name}:");

        // Add properties
        foreach (var prop in _properties)
        {
            sb.AppendLine($"{indentStr}  {prop.Key}: {prop.Value}");
        }

        // Add children
        foreach (var child in _children.Values)
        {
            sb.Append(child.ToTreeString(indent + 1));
        }

        return sb.ToString();
    }
}

/// <summary>
/// Interface for objects that can collect their state and metrics data
/// </summary>
public interface ICollectable
{
    /// <summary>
    /// Collects current state and metrics data into a CollectedNode
    /// </summary>
    CollectedNode CollectData();
}

/// <summary>
/// Tracks subscription metrics for a specific recipient-rule pair
/// </summary>
internal class SubscriptionMetrics
{
    private long _eventsSent;
    private DateTime _lastEventSent;

    public long EventsSent => _eventsSent;
    public DateTime LastEventSent => _lastEventSent;

    public void IncrementEventsSent(int count)
    {
        Interlocked.Add(ref _eventsSent, count);
        _lastEventSent = DateTime.UtcNow;
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _eventsSent, 0);
        _lastEventSent = DateTime.MinValue;
    }
}

/// <summary>
/// Enhanced Component implementation with metrics collection capabilities
/// </summary>
public abstract class Component : IComponent, IRecipient, IPublisher, IStateDrivenEntity, ICollectable
{
    private readonly Publisher _publisher;
    private readonly StateDrivenEntityHelper _stateDrivenEntityHelper;
    protected readonly ComponentCounters _counters;
    private readonly ILog _log;
    private readonly Func<Func<EventGroup, Task>, IThreadDispatcher<EventGroup>> _dispatcherFactory;
    private IThreadDispatcher<EventGroup> _eventDispatcher;
    private DateTime _lastMessageReceived = DateTime.MinValue;

    protected Component(
        string uniqueName,
        Func<Func<EventGroup, Task>, IThreadDispatcher<EventGroup>> dispatcherFactory,
        ILog log = null)
    {
        UniqueName = uniqueName ?? throw new ArgumentNullException(nameof(uniqueName));
        _dispatcherFactory = dispatcherFactory ?? throw new ArgumentNullException(nameof(dispatcherFactory));
        _log = log ?? new ConsoleLog(uniqueName);
        _counters = new ComponentCounters();
        _publisher = new Publisher(uniqueName);

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

        _log.Info("Component created with custom dispatcher");
    }

    #region Properties
    public string UniqueName { get; }
    public IRuntimeContext RuntimeContext { get; set; }
    public virtual bool IsRoot { get; set; }
    public ComponentCounters Counters => _counters;
    protected ILog Log => _log;
    public DateTime LastMessageReceived => _lastMessageReceived;

    /// <summary>
    /// Gets the current queue count in the dispatcher (0 if not initialized)
    /// </summary>
    public int QueueCount => _eventDispatcher?.QueueCount ?? 0;

    /// <summary>
    /// Gets whether the component is accepting new messages
    /// </summary>
    public bool IsAcceptingMessages => _eventDispatcher?.IsAcceptingItems == true && CurrentState == State.Started;
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

        // Update last message received time
        _lastMessageReceived = DateTime.UtcNow;

        // Only accept notifications when in Started state
        if (CurrentState != State.Started)
        {
            _log.Warn("Notification received while not in Started state (Current: {0})", CurrentState);
            throw new InvalidOperationException($"Component is not in Started state (Current: {CurrentState})");
        }

        if (_eventDispatcher == null)
        {
            _log.Error("Event dispatcher is not initialized");
            throw new InvalidOperationException("Event dispatcher is not initialized");
        }

        _counters.IncrementNotificationReceived();
        _log.Info("Notification received with {0} events", notification.EventGroup.Count);

        // Dispatch each event in the notification
        await _eventDispatcher.DispatchAsync(notification.EventGroup);
    }

    /// <summary>
    /// Internal method called by the dispatcher to process individual events
    /// </summary>
    private async Task ProcessEventGroupAsync(EventGroup eventGroup)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Call the abstract ConsumeAsync method
            await ConsumeAsync(eventGroup, CancellationToken.None);

            _counters.IncrementNotificationProcessed();
            stopwatch.Stop();

            _log.Info("Processed event {0} in {1}ms", eventGroup.GetType().Name, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _counters.IncrementEventError();
            _log.Error(ex, "Error processing event {0}", eventGroup.GetType().Name);

            try
            {
                await HandleEventErrorAsync(ex, eventGroup, CancellationToken.None);
            }
            catch (Exception handlerEx)
            {
                _log.Error(handlerEx, "Error in event error handler");
            }
        }
    }

    /// <summary>
    /// Override this method to implement component-specific event consumption logic.
    /// </summary>
    protected abstract Task ConsumeAsync(EventGroup eventGroup, CancellationToken cancellationToken);

    /// <summary>
    /// Override this method to handle errors that occur during event processing.
    /// </summary>
    protected virtual Task HandleEventErrorAsync(Exception exception, IEvent evt, CancellationToken cancellationToken)
    {
        _log.Error(exception, "Unhandled error processing event {0}", evt.GetType().Name);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Legacy method for backward compatibility
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

    #region ICollectable Implementation
    public virtual CollectedNode CollectData()
    {
        var node = new CollectedNode(UniqueName);

        // Add component-level properties
        node.AddProperty("ComponentType", GetType().Name);
        node.AddProperty("CurrentState", CurrentState.ToString());
        node.AddProperty("LastMessageReceived", _lastMessageReceived);
        node.AddProperty("IsRoot", IsRoot);
        node.AddProperty("IsAcceptingMessages", IsAcceptingMessages);
        node.AddProperty("QueueCount", QueueCount);
        node.AddProperty("DispatcherType", _eventDispatcher?.GetType().Name ?? "Not initialized");

        // Add counter metrics
        node.AddProperty("EventErrors", _counters.EventErrorCount);
        node.AddProperty("NotificationsReceived", _counters.NotificationReceivedCount);
        node.AddProperty("NotificationsProcessed", _counters.NotificationProcessedCount);
        node.AddProperty("StateTransitions", _counters.StateTransitionCount);

        // Add publisher data as AttachedComponents
        var publisherData = _publisher.CollectData();
        node.AddChild("AttachedComponents", publisherData);

        return node;
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

        // Create the event dispatcher
        _eventDispatcher = _dispatcherFactory(ProcessEventGroupAsync);
        _log.Info("Event dispatcher created during initialization");

        return Task.CompletedTask;
    }

    protected virtual async Task InnerInitializedToUninitializedAsync(CancellationToken cancellationToken)
    {
        _log.Info("Uninitializing component");

        // Complete and dispose the event dispatcher
        if (_eventDispatcher != null)
        {
            _eventDispatcher.Complete();
            try
            {
                await _eventDispatcher.Completion;
                _log.Info("Event dispatcher completed successfully");
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error completing event dispatcher");
            }
            finally
            {
                _eventDispatcher?.Dispose();
                _eventDispatcher = null;
            }
        }
    }

    protected virtual Task InnerInitializedToStartedAsync(CancellationToken cancellationToken)
    {
        _log.Info("Starting component");

        // Dispatcher is already created and ready to process events
        if (_eventDispatcher != null)
        {
            _log.Info("Event dispatcher ready to process events");
        }

        return Task.CompletedTask;
    }

    protected virtual Task InnerStartedToInitializedAsync(CancellationToken cancellationToken)
    {
        _log.Info("Stopping component");

        // Dispatcher continues to exist but component won't accept new notifications
        // due to state check in HandleNotificationAsync

        return Task.CompletedTask;
    }

    protected virtual Task InnerAnyToInvalidAsync(CancellationToken cancellationToken)
    {
        _log.Warn("Component transitioning to Invalid state");

        // Complete dispatcher when going invalid
        _eventDispatcher?.Complete();
        return Task.CompletedTask;
    }

    protected virtual Task InnerInvalidToUninitializedAsync(CancellationToken cancellationToken)
    {
        _log.Info("Recovering from Invalid state");

        // Clean up the dispatcher reference since it was completed in InnerAnyToInvalidAsync
        _eventDispatcher?.Dispose();
        _eventDispatcher = null;

        return Task.CompletedTask;
    }
    #endregion

    #region Utility Methods

    /// <summary>
    /// Gets a tree-formatted metrics report
    /// </summary>
    public string GetMetricsTree()
    {
        return CollectData().ToTreeString();
    }

    /// <summary>
    /// Waits for all pending events to be processed
    /// </summary>
    public async Task WaitForCompletionAsync(TimeSpan timeout = default)
    {
        if (_eventDispatcher == null)
        {
            _log.Info("No event dispatcher to wait for - not initialized");
            return;
        }

        if (timeout == default) timeout = TimeSpan.FromSeconds(30);

        _eventDispatcher.Complete();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _eventDispatcher.Completion.WaitAsync(cts.Token);
            _log.Info("All events processed successfully");
        }
        catch (OperationCanceledException)
        {
            _log.Warn("Timeout waiting for events to complete");
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

            // Complete and dispose event dispatcher
            if (_eventDispatcher != null)
            {
                _eventDispatcher.Complete();
                try
                {
                    _eventDispatcher.Completion.Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error waiting for event dispatcher completion during dispose");
                }
                finally
                {
                    _eventDispatcher?.Dispose();
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

    protected void IncrementEventProcessed()
    {
        this.Counters.IncrementEventProcessed();
    }
}


/// <summary>
/// Factory class for creating common dispatcher configurations
/// </summary>
public static class DispatcherFactory
{
    /// <summary>
    /// Creates a synchronous dispatcher factory
    /// </summary>
    public static Func<Func<EventGroup, Task>, IThreadDispatcher<EventGroup>> CreateSynchronous()
    {
        return processor => new SynchronousDispatcher<EventGroup>(processor);
    }

    /// <summary>
    /// Creates an ActionBlock dispatcher factory with default options
    /// </summary>
    public static Func<Func<IEvent, Task>, IThreadDispatcher<IEvent>> CreateActionBlock(
        int maxDegreeOfParallelism = 1,
        int boundedCapacity = 1000)
    {
        return processor => new ActionBlockDispatcher<IEvent>(processor, new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
            BoundedCapacity = boundedCapacity
        });
    }

    /// <summary>
    /// Creates an ActionBlock dispatcher factory with custom options
    /// </summary>
    public static Func<Func<IEvent, Task>, IThreadDispatcher<IEvent>> CreateActionBlock(
        ExecutionDataflowBlockOptions options)
    {
        return processor => new ActionBlockDispatcher<IEvent>(processor, options);
    }
}


/// <summary>
/// Enhanced Publisher with metrics collection capabilities
/// </summary>
internal class Publisher : IPublisher, ICollectable
{
    private readonly string _uniqueName;
    // Primary subscription index by rule for efficient matching
    private readonly SwapDictionary<IRule, ISet<IRecipient>> _subscriptionsByRule = new();
    // Secondary index by recipient for efficient unsubscribe operations
    private readonly SwapDictionary<IRecipient, ISet<IRule>> _rulesByRecipient = new();
    // Metrics tracking for each recipient-rule pair
    private readonly SwapDictionary<string, SubscriptionMetrics> _subscriptionMetrics = new();

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

    private string GetMetricsKey(IRecipient recipient, IRule rule)
    {
        return $"{recipient.GetType().Name}_{recipient.GetHashCode()}_{rule.GetType().Name}_{rule.GetHashCode()}";
    }

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

                        // Track metrics before sending
                        var metricsKey = GetMetricsKey(recipient, rule);
                        if (!_subscriptionMetrics.TryGetValue(metricsKey, out var metrics))
                        {
                            metrics = new SubscriptionMetrics();
                            _subscriptionMetrics.TryAdd(metricsKey, metrics);
                        }

                        notificationTasks.Add(SendNotificationWithMetrics(recipient, notification, metrics, cancellationToken));
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

    private async Task SendNotificationWithMetrics(IRecipient recipient, Notification notification, SubscriptionMetrics metrics, CancellationToken cancellationToken)
    {
        await recipient.HandleNotificationAsync(notification, cancellationToken);
        metrics.IncrementEventsSent(notification.EventGroup.Count);
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

        // Initialize metrics for this subscription
        var metricsKey = GetMetricsKey(recipient, rule);
        if (!_subscriptionMetrics.ContainsKey(metricsKey))
        {
            _subscriptionMetrics.TryAdd(metricsKey, new SubscriptionMetrics());
        }
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

        // Clean up metrics
        var metricsKey = GetMetricsKey(recipient, rule);
        _subscriptionMetrics.Remove(metricsKey);
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

                // Clean up metrics
                var metricsKey = GetMetricsKey(recipient, rule);
                _subscriptionMetrics.Remove(metricsKey);
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

    public CollectedNode CollectData()
    {
        var node = new CollectedNode(_uniqueName);

        // Add publisher-level properties
        node.AddProperty("EventGroupMaxSize", EventGroupMaxSize);
        node.AddProperty("TotalSubscriptions", _subscriptionMetrics.Count);
        node.AddProperty("TotalRules", _subscriptionsByRule.Count);
        node.AddProperty("TotalRecipients", _rulesByRecipient.Count);

        // Add subscription details
        foreach (var recipientRules in _rulesByRecipient)
        {
            var recipient = recipientRules.Key;
            var rules = recipientRules.Value;

            var recipientNode = new CollectedNode($"{recipient.GetType().Name}_{recipient.GetHashCode()}");
            recipientNode.AddProperty("RecipientType", recipient.GetType().Name);
            recipientNode.AddProperty("RuleCount", rules.Count);

            foreach (var rule in rules)
            {
                var metricsKey = GetMetricsKey(recipient, rule);
                if (_subscriptionMetrics.TryGetValue(metricsKey, out var metrics))
                {
                    var ruleNode = new CollectedNode($"{rule.GetType().Name}_{rule.GetHashCode()}");
                    ruleNode.AddProperty("RuleType", rule.GetType().Name);
                    ruleNode.AddProperty("EventsSent", metrics.EventsSent);
                    ruleNode.AddProperty("LastEventSent", metrics.LastEventSent);

                    recipientNode.AddChild($"Rule_{rule.GetHashCode()}", ruleNode);
                }
            }

            node.AddChild($"Recipient_{recipient.GetHashCode()}", recipientNode);
        }

        return node;
    }
}