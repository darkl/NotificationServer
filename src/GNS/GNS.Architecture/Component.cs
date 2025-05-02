using System;

namespace GNS.Architecture
{
    public abstract partial class Component
    {
        private string _uniqueName;
        // Primary subscription index by rule for efficient matching
        private readonly Dictionary<IRule, HashSet<IRecipient>> _subscriptionsByRule = new Dictionary<IRule, HashSet<IRecipient>>();
        // Secondary index by recipient for efficient unsubscribe operations
        private readonly Dictionary<IRecipient, HashSet<IRule>> _rulesByRecipient = new Dictionary<IRecipient, HashSet<IRule>>();

        protected Component(string uniqueName)
        {
            if (string.IsNullOrEmpty(uniqueName))
                throw new ArgumentException("Component name cannot be null or empty", nameof(uniqueName));

            _uniqueName = uniqueName;
        }

        public string UniqueName => _uniqueName;

        protected void Publish(EventGroup eventGroup)
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

                if (matchedEvents.Count > 0 && _subscriptionsByRule.TryGetValue(rule, out HashSet<IRecipient> recipients))
                {
                    foreach (IRecipient recipient in recipients)
                    {
                        recipient.HandleNotification(new Notification(matchedEvents, rule));
                    }
                }
            }
        }

        public void HandleNotification(Notification notification)
        {
            if (notification == null)
                throw new ArgumentNullException(nameof(notification));

            // Clone the event group to prevent modification by other recipients
            EventGroup eventGroupClone = (EventGroup)notification.EventGroup.Clone();

            // In a real implementation, this would dispatch to the component's thread
            // For simplicity, we'll directly call Consume here
            Consume(eventGroupClone);
        }

        protected abstract void Consume(EventGroup eventGroup);

        public void Subscribe(IRecipient recipient, IRule rule)
        {
            if (recipient == null)
                throw new ArgumentNullException(nameof(recipient));
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));

            // Add to rule-based index
            if (!_subscriptionsByRule.TryGetValue(rule, out HashSet<IRecipient> recipients))
            {
                recipients = new HashSet<IRecipient>();
                _subscriptionsByRule.Add(rule, recipients);
            }
            recipients.Add(recipient);

            // Add to recipient-based index
            if (!_rulesByRecipient.TryGetValue(recipient, out HashSet<IRule> rules))
            {
                rules = new HashSet<IRule>();
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
            if (_subscriptionsByRule.TryGetValue(rule, out HashSet<IRecipient> recipients))
            {
                recipients.Remove(recipient);
                if (recipients.Count == 0)
                {
                    _subscriptionsByRule.Remove(rule);
                }
            }

            // Remove from recipient-based index
            if (_rulesByRecipient.TryGetValue(recipient, out HashSet<IRule> rules))
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
            if (_rulesByRecipient.TryGetValue(recipient, out HashSet<IRule> rules))
            {
                // Make a copy to avoid modification during enumeration
                var rulesCopy = rules.ToList();

                // Remove recipient from each rule's subscribers
                foreach (IRule rule in rulesCopy)
                {
                    if (_subscriptionsByRule.TryGetValue(rule, out HashSet<IRecipient> recipients))
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
    }

}