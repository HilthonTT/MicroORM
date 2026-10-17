using MicroORM.Dialects;
using MicroORM.Mapping;
using MicroORM.Sessions;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace MicroORM.Querying;

/// <summary>
/// An immutable, composable query over one table. Each method returns a new query; nothing runs
/// until a terminal method such as <see cref="ToListAsync"/> (or its synchronous twin
/// <see cref="ToList"/>) is called.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public class Query<T>
    where T : class
{
    private readonly IReadOnlyList<Sql.Sql> _filters;
    private readonly IReadOnlyList<(Sql.Sql Expression, bool Descending)> _ordering;
    private readonly int? _skip;
    private readonly int? _take;

    internal Query(MicroSession session, EntityMap map)
        : this(session, map, [], [], null, null)
    {
    }

    private Query(MicroSession session, EntityMap map, IReadOnlyList<Sql.Sql> filters, IReadOnlyList<(Sql.Sql, bool)> ordering, int? skip, int? take)
    {
        Session = session;
        Map = map;
        _filters = filters;
        _ordering = ordering;
        _skip = skip;
        _take = take;
    }

    /// <summary>Gets the mapping of <typeparamref name="T"/>.</summary>
    public EntityMap Map { get; }

    internal MicroSession Session { get; }

    private SqlDialect Dialect => Session.Dialect;

    private bool IsPaged => _skip is not null || _take is not null;

    // ---- Composition ---------------------------------------------------------------------------

    /// <summary>Filters rows with a translated predicate. Multiple calls are combined with <c>AND</c>.</summary>
    public Query<T> Where(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Filter(ExpressionTranslator.Predicate(predicate, Map, Dialect));
    }

    /// <summary>Filters rows with a SQL condition, for anything predicates cannot express.</summary>
    /// <example><code>users.Where($"lower(email) = lower({email})")</code></example>
    public Query<T> Where(Sql.Sql condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        // Parenthesized so an OR in the condition can't escape the AND that joins it to other filters.
        return condition.IsEmpty ? this : Filter(Sql.Sql.Empty.Append("(").Append(condition).Append(")"));
    }

    /// <summary>Sorts ascending by a key, replacing any previous ordering.</summary>
    public Query<T> OrderBy<TKey>(Expression<Func<T, TKey>> key) => Order(key, descending: false, replace: true);

    /// <summary>Sorts descending by a key, replacing any previous ordering.</summary>
    public Query<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> key) => Order(key, descending: true, replace: true);

    /// <summary>Adds an ascending secondary sort key.</summary>
    public Query<T> ThenBy<TKey>(Expression<Func<T, TKey>> key) => Order(key, descending: false, replace: false);

    /// <summary>Adds a descending secondary sort key.</summary>
    public Query<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> key) => Order(key, descending: true, replace: false);

    /// <summary>Skips a number of rows.</summary>
    public Query<T> Skip(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new(Session, Map, _filters, _ordering, count, _take);
    }

    /// <summary>Limits the number of rows.</summary>
    public Query<T> Take(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new(Session, Map, _filters, _ordering, _skip, count);
    }

    /// <summary>
    /// Selects only some values per row: one value (<c>x =&gt; x.Name</c>), an anonymous type
    /// (<c>x =&gt; new { x.Id, Upper = x.Name.ToUpper() }</c>), a record or a DTO.
    /// </summary>
    public Projection<T, TResult> Select<TResult>(Expression<Func<T, TResult>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new Projection<T, TResult>(this, ExpressionTranslator.SelectList(selector, Map, Dialect));
    }

    /// <summary>Caps the number of rows without raising a smaller <see cref="Take"/> already set.</summary>
    internal Query<T> Limit(int count) =>
        new(Session, Map, _filters, _ordering, _skip, _take is int take ? Math.Min(take, count) : count);

    private Query<T> Filter(Sql.Sql condition) => new(Session, Map, [.. _filters, condition], _ordering, _skip, _take);

    // ---- Rows ----------------------------------------------------------------------------------

    /// <summary>Runs the query and returns every row.</summary>
    public Task<List<T>> ToListAsync(CancellationToken cancellationToken = default) =>
        Session.QueryCore<T>(true, ToSql(), null, cancellationToken).AsTask();

    /// <summary>Runs the query and returns every row.</summary>
    public List<T> ToList() => MicroSession.Sync(Session.QueryCore<T>(false, ToSql(), null, default));

    /// <summary>Runs the query and yields rows as they are read.</summary>
    public IAsyncEnumerable<T> StreamAsync(CancellationToken cancellationToken = default) => Session.StreamAsync<T>(ToSql(), cancellationToken);

    /// <summary>Runs the query and yields rows as they are read.</summary>
    public IEnumerable<T> Stream() => Session.Stream<T>(ToSql());

    /// <summary>Returns the first row, or <see langword="null"/>.</summary>
    public async Task<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default) =>
        MicroSession.First(await Session.QueryCore<T>(true, Limit(1).ToSql(), 1, cancellationToken).ConfigureAwait(false));

    /// <summary>Returns the first row, or <see langword="null"/>.</summary>
    public T? FirstOrDefault() => MicroSession.First(MicroSession.Sync(Session.QueryCore<T>(false, Limit(1).ToSql(), 1, default)));

    /// <summary>Returns the first row matching <paramref name="predicate"/>, or <see langword="null"/>.</summary>
    public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        Where(predicate).FirstOrDefaultAsync(cancellationToken);

    /// <summary>Returns the first row matching <paramref name="predicate"/>, or <see langword="null"/>.</summary>
    public T? FirstOrDefault(Expression<Func<T, bool>> predicate) => Where(predicate).FirstOrDefault();

    /// <summary>Returns the only row; throws when there are none or several.</summary>
    public async Task<T> SingleAsync(CancellationToken cancellationToken = default) =>
        MicroSession.Single(await Session.QueryCore<T>(true, Limit(2).ToSql(), 2, cancellationToken).ConfigureAwait(false), orDefault: false)!;

    /// <summary>Returns the only row; throws when there are none or several.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Matches LINQ's Single.")]
    public T Single() => MicroSession.Single(MicroSession.Sync(Session.QueryCore<T>(false, Limit(2).ToSql(), 2, default)), orDefault: false)!;

    // ---- Aggregates ----------------------------------------------------------------------------

    /// <summary>Counts the rows.</summary>
    public async Task<long> CountAsync(CancellationToken cancellationToken = default) =>
        await Session.ScalarCore<long>(true, AggregateSql(Sql.Sql.Raw("COUNT(*)")), cancellationToken).ConfigureAwait(false);

    /// <summary>Counts the rows.</summary>
    public long Count() => MicroSession.Sync(Session.ScalarCore<long>(false, AggregateSql(Sql.Sql.Raw("COUNT(*)")), default));

    /// <summary>Returns whether any row matches.</summary>
    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        await Session.ScalarCore<bool>(true, AnySql(), cancellationToken).ConfigureAwait(false);

    /// <summary>Returns whether any row matches.</summary>
    public bool Any() => MicroSession.Sync(Session.ScalarCore<bool>(false, AnySql(), default));

    /// <summary>Sums a value over the rows; an empty set sums to zero.</summary>
    public async Task<TResult> SumAsync<TResult>(Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default) =>
        (await Session.ScalarCore<TResult>(true, Aggregate("SUM", selector), cancellationToken).ConfigureAwait(false))!;

    /// <summary>Sums a value over the rows; an empty set sums to zero.</summary>
    public TResult Sum<TResult>(Expression<Func<T, TResult>> selector) =>
        MicroSession.Sync(Session.ScalarCore<TResult>(false, Aggregate("SUM", selector), default))!;

    /// <summary>Returns the smallest value, or default (<see langword="null"/> for nullable types) when there are no rows.</summary>
    public Task<TResult?> MinAsync<TResult>(Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default) =>
        Session.ScalarCore<TResult>(true, Aggregate("MIN", selector), cancellationToken).AsTask();

    /// <summary>Returns the smallest value, or default when there are no rows.</summary>
    public TResult? Min<TResult>(Expression<Func<T, TResult>> selector) =>
        MicroSession.Sync(Session.ScalarCore<TResult>(false, Aggregate("MIN", selector), default));

    /// <summary>Returns the largest value, or default (<see langword="null"/> for nullable types) when there are no rows.</summary>
    public Task<TResult?> MaxAsync<TResult>(Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default) =>
        Session.ScalarCore<TResult>(true, Aggregate("MAX", selector), cancellationToken).AsTask();

    /// <summary>Returns the largest value, or default when there are no rows.</summary>
    public TResult? Max<TResult>(Expression<Func<T, TResult>> selector) =>
        MicroSession.Sync(Session.ScalarCore<TResult>(false, Aggregate("MAX", selector), default));

    /// <summary>Returns the average as a <see cref="double"/> (integers are not truncated), or <see langword="null"/> when there are no rows.</summary>
    public Task<double?> AverageAsync<TResult>(Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default) =>
        Session.ScalarCore<double?>(true, Aggregate("AVG", selector), cancellationToken).AsTask();

    /// <summary>Returns the average as a <see cref="double"/>, or <see langword="null"/> when there are no rows.</summary>
    public double? Average<TResult>(Expression<Func<T, TResult>> selector) =>
        MicroSession.Sync(Session.ScalarCore<double?>(false, Aggregate("AVG", selector), default));

    // ---- Set-based writes ----------------------------------------------------------------------

    /// <summary>Deletes every matching row in one statement. Without a filter, deletes the whole table.</summary>
    /// <returns>The number of deleted rows.</returns>
    public Task<int> ExecuteDeleteAsync(CancellationToken cancellationToken = default) =>
        Session.ExecuteCore(true, DeleteSql(), cancellationToken).AsTask();

    /// <summary>Deletes every matching row in one statement. Without a filter, deletes the whole table.</summary>
    public int ExecuteDelete() => MicroSession.Sync(Session.ExecuteCore(false, DeleteSql(), default));

    /// <summary>Updates every matching row in one statement.</summary>
    /// <example><code>await users.Where(u =&gt; u.LastSeen &lt; cutoff).ExecuteUpdateAsync(s =&gt; s.Set(u =&gt; u.Active, false));</code></example>
    /// <returns>The number of updated rows.</returns>
    public Task<int> ExecuteUpdateAsync(Action<UpdateBuilder<T>> set, CancellationToken cancellationToken = default) =>
        Session.ExecuteCore(true, UpdateSql(set), cancellationToken).AsTask();

    /// <summary>Updates every matching row in one statement.</summary>
    public int ExecuteUpdate(Action<UpdateBuilder<T>> set) => MicroSession.Sync(Session.ExecuteCore(false, UpdateSql(set), default));

    // ---- SQL -----------------------------------------------------------------------------------

    /// <summary>Builds the <c>SELECT</c> statement without running it.</summary>
    public Sql.Sql ToSql() => SelectSql(Sql.Sql.Join(", ", Map.Columns.Select(c => Sql.Sql.Identifier(c.Name))));

    /// <inheritdoc />
    public override string ToString() => ToSql().Render(Dialect).Text;

    internal Sql.Sql SelectSql(Sql.Sql selectList)
    {
        // Take(0) becomes an always-false filter with no ORDER BY or paging: SQL Server rejects
        // "FETCH NEXT 0 ROWS", and ORDER BY without paging inside a subquery (Count, Any).
        var sql = Sql.Sql.Empty.Append("SELECT ").Append(selectList).Append(" FROM ").AppendIdentifier(Map.Table);
        if (_take == 0)
        {
            return sql.Append(WhereClause([.. _filters, Sql.Sql.Raw("1 = 0")]));
        }

        sql.Append(WhereClause());
        if (_ordering.Count > 0)
        {
            sql.Append(" ORDER BY ").Append(Sql.Sql.Join(", ", _ordering.Select(o => o.Descending ? Sql.Sql.Empty.Append(o.Expression).Append(" DESC") : o.Expression)));
        }

        Dialect.AppendPaging(sql, _skip, _take, _ordering.Count > 0);
        return sql;
    }

    private Sql.Sql WhereClause() => WhereClause(_filters);

    private static Sql.Sql WhereClause(IReadOnlyList<Sql.Sql> filters) => filters.Count > 0
        ? Sql.Sql.Empty.Append(" WHERE ").Append(Sql.Sql.Join(" AND ", filters))
        : Sql.Sql.Empty;

    /// <summary>Applies an aggregate to the filtered rows, or to the page when Skip/Take is set.</summary>
    private Sql.Sql AggregateSql(Sql.Sql aggregate) => IsPaged
        ? Sql.Sql.Empty.Append("SELECT ").Append(aggregate).Append(" FROM (").Append(ToSql()).Append(") paged")
        : Sql.Sql.Empty.Append("SELECT ").Append(aggregate).Append(" FROM ").AppendIdentifier(Map.Table).Append(WhereClause());

    private Sql.Sql Aggregate(string function, LambdaExpression selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var value = ExpressionTranslator.Value(selector, Map, Dialect);
        var aggregate = function switch
        {
            "SUM" => Sql.Sql.Empty.Append("COALESCE(SUM(").Append(value).Append("), 0)"),
            "AVG" => Sql.Sql.Empty.Append("AVG(CAST(").Append(value).Append(" AS " + Dialect.FloatType + "))"),
            _ => Sql.Sql.Empty.Append(function + "(").Append(value).Append(")"),
        };
        return AggregateSql(aggregate);
    }

    private Sql.Sql AnySql() => Sql.Sql.Empty.Append("SELECT CASE WHEN EXISTS (").Append(Limit(1).ToSql()).Append(") THEN 1 ELSE 0 END");

    private Sql.Sql DeleteSql()
    {
        ThrowIfShaped("ExecuteDelete");
        return Sql.Sql.Empty.Append("DELETE FROM ").AppendIdentifier(Map.Table).Append(WhereClause());
    }

    private Sql.Sql UpdateSql(Action<UpdateBuilder<T>> set)
    {
        ArgumentNullException.ThrowIfNull(set);
        ThrowIfShaped("ExecuteUpdate");
        var builder = new UpdateBuilder<T>(Map, Dialect);
        set(builder);
        if (builder.Assignments.Count == 0)
        {
            throw new InvalidOperationException("ExecuteUpdate needs at least one Set(...) call.");
        }

        return Sql.Sql.Empty.Append("UPDATE ").AppendIdentifier(Map.Table).Append(" SET ")
            .Append(Sql.Sql.Join(", ", builder.Assignments))
            .Append(WhereClause());
    }

    private Query<T> Order<TKey>(Expression<Func<T, TKey>> key, bool descending, bool replace)
    {
        ArgumentNullException.ThrowIfNull(key);
        var expression = ExpressionTranslator.Value(key, Map, Dialect);
        IReadOnlyList<(Sql.Sql, bool)> ordering = replace ? [(expression, descending)] : [.. _ordering, (expression, descending)];
        return new(Session, Map, _filters, ordering, _skip, _take);
    }

    private void ThrowIfShaped(string operation)
    {
        if (_ordering.Count > 0 || IsPaged)
        {
            throw new NotSupportedException($"{operation} cannot be combined with ordering, Skip or Take; filter with Where instead.");
        }
    }
}
