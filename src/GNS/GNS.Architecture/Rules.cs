using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace GNS.Architecture
{
    #region Additional Rule Implementations

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

    #endregion

    #region Rule Factory for easier rule creation

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

    #endregion
}