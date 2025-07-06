namespace GNS.Async;

/// <summary>
/// A rule that combines other rules with logical operators
/// </summary>
[Serializable]
public class CompositeRule : IRule
{
    private readonly List<IRule> _rules;
    private readonly LogicalOperator _operator;

    public enum LogicalOperator
    {
        And,
        Or
    }

    public CompositeRule(LogicalOperator op, params IRule[] rules)
    {
        if (rules == null || rules.Length == 0)
            throw new ArgumentException("At least one rule must be provided", nameof(rules));

        _rules = new List<IRule>(rules);
        _operator = op;
    }

    public bool IsActivated(IEvent evt)
    {
        if (evt == null)
            return false;

        switch (_operator)
        {
            case LogicalOperator.And:
                // All rules must match
                foreach (var rule in _rules)
                {
                    if (!rule.IsActivated(evt))
                        return false;
                }
                return true;

            case LogicalOperator.Or:
                // At least one rule must match
                foreach (var rule in _rules)
                {
                    if (rule.IsActivated(evt))
                        return true;
                }
                return false;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public override bool Equals(object obj)
    {
        if (obj is CompositeRule other && _operator == other._operator && _rules.Count == other._rules.Count)
        {
            // Check that all rules match (order-dependent)
            for (int i = 0; i < _rules.Count; i++)
            {
                if (!_rules[i].Equals(other._rules[i]))
                    return false;
            }
            return true;
        }
        return false;
    }

    public override int GetHashCode()
    {
        int hash = 17;
        hash = hash * 31 + _operator.GetHashCode();

        foreach (var rule in _rules)
        {
            hash = hash * 31 + rule.GetHashCode();
        }

        return hash;
    }

    public object Clone()
    {
        return new CompositeRule(this._operator, this._rules.Select(x => (IRule)x.Clone()).ToArray());
    }
}