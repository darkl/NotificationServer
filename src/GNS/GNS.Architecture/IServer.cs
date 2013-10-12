namespace GNS.Architecture
{
    public interface IServer : IStateDrivenEntity
    {
        IRuntimeContext RuntimeContext { get; }
    }
}