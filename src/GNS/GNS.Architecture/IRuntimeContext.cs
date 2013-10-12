namespace GNS.Architecture
{
    public interface IRuntimeContext
    {
        string ServerName { get; }
        IComponentContainer Components { get; }
        IServer Server { get; }
        IInputPort InputPort { get; }
        IOutputPort OutputPort { get; }
    }
}