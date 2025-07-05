namespace GNS.Async.Tests;

[TestFixture]
public class ComponentTests
{
    [Test]
    public async Task Subscribe_And_Publish_Should_Invoke_Recipient()
    {
        var component = new TestComponent("TestComponent");
        var recipient = new DummyRecipient();
        var rule = new DummyRule();
        var evt = new DummyEvent();
        var group = new EventGroup(){ evt };

        component.Subscribe(recipient, rule);
        await component.PublishAsync(group, CancellationToken.None);

        Assert.IsNotNull(recipient.LastNotification);
        Assert.AreEqual(rule, recipient.LastNotification.Rule);
        Assert.Contains(evt, recipient.LastNotification.EventGroup);
    }

    [Test]
    public async Task Unsubscribe_SpecificRule_Should_StopNotifications()
    {
        var component = new TestComponent("TestComponent");
        var recipient = new DummyRecipient();
        var rule = new DummyRule();
        var evt = new DummyEvent();

        component.Subscribe(recipient, rule);
        component.Unsubscribe(recipient, rule);
        await component.PublishAsync([evt], CancellationToken.None);

        Assert.IsNull(recipient.LastNotification);
    }

    [Test]
    public async Task Unsubscribe_AllRules_Should_StopNotifications()
    {
        var component = new TestComponent("TestComponent");
        var recipient = new DummyRecipient();
        var rule = new DummyRule();
        var evt = new DummyEvent();

        component.Subscribe(recipient, rule);
        component.Unsubscribe(recipient);
        await component.PublishAsync([evt], CancellationToken.None);

        Assert.IsNull(recipient.LastNotification);
    }

    [Test]
    public async Task HandleNotification_Should_Consume_EventGroup()
    {
        var component = new TestComponent("TestComponent");
        var eventGroup = new EventGroup(){ new DummyEvent() };
        var notification = new Notification(eventGroup, new DummyRule());

        await component.TransformToAsync(State.Started);

        await component.HandleNotificationAsync(notification);
        await Task.Delay(300);
        Assert.AreEqual(eventGroup, component.ConsumedEvents);
    }

    [Test]
    public void HandleNotification_Null_Throws()
    {
        var component = new TestComponent("TestComponent");
        Assert.ThrowsAsync<ArgumentNullException>(() => component.HandleNotificationAsync(null));
    }

    [Test]
    public async Task HandleNotification_With_CancellationToken()
    {
        var component = new TestComponent("TestComponent");
        var eventGroup = new EventGroup(){ new DummyEvent() };
        var notification = new Notification(eventGroup, new DummyRule());
        var cancellationToken = new CancellationToken();

        await component.TransformToAsync(State.Started);

        await component.HandleNotificationAsync(notification, cancellationToken);
        await Task.Delay(300);
        
        Assert.AreEqual(eventGroup, component.ConsumedEvents);
        Assert.AreEqual(cancellationToken, component.LastCancellationToken);
    }
}

public class TestComponent : Component
{
    public EventGroup ConsumedEvents { get; private set; }
    public CancellationToken LastCancellationToken { get; private set; }

    public TestComponent(string name) : base(name, 1, 1000) { }

    protected override Task ConsumeAsync(EventGroup eventGroup, CancellationToken cancellationToken)
    {
        ConsumedEvents = eventGroup;
        LastCancellationToken = cancellationToken;
        return Task.CompletedTask;
    }

    // Helper method to expose PublishAsync for testing
    public Task PublishAsync(EventGroup eventGroup, CancellationToken cancellationToken)
    {
        return base.PublishAsync(eventGroup, cancellationToken);
    }
}

public class DummyEvent : IEvent
{
    public object Clone()
    {
        return new DummyEvent();
    }
}

public class DummyRule : IRule
{
    public bool ShouldActivate = true;
    public bool IsActivated(IEvent e) => ShouldActivate;
}

public class DummyRecipient : IRecipient
{
    public Notification LastNotification;

    public Task HandleNotificationAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        LastNotification = notification;
        return Task.CompletedTask;
    }
}