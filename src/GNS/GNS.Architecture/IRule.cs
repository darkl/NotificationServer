using System;

namespace GNS.Architecture
{
    public interface IRule : ICloneable
    {
        bool IsActivated(IEvent @event);
    }
}