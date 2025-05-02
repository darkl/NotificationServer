namespace GNS.Architecture;

public abstract class Component : IPublisher, IRecipient
{
    private readonly string _uniqueName;
    private readonly Publisher _publisher;

    public Component(string uniqueName)
    {
        _uniqueName = uniqueName;
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
}