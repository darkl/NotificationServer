using System;

namespace GNS.Architecture
{
    [Serializable]
    public class Notification
    {
        public Notification(EventGroup eventGroup, IRule rule)
        {
            EventGroup = eventGroup;
            Rule = rule;
        }

        public EventGroup EventGroup
        {
            get; 
            private set;
        }

        public IRule Rule
        {
            get; 
            private set;
        }
    }
}