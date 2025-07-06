namespace GNS.Async;

/// <summary>
/// A rule that negates another rule
/// </summary>
[Serializable]
public class NotRule : IRule
{
    private readonly IRule _innerRule;

    public NotRule(IRule rule)
    {
        _innerRule = rule ?? throw new ArgumentNullException(nameof(rule));
    }

    public bool IsActivated(IEvent evt)
    {
        return !_innerRule.IsActivated(evt);
    }

    public override bool Equals(object obj)
    {
        if (obj is NotRule other)
        {
            return _innerRule.Equals(other._innerRule);
        }
        return false;
    }

    public override int GetHashCode()
    {
        return ~_innerRule.GetHashCode();
    }

    public object Clone()
    {
        return new NotRule((IRule)this._innerRule.Clone());
    }
}