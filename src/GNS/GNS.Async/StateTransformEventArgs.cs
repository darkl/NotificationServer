namespace GNS.Async;

public class StateTransformEventArgs : EventArgs
{
    public State PreviousState { get; set; }
    public State NextState { get; set; }
}