namespace GNS.Architecture
{
    public abstract class Logic : Component
    {
        protected override void Consume(EventGroup eventGroup)
        {
            EventGroup processed = ProcessEventGroup(eventGroup);

            if (processed != null && processed.Count > 0)
            {
                Publish(processed);
            }
        }

        public virtual EventGroup ProcessEventGroup(EventGroup eventGroup)
        {
            EventGroup result = new EventGroup();

            foreach (IEvent currentEvent in eventGroup)
            {
                IEvent processed = ProcessEvent(currentEvent);

                if (processed != null)
                {
                    EventGroup group = processed as EventGroup;
                    
                    if (group != null)
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

        public abstract IEvent ProcessEvent(IEvent eventToProcess);
    }
}