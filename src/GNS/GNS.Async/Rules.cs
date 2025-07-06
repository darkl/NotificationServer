using System.Linq.Expressions;

namespace GNS.Async;

/// <summary>
/// Helper class to create rules with a fluent syntax
/// </summary>
public static class Rules
{
    public static TypeRule OfType<T>() where T : IEvent
    {
        return new TypeRule(typeof(T));
    }

    public static PropertyRule<TEvent, TProperty> WithProperty<TEvent, TProperty>(
        Expression<Func<TEvent, TProperty>> propertyExpression,
        TProperty expectedValue) where TEvent : IEvent
    {
        return new PropertyRule<TEvent, TProperty>(propertyExpression, expectedValue);
    }

    public static PropertyRule<TEvent, TProperty> WithProperty<TEvent, TProperty>(
        Expression<Func<TEvent, TProperty>> propertyExpression,
        Predicate<TProperty> predicate) where TEvent : IEvent
    {
        return new PropertyRule<TEvent, TProperty>(propertyExpression, predicate);
    }

    public static CompositeRule And(params IRule[] rules)
    {
        return new CompositeRule(CompositeRule.LogicalOperator.And, rules);
    }

    public static CompositeRule Or(params IRule[] rules)
    {
        return new CompositeRule(CompositeRule.LogicalOperator.Or, rules);
    }

    public static NotRule Not(IRule rule)
    {
        return new NotRule(rule);
    }
}