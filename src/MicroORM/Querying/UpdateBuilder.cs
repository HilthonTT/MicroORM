using MicroORM.Dialects;
using MicroORM.Mapping;
using System.Linq.Expressions;

namespace MicroORM.Querying;

/// <summary>Collects <c>SET</c> assignments for <see cref="Query{T}.ExecuteUpdateAsync"/>.</summary>
/// <typeparam name="T">The entity type.</typeparam>
public sealed class UpdateBuilder<T>
    where T : class
{
    private readonly EntityMap _map;
    private readonly SqlDialect _dialect;

    internal UpdateBuilder(EntityMap map, SqlDialect dialect)
    {
        _map = map;
        _dialect = dialect;
    }

    internal List<Sql.Sql> Assignments { get; } = [];

    /// <summary>Sets a column to a value.</summary>
    public UpdateBuilder<T> Set<TProperty>(Expression<Func<T, TProperty>> column, TProperty value)
    {
        ArgumentNullException.ThrowIfNull(column);
        var map = ExpressionTranslator.Column(column, _map);
        Assignments.Add(Sql.Sql.Identifier(map.Name).Append(" = ").AppendParameter(value, typeof(TProperty)));
        return this;
    }

    /// <summary>Sets a column to an expression of the row, such as <c>u =&gt; u.Visits + 1</c>.</summary>
    public UpdateBuilder<T> SetFrom<TProperty>(Expression<Func<T, TProperty>> column, Expression<Func<T, TProperty>> value)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(value);
        var map = ExpressionTranslator.Column(column, _map);
        Assignments.Add(Sql.Sql.Identifier(map.Name).Append(" = ").Append(ExpressionTranslator.Value(value, _map, _dialect)));
        return this;
    }
}
