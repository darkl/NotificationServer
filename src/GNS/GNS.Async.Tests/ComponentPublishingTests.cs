using Moq;

namespace GNS.Async.Tests;

[TestFixture]
public class ComponentPublishingTests
{
    // Test implementation of Component class for testing
    private class TestComponent : Component
    {
        public List<EventGroup> ConsumedEventGroups { get; } = new List<EventGroup>();

        public TestComponent(string name) : base(name, DispatcherFactory.CreateSynchronous()) { }

        protected override Task ConsumeAsync(EventGroup eventGroup, CancellationToken cancellationToken)
        {
            ConsumedEventGroups.Add(eventGroup);
            return Task.CompletedTask;
        }

        // Expose protected PublishAsync method for testing
        public async Task TestPublishAsync(EventGroup eventGroup, CancellationToken cancellationToken = default)
        {
            await PublishAsync(eventGroup, cancellationToken);
        }
    }

    // Test implementation of Event for testing
    private class TestEvent : IEvent
    {
        public int Id { get; }

        public TestEvent(int id)
        {
            Id = id;
        }

        public override bool Equals(object obj)
        {
            return obj is TestEvent other && Id == other.Id;
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public object Clone()
        {
            return new TestEvent(Id);
        }

        public override string ToString()
        {
            return $"TestEvent({Id})";
        }
    }

    // Test implementation of Rule for testing
    private class TestRule : IRule
    {
        public Predicate<IEvent> Matcher { get; }

        public TestRule(Predicate<IEvent> matcher)
        {
            Matcher = matcher;
        }

        public bool IsActivated(IEvent evt)
        {
            return Matcher(evt);
        }

        public override bool Equals(object obj)
        {
            // Simple implementation for testing
            return obj == this;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public object Clone()
        {
            return MemberwiseClone();
        }
    }

    [Test]
    public async Task Publish_NoEventGroupMaxSize_SendsAllEventsInOneGroup()
    {
        // Arrange
        var component = new TestComponent("TestComponent");
        var recipient = new Mock<IRecipient>();
        var capturedNotifications = new List<Notification>();

        recipient.Setup(r => r.HandleNotificationAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, ct) => capturedNotifications.Add(n))
            .Returns(Task.CompletedTask);

        var eventGroup = new EventGroup();
        for (int i = 1; i <= 10; i++)
        {
            eventGroup.Add(new TestEvent(i));
        }

        var rule = new TestRule(evt => true); // Match all events

        component.Subscribe(recipient.Object, rule);

        // Act
        await component.TestPublishAsync(eventGroup);

        // Assert
        Assert.That(capturedNotifications.Count, Is.EqualTo(1));
        Assert.That(capturedNotifications[0].EventGroup.Count, Is.EqualTo(10));
    }

    [Test]
    public async Task Publish_WithEventGroupMaxSize_SplitsEventsIntoChunks()
    {
        // Arrange
        var component = new TestComponent("TestComponent");
        component.EventGroupMaxSize = 3; // Limit to 3 events per group

        var recipient = new Mock<IRecipient>();
        var capturedNotifications = new List<Notification>();

        recipient.Setup(r => r.HandleNotificationAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, ct) => capturedNotifications.Add(n))
            .Returns(Task.CompletedTask);

        var eventGroup = new EventGroup();
        for (int i = 1; i <= 10; i++)
        {
            eventGroup.Add(new TestEvent(i));
        }

        var rule = new TestRule(evt => true); // Match all events

        component.Subscribe(recipient.Object, rule);

        // Act
        await component.TestPublishAsync(eventGroup);

        // Assert
        Assert.That(capturedNotifications.Count, Is.EqualTo(4)); // 10 events / 3 per group = 4 groups (3+3+3+1)
        Assert.That(capturedNotifications[0].EventGroup.Count, Is.EqualTo(3));
        Assert.That(capturedNotifications[1].EventGroup.Count, Is.EqualTo(3));
        Assert.That(capturedNotifications[2].EventGroup.Count, Is.EqualTo(3));
        Assert.That(capturedNotifications[3].EventGroup.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Publish_WithEventGroupMaxSizeAndMultipleRecipients_SendsChunksToAllRecipients()
    {
        // Arrange
        var component = new TestComponent("TestComponent");
        component.EventGroupMaxSize = 4; // Limit to 4 events per group

        var recipient1 = new Mock<IRecipient>();
        var recipient2 = new Mock<IRecipient>();

        var capturedNotifications1 = new List<Notification>();
        var capturedNotifications2 = new List<Notification>();

        recipient1.Setup(r => r.HandleNotificationAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, ct) => capturedNotifications1.Add(n))
            .Returns(Task.CompletedTask);
        recipient2.Setup(r => r.HandleNotificationAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, ct) => capturedNotifications2.Add(n))
            .Returns(Task.CompletedTask);

        var eventGroup = new EventGroup();
        for (int i = 1; i <= 10; i++)
        {
            eventGroup.Add(new TestEvent(i));
        }

        var rule = new TestRule(evt => true); // Match all events

        component.Subscribe(recipient1.Object, rule);
        component.Subscribe(recipient2.Object, rule);

        // Act
        await component.TestPublishAsync(eventGroup);

        // Assert
        Assert.That(capturedNotifications1.Count, Is.EqualTo(3)); // 10 events / 4 per group = 3 groups (4+4+2)
        Assert.That(capturedNotifications2.Count, Is.EqualTo(3));

        // Check recipient 1 received the right chunks
        Assert.That(capturedNotifications1[0].EventGroup.Count, Is.EqualTo(4));
        Assert.That(capturedNotifications1[1].EventGroup.Count, Is.EqualTo(4));
        Assert.That(capturedNotifications1[2].EventGroup.Count, Is.EqualTo(2));

        // Check recipient 2 received the right chunks
        Assert.That(capturedNotifications2[0].EventGroup.Count, Is.EqualTo(4));
        Assert.That(capturedNotifications2[1].EventGroup.Count, Is.EqualTo(4));
        Assert.That(capturedNotifications2[2].EventGroup.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task Publish_WithEventGroupMaxSizeAndMultipleRules_FiltersCorrectly()
    {
        // Arrange
        var component = new TestComponent("TestComponent");
        component.EventGroupMaxSize = 2; // Limit to 2 events per group

        var recipient = new Mock<IRecipient>();
        var capturedNotifications = new List<Notification>();

        recipient.Setup(r => r.HandleNotificationAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, ct) => capturedNotifications.Add(n))
            .Returns(Task.CompletedTask);

        var eventGroup = new EventGroup();
        for (int i = 1; i <= 10; i++)
        {
            eventGroup.Add(new TestEvent(i));
        }

        // Rule 1: Match events with even IDs
        var evenRule = new TestRule(evt => ((TestEvent)evt).Id % 2 == 0);
        // Rule 2: Match events with IDs > 5
        var greaterThanFiveRule = new TestRule(evt => ((TestEvent)evt).Id > 5);

        component.Subscribe(recipient.Object, evenRule);
        component.Subscribe(recipient.Object, greaterThanFiveRule);

        // Act
        await component.TestPublishAsync(eventGroup);

        // Assert
        // Count notifications for each rule
        int evenRuleNotifications = capturedNotifications.Count(n => n.Rule == evenRule);
        int greaterThanFiveRuleNotifications = capturedNotifications.Count(n => n.Rule == greaterThanFiveRule);

        // For evenRule: 5 even numbers (2,4,6,8,10) / 2 per group = 3 groups (2+2+1)
        Assert.That(evenRuleNotifications, Is.EqualTo(3));

        // For greaterThanFiveRule: 5 numbers > 5 (6,7,8,9,10) / 2 per group = 3 groups (2+2+1)
        Assert.That(greaterThanFiveRuleNotifications, Is.EqualTo(3));

        // Total notifications: 3 + 3 = 6
        Assert.That(capturedNotifications.Count, Is.EqualTo(6));
    }

    [Test]
    public async Task Publish_WithZeroEventGroupMaxSize_DoesNotSplitEvents()
    {
        // Arrange
        var component = new TestComponent("TestComponent");
        component.EventGroupMaxSize = 0; // Explicitly set to 0 (default)

        var recipient = new Mock<IRecipient>();
        var capturedNotifications = new List<Notification>();

        recipient.Setup(r => r.HandleNotificationAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, ct) => capturedNotifications.Add(n))
            .Returns(Task.CompletedTask);

        var eventGroup = new EventGroup();
        for (int i = 1; i <= 10; i++)
        {
            eventGroup.Add(new TestEvent(i));
        }

        var rule = new TestRule(evt => true); // Match all events

        component.Subscribe(recipient.Object, rule);

        // Act
        await component.TestPublishAsync(eventGroup);

        // Assert
        Assert.That(capturedNotifications.Count, Is.EqualTo(1));
        Assert.That(capturedNotifications[0].EventGroup.Count, Is.EqualTo(10));
    }

    [Test]
    public async Task Publish_WithNegativeEventGroupMaxSize_DoesNotSplitEvents()
    {
        // Arrange
        var component = new TestComponent("TestComponent");
        component.EventGroupMaxSize = -5; // Negative value

        var recipient = new Mock<IRecipient>();
        var capturedNotifications = new List<Notification>();

        recipient.Setup(r => r.HandleNotificationAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, ct) => capturedNotifications.Add(n))
            .Returns(Task.CompletedTask);

        var eventGroup = new EventGroup();
        for (int i = 1; i <= 10; i++)
        {
            eventGroup.Add(new TestEvent(i));
        }

        var rule = new TestRule(evt => true); // Match all events

        component.Subscribe(recipient.Object, rule);

        // Act
        await component.TestPublishAsync(eventGroup);

        // Assert
        Assert.That(capturedNotifications.Count, Is.EqualTo(1));
        Assert.That(capturedNotifications[0].EventGroup.Count, Is.EqualTo(10));
    }

    [Test]
    public async Task Publish_WithEventGroupMaxSizeExactlyMatchingEventCount_CreatesSingleChunk()
    {
        // Arrange
        var component = new TestComponent("TestComponent");
        component.EventGroupMaxSize = 10; // Match exactly the number of events

        var recipient = new Mock<IRecipient>();
        var capturedNotifications = new List<Notification>();

        recipient.Setup(r => r.HandleNotificationAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, ct) => capturedNotifications.Add(n))
            .Returns(Task.CompletedTask);

        var eventGroup = new EventGroup();
        for (int i = 1; i <= 10; i++)
        {
            eventGroup.Add(new TestEvent(i));
        }

        var rule = new TestRule(evt => true); // Match all events

        component.Subscribe(recipient.Object, rule);

        // Act
        await component.TestPublishAsync(eventGroup);

        // Assert
        Assert.AreEqual(1, capturedNotifications.Count);
        Assert.AreEqual(10, capturedNotifications[0].EventGroup.Count);
    }

    [Test]
    public async Task Publish_WithNoEvents_DoesNotSendNotifications()
    {
        // Arrange
        var component = new TestComponent("TestComponent");
        component.EventGroupMaxSize = 3;

        var recipient = new Mock<IRecipient>();
        var capturedNotifications = new List<Notification>();

        recipient.Setup(r => r.HandleNotificationAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, ct) => capturedNotifications.Add(n))
            .Returns(Task.CompletedTask);

        var emptyEventGroup = new EventGroup();

        var rule = new TestRule(evt => true);

        component.Subscribe(recipient.Object, rule);

        // Act
        await component.TestPublishAsync(emptyEventGroup);

        // Assert
        Assert.That(capturedNotifications.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task Publish_WithNoMatchingEvents_DoesNotSendNotifications()
    {
        // Arrange
        var component = new TestComponent("TestComponent");
        component.EventGroupMaxSize = 3;

        var recipient = new Mock<IRecipient>();
        var capturedNotifications = new List<Notification>();

        recipient.Setup(r => r.HandleNotificationAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, ct) => capturedNotifications.Add(n))
            .Returns(Task.CompletedTask);

        var eventGroup = new EventGroup();
        for (int i = 1; i <= 10; i++)
        {
            eventGroup.Add(new TestEvent(i));
        }

        var noMatchRule = new TestRule(evt => false); // Never matches

        component.Subscribe(recipient.Object, noMatchRule);

        // Act
        await component.TestPublishAsync(eventGroup);

        // Assert
        Assert.That(capturedNotifications.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task Publish_WithCancellationToken_PropagatesToken()
    {
        // Arrange
        var component = new TestComponent("TestComponent");
        var recipient = new Mock<IRecipient>();
        var capturedTokens = new List<CancellationToken>();

        recipient.Setup(r => r.HandleNotificationAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, ct) => capturedTokens.Add(ct))
            .Returns(Task.CompletedTask);

        var eventGroup = new EventGroup();
        eventGroup.Add(new TestEvent(1));

        var rule = new TestRule(evt => true);
        component.Subscribe(recipient.Object, rule);

        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        // Act
        await component.TestPublishAsync(eventGroup, cancellationToken);

        // Assert
        Assert.That(capturedTokens.Count, Is.EqualTo(1));
        Assert.That(capturedTokens[0], Is.EqualTo(cancellationToken));
    }

    [Test]
    public void Publish_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var component = new TestComponent("TestComponent");
        var recipient = new Mock<IRecipient>();

        recipient.Setup(r => r.HandleNotificationAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromCanceled(new CancellationToken(true)));

        var eventGroup = new EventGroup();
        eventGroup.Add(new TestEvent(1));

        var rule = new TestRule(evt => true);
        component.Subscribe(recipient.Object, rule);

        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        // Act & Assert
        Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await component.TestPublishAsync(eventGroup, cancellationTokenSource.Token));
    }
}