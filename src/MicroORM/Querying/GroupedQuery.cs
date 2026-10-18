using MicroORM.Dialects;
using MicroORM.Sessions;
using System.Globalization;
using System.Linq.Expressions;

namespace MicroORM.Querying;

/// <summary>
/// Rows grouped by a key, created by <see cref="Query{T}.GroupBy{TKey}"/>. Filter groups with
/// <see cref="Having"/>, sort and page them, then <see cref="Select{TResult}"/> the key and
/// aggregates. Like a query, it is immutable and runs nothing until a terminal method.
/// </summary>
/// <example><code>
/// var totals = await orders.Where(o =&gt; o.Paid)
///     .GroupBy(o =&gt; o.CustomerId)
///     .Having(g =&gt; g.Count() &gt; 1)
///     .OrderByDescending(g =&gt; g.Sum(o =&gt; o.Amount))
///     .Select(g =&gt; new { CustomerId = g.Key, Orders = g.Count(), Total = g.Sum(o =&gt; o.Amount) })
///     .ToListAsync();
/// </code></example>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="T">The entity type.</typeparam>
public sealed class GroupedQuery<TKey, T> : IProjectionSource
    where T : class
{
    private readonly Query<T> _source;
    private readonly IReadOnlyList<(string? Name, Sql.Sql Value, bool IsColumn)> _parts;
    private readonly bool _derived;
    private readonly GroupKey _key;
    private readonly IReadOnlyList<Sql.Sql> _having;
    private readonly IReadOnlyList<(Sql.Sql Expression, bool Descending)> _ordering;
    private readonly int? _skip;
    private readonly int? _take;

    internal GroupedQuery(Query<T> source, LambdaExpression key)
    {
        _source = source;
        _parts = ExpressionTranslator.GroupKeyParts(key, source.Map, source.Dialect);

        // Columns are grouped directly. A computed key is computed once, in a derived table, so that
        // GROUP BY and SELECT use the same column: PostgreSQL won't match "age / @p0" in one with
        // "age / @p1" in the other. A paged source is a derived table too, so it is grouped after
        // its paging, as in LINQ.
        _derived = source.IsPaged || _parts.Any(p => !p.IsColumn);
        _key = new GroupKey(_derived
            ? _parts.Select((p, i) => (p.Name, Sql.Sql.Identifier(KeyAlias(i)))).ToList()
            : _parts.Select(p => (p.Name, p.Value)).ToList());
        _having = [];
        _ordering = [];
    }

    private GroupedQuery(GroupedQuery<TKey, T> query, IReadOnlyList<Sql.Sql> having, IReadOnlyList<(Sql.Sql, bool)> ordering, int? skip, int? take)
    {
        _source = query._source;
        _parts = query._parts;
        _derived = query._derived;
        _key = query._key;
        _having = having;
        _ordering = ordering;
        _skip = skip;
        _take = take;
    }

    MicroSession IProjectionSource.Session => _source.Session;

    private SqlDialect Dialect => _source.Dialect;

    private bool IsPaged => _skip is not null || _take is not null;

    /// <summary>
    /// Keeps the groups matching a predicate over the key and aggregates (<c>HAVING</c>), such as
    /// <c>g =&gt; g.Count() &gt; 1</c>. Multiple calls are combined with <c>AND</c>. Filter rows before
    /// grouping with <see cref="Query{T}.Where(Expression{Func{T, bool}})"/>.
    /// </summary>
    public GroupedQuery<TKey, T> Having(Expression<Func<IGrouping<TKey, T>, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new(this, [.. _having, ExpressionTranslator.Predicate(predicate, _source.Map, Dialect, _key)], _ordering, _skip, _take);
    }

    /// <summary>Sorts the groups ascending, replacing any previous ordering: <c>g =&gt; g.Key</c> or <c>g =&gt; g.Count()</c>.</summary>
    public GroupedQuery<TKey, T> OrderBy<TOrder>(Expression<Func<IGrouping<TKey, T>, TOrder>> key) => Order(key, descending: false, replace: true);

    /// <summary>Sorts the groups descending, replacing any previous ordering.</summary>
    public GroupedQuery<TKey, T> OrderByDescending<TOrder>(Expression<Func<IGrouping<TKey, T>, TOrder>> key) => Order(key, descending: true, replace: true);

    /// <summary>Adds an ascending secondary sort key.</summary>
    public GroupedQuery<TKey, T> ThenBy<TOrder>(Expression<Func<IGrouping<TKey, T>, TOrder>> key) => Order(key, descending: false, replace: false);

    /// <summary>Adds a descending secondary sort key.</summary>
    public GroupedQuery<TKey, T> ThenByDescending<TOrder>(Expression<Func<IGrouping<TKey, T>, TOrder>> key) => Order(key, descending: true, replace: false);

    /// <summary>Skips a number of groups. Like LINQ, it applies after any earlier Skip or Take.</summary>
    public GroupedQuery<TKey, T> Skip(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var skip = (int)Math.Min((long)(_skip ?? 0) + count, int.MaxValue);
        return new(this, _having, _ordering, skip, _take is int take ? Math.Max(take - count, 0) : null);
    }

    /// <summary>Limits the number of groups. Like LINQ, it never raises an earlier, smaller Take.</summary>
    public GroupedQuery<TKey, T> Take(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new(this, _having, _ordering, _skip, Limit(count));
    }

    /// <summary>
    /// Selects values per group: the key (<c>g.Key</c>, or its members for a composite key) and
    /// aggregates over the group's rows: <c>g.Count()</c>, <c>g.Count(x =&gt; ...)</c>,
    /// <c>g.Sum(x =&gt; ...)</c>, <c>g.Min</c>, <c>g.Max</c> and <c>g.Average</c>.
    /// </summary>
    public Projection<T, TResult> Select<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new Projection<T, TResult>(this, ExpressionTranslator.SelectList(selector, _source.Map, Dialect, _key), ExpressionTranslator.SelectsSingleValue(selector));
    }

    /// <summary>Counts the groups.</summary>
    public async Task<long> CountAsync(CancellationToken cancellationToken = default) =>
        await _source.Session.ScalarCore<long>(true, CountSql(), cancellationToken).ConfigureAwait(false);

    /// <summary>Counts the groups.</summary>
    public long Count() => MicroSession.Sync(_source.Session.ScalarCore<long>(false, CountSql(), default));

    /// <inheritdoc />
    public override string ToString() => ((IProjectionSource)this).SelectSql(KeyList(), false, null, ordered: true).Render(Dialect).Text;

    Sql.Sql IProjectionSource.SelectSql(Sql.Sql selectList, bool distinct, int? limit, bool ordered)
    {
        if (distinct)
        {
            throw new NotSupportedException("Distinct can't be applied to a grouped query; each group is already one row.");
        }

        var take = limit is int count ? Limit(count) : _take;
        var sql = Sql.Sql.Empty.Append("SELECT ").Append(selectList).Append(From())
            .Append(" GROUP BY ").Append(Sql.Sql.Join(", ", _key.Parts.Select(p => Sql.Sql.Empty.Append(p.Value))));

        // Take(0) becomes an always-false HAVING with no ORDER BY or paging, as in Query.
        IReadOnlyList<Sql.Sql> having = take == 0 ? [.. _having, Sql.Sql.Raw("1 = 0")] : _having;
        if (having.Count > 0)
        {
            sql.Append(" HAVING ").Append(Sql.Sql.Join(" AND ", having));
        }

        if (take == 0)
        {
            return sql;
        }

        var paged = _skip is not null || take is not null;
        var orderBy = _ordering.Count > 0 && (ordered || paged);
        if (orderBy)
        {
            sql.Append(" ORDER BY ").Append(Sql.Sql.Join(", ", _ordering.Select(o => o.Descending ? Sql.Sql.Empty.Append(o.Expression).Append(" DESC") : Sql.Sql.Empty.Append(o.Expression))));
        }

        Dialect.AppendPaging(sql, _skip, take, orderBy);
        return sql;
    }

    private static string KeyAlias(int index) => "group_key" + index.ToString(CultureInfo.InvariantCulture);

    private int Limit(int count) => _take is int take ? Math.Min(take, count) : count;

    private Sql.Sql From()
    {
        if (!_derived)
        {
            return Sql.Sql.Empty.Append(" FROM ").AppendIdentifier(_source.Map.Table).Append(_source.WhereClause());
        }

        // Every column, so aggregates can use any of them, plus the computed key parts.
        var columns = _source.Map.Columns.Select(c => Sql.Sql.Identifier(c.Name))
            .Concat(_parts.Select((p, i) => Sql.Sql.Empty.Append(p.Value).Append(" AS ").AppendIdentifier(KeyAlias(i))));
        return Sql.Sql.Empty.Append(" FROM (").Append(_source.SelectSql(Sql.Sql.Join(", ", columns), ordered: false)).Append(") AS ").AppendIdentifier("grouped");
    }

    /// <summary>Selects the key parts, for counting groups and for <see cref="ToString"/>.</summary>
    private Sql.Sql KeyList() =>
        Sql.Sql.Join(", ", _key.Parts.Select((p, i) => Sql.Sql.Empty.Append(p.Value).Append(" AS ").AppendIdentifier(KeyAlias(i))));

    private Sql.Sql CountSql() =>
        Sql.Sql.Empty.Append("SELECT COUNT(*) FROM (").Append(((IProjectionSource)this).SelectSql(KeyList(), false, null, ordered: false)).Append(") AS ").AppendIdentifier("groups");

    private GroupedQuery<TKey, T> Order<TOrder>(Expression<Func<IGrouping<TKey, T>, TOrder>> key, bool descending, bool replace)
    {
        ArgumentNullException.ThrowIfNull(key);

        // Ordering by a whole composite key orders by each of its parts.
        IEnumerable<Sql.Sql> expressions = key.Body is MemberExpression { Member.Name: "Key" } member && member.Expression == key.Parameters[0] && _key.IsComposite
            ? _key.Parts.Select(p => p.Value)
            : [ExpressionTranslator.Value(key, _source.Map, Dialect, _key)];
        var added = expressions.Select(e => (e, descending)).ToList();
        return new(this, _having, replace ? added : [.. _ordering, .. added], _skip, _take);
    }
}
