namespace GNS.Async;

[Serializable]
public class TypeRule : IRule
{
    private readonly Type _eventType;

    public TypeRule(Type eventType)
    {
        if (eventType == null)
            throw new ArgumentNullException(nameof(eventType));

        if (!typeof(IEvent).IsAssignableFrom(eventType))
            throw new ArgumentException("Type must implement IEvent", nameof(eventType));

        _eventType = eventType;
    }

    public bool IsActivated(IEvent evt)
    {
        if (evt == null)
            return false;

        return _eventType.IsInstanceOfType(evt);
    }

    public override bool Equals(object obj)
    {
        if (obj is TypeRule other)
        {
            return _eventType == other._eventType;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return _eventType.GetHashCode();
    }

    public object Clone()
    {
        return new TypeRule(this._eventType);
    }
}