namespace GNS.Architecture;

public abstract class Component : StateDrivenEntity, IPublisher, IRecipient, IComponent
{
    private readonly Publisher _publisher;

    public Component(string uniqueName)
    {
        UniqueName = uniqueName;
        _publisher = new Publisher(uniqueName);
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

    protected override void InnerUninitializedToInitialized()
    {
    }

    protected override void InnerInitializedToUninitialized()
    {
    }

    protected override void InnerInitializedToStarted()
    {
    }

    protected override void InnerStartedToInitialized()
    {
    }

    protected override void InnerAnyToInvalid()
    {
    }

    protected override void InnerInvalidToUninitialized()
    {
    }
}