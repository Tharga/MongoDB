using System;
using System.Linq.Expressions;

namespace Tharga.MongoDB;

public static class ExpressionExtensions
{
    public static Expression<Func<T, bool>> AndAlso<T>(this Expression<Func<T, bool>> expr1, Expression<Func<T, bool>> expr2)
    {
        var parameter = Expression.Parameter(typeof(T));

        var left = ReplaceParameter(expr1.Body, expr1.Parameters[0], parameter);
        var right = ReplaceParameter(expr2.Body, expr2.Parameters[0], parameter);

        var body = Expression.AndAlso(left, right);
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static Expression ReplaceParameter(Expression body, ParameterExpression toReplace, ParameterExpression replaceWith)
    {
        return new ParameterReplacer(toReplace, replaceWith).Visit(body);
    }

    private class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _from, _to;
        public ParameterReplacer(ParameterExpression from, ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == _from ? _to : base.VisitParameter(node);
        }
    }
}