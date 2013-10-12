using System;

namespace GNS.Architecture
{
    public interface IEvent : ICloneable, IValidatable
    {
        // Crap
        string ToString();
    }

    public interface IValidatable
    {
        // Crap
    }
}