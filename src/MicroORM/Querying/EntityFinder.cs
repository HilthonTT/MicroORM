using System.Linq.Expressions;

namespace MicroORM.Querying;

internal sealed class EntityFinder : ExpressionVisitor
{
    private readonly ParameterExpression _entity;
    private bool _found;

    private EntityFinder(ParameterExpression entity) => _entity = entity;

    public static bool Finds(Expression node, ParameterExpression entity)
    {
        var finder = new EntityFinder(entity);
        finder.Visit(node);
        return finder._found;
    }

    public override Expression? Visit(Expression? node) => _found ? node : base.Visit(node);

    protected override Expression VisitParameter(ParameterExpression node)
    {
        _found |= node == _entity;
        return node;
    }
}
