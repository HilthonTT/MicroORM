using System.Linq.Expressions;

namespace MicroORM.Querying;

/// <summary>Finds whether an expression references any of the given parameters (or, optionally, contains a subquery).</summary>
internal sealed class EntityFinder : ExpressionVisitor
{
    private readonly IReadOnlyCollection<ParameterExpression> _parameters;
    private readonly bool _subqueries;
    private bool _found;

    private EntityFinder(IReadOnlyCollection<ParameterExpression> parameters, bool subqueries)
    {
        _parameters = parameters;
        _subqueries = subqueries;
    }

    /// <summary>Whether <paramref name="node"/> depends on the row (or holds a subquery, when asked).</summary>
    /// <param name="node">The expression to search.</param>
    /// <param name="parameters">The parameters that make an expression depend on the row.</param>
    /// <param name="subqueries">
    /// Whether a subquery counts as found too, so it is translated rather than evaluated (which
    /// would run it while the outer query is being built).
    /// </param>
    public static bool Finds(Expression node, IReadOnlyCollection<ParameterExpression> parameters, bool subqueries)
    {
        var finder = new EntityFinder(parameters, subqueries);
        finder.Visit(node);
        return finder._found;
    }

    public override Expression? Visit(Expression? node) => _found ? node : base.Visit(node);

    protected override Expression VisitParameter(ParameterExpression node)
    {
        _found |= _parameters.Contains(node);
        return node;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        _found |= _subqueries && ExpressionTranslator.IsSubquery(node);
        return base.VisitMethodCall(node);
    }
}
