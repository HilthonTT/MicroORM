using MicroORM.Sessions;
using System.Diagnostics.CodeAnalysis;

namespace MicroORM.Querying;

/// <summary>
/// A query that selects a projection of each row, created by <see cref="Query{T}.Select{TResult}"/>.
/// Filter, sort and page the source query before calling <c>Select</c>.
/// </summary>
/// <typeparam name="T">The entity type of the source query.</typeparam>
/// <typeparam name="TResult">The projected type.</typeparam>
public sealed class Projection<T, TResult> : IValueSubquerySource
    where T : class
{
    private readonly IProjectionSource _source;
    private readonly Sql.Sql _selectList;
    private readonly bool _singleValue;
    private readonly bool _distinct;

    internal Projection(IProjectionSource source, Sql.Sql selectList, bool singleValue, bool distinct = false)
    {
        _source = source;
        _selectList = selectList;
        _singleValue = singleValue;
        _distinct = distinct;
    }

    private MicroSession Session => _source.Session;

    MicroSession IValueSubquerySource.Session => Session;

    bool IValueSubquerySource.SelectsSingleValue => _singleValue;

    /// <summary>
    /// Selects each distinct projected row once (<c>SELECT DISTINCT</c>). A paged source is paged
    /// first, as in LINQ; otherwise its ordering is dropped, since distinct rows have no order.
    /// </summary>
    public Projection<T, TResult> Distinct() => new(_source, _selectList, _singleValue, distinct: true);

    /// <summary>Runs the query and returns every projected row.</summary>
    public Task<List<TResult>> ToListAsync(CancellationToken cancellationToken = default) =>
        Session.QueryCore<TResult>(true, ToSql(), null, cancellationToken).AsTask();

    /// <summary>Runs the query and returns every projected row.</summary>
    public List<TResult> ToList() => MicroSession.Sync(Session.QueryCore<TResult>(false, ToSql(), null, default));

    /// <summary>Runs the query and yields projected rows as they are read.</summary>
    public IAsyncEnumerable<TResult> StreamAsync(CancellationToken cancellationToken = default) => Session.StreamAsync<TResult>(ToSql(), cancellationToken);

    /// <summary>Runs the query and yields projected rows as they are read.</summary>
    public IEnumerable<TResult> Stream() => Session.Stream<TResult>(ToSql());

    /// <summary>Returns the first projected row, or default.</summary>
    public async Task<TResult?> FirstOrDefaultAsync(CancellationToken cancellationToken = default) =>
        MicroSession.First(await Session.QueryCore<TResult>(true, Limited(1), 1, cancellationToken).ConfigureAwait(false));

    /// <summary>Returns the first projected row, or default.</summary>
    public TResult? FirstOrDefault() =>
        MicroSession.First(MicroSession.Sync(Session.QueryCore<TResult>(false, Limited(1), 1, default)));

    /// <summary>Returns the only projected row; throws when there are none or several.</summary>
    public async Task<TResult> SingleAsync(CancellationToken cancellationToken = default) =>
        MicroSession.Single(await Session.QueryCore<TResult>(true, Limited(2), 2, cancellationToken).ConfigureAwait(false), orDefault: false)!;

    /// <summary>Returns the only projected row; throws when there are none or several.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Matches LINQ's Single.")]
    public TResult Single() =>
        MicroSession.Single(MicroSession.Sync(Session.QueryCore<TResult>(false, Limited(2), 2, default)), orDefault: false)!;

    /// <summary>Counts the projected rows (the distinct ones after <see cref="Distinct"/>).</summary>
    public async Task<long> CountAsync(CancellationToken cancellationToken = default) =>
        await Session.ScalarCore<long>(true, CountSql(), cancellationToken).ConfigureAwait(false);

    /// <summary>Counts the projected rows (the distinct ones after <see cref="Distinct"/>).</summary>
    public long Count() => MicroSession.Sync(Session.ScalarCore<long>(false, CountSql(), default));

    /// <summary>Returns whether a projection of one value contains <paramref name="value"/>.</summary>
    public async Task<bool> ContainsAsync(TResult value, CancellationToken cancellationToken = default) =>
        await Session.ScalarCore<bool>(true, ContainsSql(value), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Returns whether a projection of one value contains <paramref name="value"/>. Inside another
    /// query's predicate it becomes a subquery:
    /// <c>people.Where(p =&gt; orders.Select(o =&gt; o.PersonId).Contains(p.Id))</c>.
    /// </summary>
    public bool Contains(TResult value) => MicroSession.Sync(Session.ScalarCore<bool>(false, ContainsSql(value), default));

    /// <summary>Builds the <c>SELECT</c> statement without running it.</summary>
    public Sql.Sql ToSql() => _source.SelectSql(_selectList, _distinct, null, ordered: true);

    /// <inheritdoc />
    public override string ToString() => ToSql().Render(Session.Dialect).Text;

    Sql.Sql IValueSubquerySource.FromItem(string alias) => Derived(alias);

    private Sql.Sql Limited(int count) => _source.SelectSql(_selectList, _distinct, count, ordered: true);

    private Sql.Sql Derived(string alias) =>
        Sql.Sql.Empty.Append("(").Append(_source.SelectSql(_selectList, _distinct, null, ordered: false)).Append(") AS ").AppendIdentifier(alias);

    private Sql.Sql CountSql() => Sql.Sql.Empty.Append("SELECT COUNT(*) FROM ").Append(Derived("projected"));

    private Sql.Sql ContainsSql(TResult value)
    {
        if (!_singleValue)
        {
            throw new NotSupportedException("Contains needs a projection of one value, such as orders.Select(o => o.CustomerId).");
        }

        var column = Sql.Sql.Identifier("projected." + ExpressionTranslator.ValueAlias);
        var match = value is null
            ? Sql.Sql.Empty.Append(column).Append(" IS NULL")
            : Sql.Sql.Empty.Append(column).Append(" = ").AppendParameter(value, typeof(TResult));
        return Sql.Sql.Empty.Append("SELECT CASE WHEN EXISTS (SELECT 1 FROM ").Append(Derived("projected")).Append(" WHERE ").Append(match).Append(") THEN 1 ELSE 0 END");
    }
}
