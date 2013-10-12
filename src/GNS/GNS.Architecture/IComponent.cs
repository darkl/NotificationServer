namespace GNS.Architecture
{
    public interface IComponent : IStateDrivenEntity
    {
        string UniqueName { get; }

        IRuntimeContext RuntimeContext { get; set; }
    }
}