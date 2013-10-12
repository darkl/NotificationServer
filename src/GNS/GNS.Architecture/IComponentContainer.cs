using System.Collections.Generic;

namespace GNS.Architecture
{
    public interface IComponentContainer : ICollection<IComponent>
    {
        void RemoveByName(string name);
        IComponent GetByName(string name);
    }
}