using GNS.Architecture;

[TestFixture]
public class ComponentTests
{
    [Test]
    public void Subscribe_And_Publish_Should_Invoke_Recipient()
    {
        var component = new TestComponent("TestComponent");
        var recipient = new DummyRecipient();
        var rule = new DummyRule();
        var evt = new DummyEvent();
        var group = new DummyEventGroup(evt);

        component.Subscribe(recipient, rule);
        component.Publish(group);

        Assert.IsNotNull(recipient.LastNotification);
        Assert.AreEqual(rule, recipient.LastNotification.Rule);
        Assert.Contains(evt, recipient.LastNotification.EventGroup);
    }

    [Test]
    public void Unsubscribe_SpecificRule_Should_StopNotifications()
    {
        var component = new TestComponent("TestComponent");
        var recipient = new DummyRecipient();
        var rule = new DummyRule();
        var evt = new DummyEvent();

        component.Subscribe(recipient, rule);
        component.Unsubscribe(recipient, rule);
        component.Publish(new DummyEventGroup(evt));

        Assert.IsNull(recipient.LastNotification);
    }

    [Test]
    public void Unsubscribe_AllRules_Should_StopNotifications()
    {
        var component = new TestComponent("TestComponent");
        var recipient = new DummyRecipient();
        var rule = new DummyRule();
        var evt = new DummyEvent();

        component.Subscribe(recipient, rule);
        component.Unsubscribe(recipient);
        component.Publish(new DummyEventGroup(evt));

        Assert.IsNull(recipient.LastNotification);
    }

    [Test]
    public void HandleNotification_Should_Consume_EventGroup()
    {
        var component = new TestComponent("TestComponent");
        var eventGroup = new DummyEventGroup(new DummyEvent());
        var notification = new Notification(eventGroup, new DummyRule());

        component.HandleNotification(notification);

        Assert.AreEqual(eventGroup, component.ConsumedEvents);
    }

    [Test]
    public void HandleNotification_Null_Throws()
    {
        var component = new TestComponent("TestComponent");

        Assert.Throws<ArgumentNullException>(() => component.HandleNotification(null));
    }
}

public class TestComponent : Component
{
    public EventGroup ConsumedEvents { get; private set; }

    public TestComponent(string name) : base(name) { }

    protected override void Consume(EventGroup eventGroup)
    {
        ConsumedEvents = eventGroup;
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
    public object Clone()
    {
        return new DummyRule() { ShouldActivate = ShouldActivate };
    }
}

public class DummyRecipient : IRecipient
{
    public Notification LastNotification;

    public void HandleNotification(Notification notification)
    {
        LastNotification = notification;
    }
}

public class DummyEventGroup : EventGroup
{
    public DummyEventGroup(params IEvent[] events)
    {
        foreach (var e in events)
            Add(e);
    }
}
