using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System;
using GNS.Architecture;

namespace GNS.Tests
{
    [TestFixture]
    public class ComponentPublishingTests
    {
        // Test implementation of Component class for testing
        private class TestComponent : Component
        {
            public List<EventGroup> ConsumedEventGroups { get; } = new List<EventGroup>();

            public TestComponent(string name) : base(name) { }

            protected override void Consume(EventGroup eventGroup)
            {
                ConsumedEventGroups.Add(eventGroup);
            }

            // Expose protected Publish method for testing
            public void TestPublish(EventGroup eventGroup)
            {
                Publish(eventGroup);
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
                return new TestRule(this.Matcher);
            }
        }

        [Test]
        public void Publish_NoEventGroupMaxSize_SendsAllEventsInOneGroup()
        {
            // Arrange
            var component = new TestComponent("TestComponent");
            var recipient = new Mock<IRecipient>();
            var capturedNotifications = new List<Notification>();

            recipient.Setup(r => r.HandleNotification(It.IsAny<Notification>()))
                .Callback<Notification>(n => capturedNotifications.Add(n));

            var eventGroup = new EventGroup();
            for (int i = 1; i <= 10; i++)
            {
                eventGroup.Add(new TestEvent(i));
            }

            var rule = new TestRule(evt => true); // Match all events

            component.Subscribe(recipient.Object, rule);

            // Act
            component.TestPublish(eventGroup);

            // Assert
            Assert.That(capturedNotifications.Count, Is.EqualTo(1));
            Assert.That(capturedNotifications[0].EventGroup.Count, Is.EqualTo(10));
        }

        [Test]
        public void Publish_WithEventGroupMaxSize_SplitsEventsIntoChunks()
        {
            // Arrange
            var component = new TestComponent("TestComponent");
            component.EventGroupMaxSize = 3; // Limit to 3 events per group

            var recipient = new Mock<IRecipient>();
            var capturedNotifications = new List<Notification>();

            recipient.Setup(r => r.HandleNotification(It.IsAny<Notification>()))
                .Callback<Notification>(n => capturedNotifications.Add(n));

            var eventGroup = new EventGroup();
            for (int i = 1; i <= 10; i++)
            {
                eventGroup.Add(new TestEvent(i));
            }

            var rule = new TestRule(evt => true); // Match all events

            component.Subscribe(recipient.Object, rule);

            // Act
            component.TestPublish(eventGroup);

            // Assert
            Assert.That(capturedNotifications.Count, Is.EqualTo(4)); // 10 events / 3 per group = 4 groups (3+3+3+1)
            Assert.That(capturedNotifications[0].EventGroup.Count, Is.EqualTo(3));
            Assert.That(capturedNotifications[1].EventGroup.Count, Is.EqualTo(3));
            Assert.That(capturedNotifications[2].EventGroup.Count, Is.EqualTo(3));
            Assert.That(capturedNotifications[3].EventGroup.Count, Is.EqualTo(1));
        }

        [Test]
        public void Publish_WithEventGroupMaxSizeAndMultipleRecipients_SendsChunksToAllRecipients()
        {
            // Arrange
            var component = new TestComponent("TestComponent");
            component.EventGroupMaxSize = 4; // Limit to 4 events per group

            var recipient1 = new Mock<IRecipient>();
            var recipient2 = new Mock<IRecipient>();

            var capturedNotifications1 = new List<Notification>();
            var capturedNotifications2 = new List<Notification>();

            recipient1.Setup(r => r.HandleNotification(It.IsAny<Notification>()))
                .Callback<Notification>(n => capturedNotifications1.Add(n));
            recipient2.Setup(r => r.HandleNotification(It.IsAny<Notification>()))
                .Callback<Notification>(n => capturedNotifications2.Add(n));

            var eventGroup = new EventGroup();
            for (int i = 1; i <= 10; i++)
            {
                eventGroup.Add(new TestEvent(i));
            }

            var rule = new TestRule(evt => true); // Match all events

            component.Subscribe(recipient1.Object, rule);
            component.Subscribe(recipient2.Object, rule);

            // Act
            component.TestPublish(eventGroup);

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
        public void Publish_WithEventGroupMaxSizeAndMultipleRules_FiltersCorrectly()
        {
            // Arrange
            var component = new TestComponent("TestComponent");
            component.EventGroupMaxSize = 2; // Limit to 2 events per group

            var recipient = new Mock<IRecipient>();
            var capturedNotifications = new List<Notification>();

            recipient.Setup(r => r.HandleNotification(It.IsAny<Notification>()))
                .Callback<Notification>(n => capturedNotifications.Add(n));

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
            component.TestPublish(eventGroup);

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
        public void Publish_WithZeroEventGroupMaxSize_DoesNotSplitEvents()
        {
            // Arrange
            var component = new TestComponent("TestComponent");
            component.EventGroupMaxSize = 0; // Explicitly set to 0 (default)

            var recipient = new Mock<IRecipient>();
            var capturedNotifications = new List<Notification>();

            recipient.Setup(r => r.HandleNotification(It.IsAny<Notification>()))
                .Callback<Notification>(n => capturedNotifications.Add(n));

            var eventGroup = new EventGroup();
            for (int i = 1; i <= 10; i++)
            {
                eventGroup.Add(new TestEvent(i));
            }

            var rule = new TestRule(evt => true); // Match all events

            component.Subscribe(recipient.Object, rule);

            // Act
            component.TestPublish(eventGroup);

            // Assert
            Assert.That(capturedNotifications.Count, Is.EqualTo(1));
            Assert.That(capturedNotifications[0].EventGroup.Count, Is.EqualTo(10));
        }

        [Test]
        public void Publish_WithNegativeEventGroupMaxSize_DoesNotSplitEvents()
        {
            // Arrange
            var component = new TestComponent("TestComponent");
            component.EventGroupMaxSize = -5; // Negative value

            var recipient = new Mock<IRecipient>();
            var capturedNotifications = new List<Notification>();

            recipient.Setup(r => r.HandleNotification(It.IsAny<Notification>()))
                .Callback<Notification>(n => capturedNotifications.Add(n));

            var eventGroup = new EventGroup();
            for (int i = 1; i <= 10; i++)
            {
                eventGroup.Add(new TestEvent(i));
            }

            var rule = new TestRule(evt => true); // Match all events

            component.Subscribe(recipient.Object, rule);

            // Act
            component.TestPublish(eventGroup);

            // Assert
            Assert.That(capturedNotifications.Count, Is.EqualTo(1));
            Assert.That(capturedNotifications[0].EventGroup.Count, Is.EqualTo(10));
        }

        [Test]
        public void Publish_WithEventGroupMaxSizeExactlyMatchingEventCount_CreatesSingleChunk()
        {
            // Arrange
            var component = new TestComponent("TestComponent");
            component.EventGroupMaxSize = 10; // Match exactly the number of events

            var recipient = new Mock<IRecipient>();
            var capturedNotifications = new List<Notification>();

            recipient.Setup(r => r.HandleNotification(It.IsAny<Notification>()))
                .Callback<Notification>(n => capturedNotifications.Add(n));

            var eventGroup = new EventGroup();
            for (int i = 1; i <= 10; i++)
            {
                eventGroup.Add(new TestEvent(i));
            }

            var rule = new TestRule(evt => true); // Match all events

            component.Subscribe(recipient.Object, rule);

            // Act
            component.TestPublish(eventGroup);

            // Assert
            Assert.AreEqual(1, capturedNotifications.Count);
            Assert.AreEqual(10, capturedNotifications[0].EventGroup.Count);
        }

        [Test]
        public void Publish_WithNoEvents_DoesNotSendNotifications()
        {
            // Arrange
            var component = new TestComponent("TestComponent");
            component.EventGroupMaxSize = 3;

            var recipient = new Mock<IRecipient>();
            var capturedNotifications = new List<Notification>();

            recipient.Setup(r => r.HandleNotification(It.IsAny<Notification>()))
                .Callback<Notification>(n => capturedNotifications.Add(n));

            var emptyEventGroup = new EventGroup();

            var rule = new TestRule(evt => true);

            component.Subscribe(recipient.Object, rule);

            // Act
            component.TestPublish(emptyEventGroup);

            // Assert
            Assert.That(capturedNotifications.Count, Is.EqualTo(0));
        }

        [Test]
        public void Publish_WithNoMatchingEvents_DoesNotSendNotifications()
        {
            // Arrange
            var component = new TestComponent("TestComponent");
            component.EventGroupMaxSize = 3;

            var recipient = new Mock<IRecipient>();
            var capturedNotifications = new List<Notification>();

            recipient.Setup(r => r.HandleNotification(It.IsAny<Notification>()))
                .Callback<Notification>(n => capturedNotifications.Add(n));

            var eventGroup = new EventGroup();
            for (int i = 1; i <= 10; i++)
            {
                eventGroup.Add(new TestEvent(i));
            }

            var noMatchRule = new TestRule(evt => false); // Never matches

            component.Subscribe(recipient.Object, noMatchRule);

            // Act
            component.TestPublish(eventGroup);

            // Assert
            Assert.That(capturedNotifications.Count, Is.EqualTo(0));
        }
    }
}