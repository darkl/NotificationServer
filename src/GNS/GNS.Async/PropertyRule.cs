using System.Linq.Expressions;
using System.Reflection;

namespace GNS.Async;

/// <summary>
/// A rule that matches events based on a property value
/// </summary>
[Serializable]
public class PropertyRule<TEvent, TProperty> : IRule where TEvent : IEvent
{
    private readonly string _propertyName;
    private readonly TProperty _expectedValue;
    private readonly Func<TEvent, TProperty> _propertyAccessor;
    private readonly Predicate<TProperty> _predicate;

    /// <summary>
    /// Creates a rule that matches events with a specific property value
    /// </summary>
    public PropertyRule(Expression<Func<TEvent, TProperty>> propertyExpression, TProperty expectedValue)
    {
        if (propertyExpression == null)
            throw new ArgumentNullException(nameof(propertyExpression));

        // Extract property name from the expression
        if (propertyExpression.Body is MemberExpression memberExpr &&
            memberExpr.Member is PropertyInfo)
        {
            _propertyName = memberExpr.Member.Name;
        }
        else
        {
            throw new ArgumentException("Expression must be a property access expression", nameof(propertyExpression));
        }

        _expectedValue = expectedValue;
        _propertyAccessor = propertyExpression.Compile();
        _predicate = value => EqualityComparer<TProperty>.Default.Equals(value, _expectedValue);
    }

    /// <summary>
    /// Creates a rule that matches events with a property value that satisfies a predicate
    /// </summary>
    public PropertyRule(Expression<Func<TEvent, TProperty>> propertyExpression, Predicate<TProperty> predicate)
    {
        if (propertyExpression == null)
            throw new ArgumentNullException(nameof(propertyExpression));
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        // Extract property name from the expression
        if (propertyExpression.Body is MemberExpression memberExpr &&
            memberExpr.Member is PropertyInfo)
        {
            _propertyName = memberExpr.Member.Name;
        }
        else
        {
            throw new ArgumentException("Expression must be a property access expression", nameof(propertyExpression));
        }

        _expectedValue = default; // Not used with predicate
        _propertyAccessor = propertyExpression.Compile();
        _predicate = predicate;
    }

    public bool IsActivated(IEvent evt)
    {
        if (evt == null)
            return false;

        // Check if the event is of the right type
        if (evt is TEvent typedEvent)
        {
            try
            {
                // Get the property value and check it with the predicate
                TProperty value = _propertyAccessor(typedEvent);
                return _predicate(value);
            }
            catch (Exception)
            {
                // If property access fails, the rule doesn't match
                return false;
            }
        }

        return false;
    }

    public override bool Equals(object obj)
    {
        if (obj is PropertyRule<TEvent, TProperty> other)
        {
            return _propertyName == other._propertyName &&
                   EqualityComparer<TProperty>.Default.Equals(_expectedValue, other._expectedValue);
        }
        return false;
    }

    public override int GetHashCode()
    {
        int hash = 17;
        hash = hash * 31 + _propertyName.GetHashCode();
        hash = hash * 31 + (_expectedValue != null ? _expectedValue.GetHashCode() : 0);
        return hash;
    }

    public object Clone()
    {
        throw new NotImplementedException();
    }
}