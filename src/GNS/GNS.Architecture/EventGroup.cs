using System;
using System.Collections.Generic;
using System.Linq;

namespace GNS.Architecture
{
    [Serializable]
    public class EventGroup : List<IEvent>, IEvent
    {
        public EventGroup()
        {
        }

        protected EventGroup(IEnumerable<IEvent> collection) : base(collection)
        {
        }

        public object Clone()
        {
            return new EventGroup(this.Select(x => x.Clone())
                                      .OfType<IEvent>());
        }
    }
}