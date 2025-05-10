namespace GNS.Architecture;

public abstract class Component : IStateDrivenEntity, IPublisher, IRecipient, IComponent
{
    private readonly Publisher _publisher;
    private readonly StateDrivenEntityHelper _stateDrivenEntityHelper;

    public Component(string uniqueName)
    {
        UniqueName = uniqueName;
        _publisher = new Publisher(uniqueName);

        _stateDrivenEntityHelper =
            new StateDrivenEntityHelper
            (innerUninitializedToInitialized: InnerUninitializedToInitialized,
                innerInitializedToStarted: InnerInitializedToStarted,
                innerStartedToInitialized: InnerStartedToInitialized,
                innerInitializedToUninitialized: InnerInitializedToUninitialized,
                innerAnyToInvalid: InnerAnyToInvalid,
                innerInvalidToUninitialized: InnerInitializedToUninitialized);
    }

    public void Subscribe(IRecipient recipient, IRule rule)
    {
        _publisher.Subscribe(recipient, rule);
    }

    public void Unsubscribe(IRecipient recipient, IRule rule)
    {
        _publisher.Unsubscribe(recipient, rule);
    }

    public void Unsubscribe(IRecipient recipient)
    {
        _publisher.Unsubscribe(recipient);
    }

    public void Publish(EventGroup eventGroup)
    {
        _publisher.Publish(eventGroup);
    }

    public int EventGroupMaxSize
    {
        get => _publisher.EventGroupMaxSize;
        set => _publisher.EventGroupMaxSize = value;
    }

    public void HandleNotification(Notification notification)
    {
        if (notification == null)
            throw new ArgumentNullException(nameof(notification));


        // In a real implementation, this would dispatch to the component's thread
        // For simplicity, we'll directly call Consume here
        Consume(notification.EventGroup);
    }

    protected abstract void Consume(EventGroup eventGroup);
    public IRuntimeContext RuntimeContext { get; set; }
    public bool IsRoot { get; set; }

    public string UniqueName { get; }

    public IEnumerable<IComponent> GetAttachedComponents()
    {
        return this._publisher.Subscribers().OfType<IComponent>();
    }

    protected virtual void InnerUninitializedToInitialized()
    {
    }

    protected virtual void InnerInitializedToUninitialized()
    {
    }

    protected virtual void InnerInitializedToStarted()
    {
    }

    protected virtual void InnerStartedToInitialized()
    {
    }

    protected virtual void InnerAnyToInvalid()
    {
    }

    protected virtual void InnerInvalidToUninitialized()
    {
    }

    public State CurrentState => _stateDrivenEntityHelper.CurrentState;

    public void TransformTo(State state)
    {
        _stateDrivenEntityHelper.TransformTo(state);
    }

    public event EventHandler<StateTransformEventArgs>? StateTransforming
    {
        add => _stateDrivenEntityHelper.StateTransforming += value;
        remove => _stateDrivenEntityHelper.StateTransforming -= value;
    }

    public event EventHandler<StateTransformEventArgs>? StateTransformed
    {
        add => _stateDrivenEntityHelper.StateTransformed += value;
        remove => _stateDrivenEntityHelper.StateTransformed -= value;
    }
}