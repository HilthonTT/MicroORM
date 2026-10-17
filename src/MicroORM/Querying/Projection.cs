using MicroORM.Sessions;
using System.Diagnostics.CodeAnalysis;

namespace MicroORM.Querying;

/// <summary>
/// A query that selects a projection of each row, created by <see cref="Query{T}.Select{TResult}"/>.
/// Filter, sort and page the source query before calling <c>Select</c>.
/// </summary>
/// <typeparam name="T">The entity type of the source query.</typeparam>
/// <typeparam name="TResult">The projected type.</typeparam>
public sealed class Projection<T, TResult>
    where T : class
{
    private readonly Query<T> _source;
    private readonly Sql.Sql _selectList;

    internal Projection(Query<T> source, Sql.Sql selectList)
    {
        _source = source;
        _selectList = selectList;
    }

    private MicroSession Session => _source.Session;

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
        MicroSession.First(await Session.QueryCore<TResult>(true, _source.Limit(1).SelectSql(_selectList), 1, cancellationToken).ConfigureAwait(false));

    /// <summary>Returns the first projected row, or default.</summary>
    public TResult? FirstOrDefault() =>
        MicroSession.First(MicroSession.Sync(Session.QueryCore<TResult>(false, _source.Limit(1).SelectSql(_selectList), 1, default)));

    /// <summary>Returns the only projected row; throws when there are none or several.</summary>
    public async Task<TResult> SingleAsync(CancellationToken cancellationToken = default) =>
        MicroSession.Single(await Session.QueryCore<TResult>(true, _source.Limit(2).SelectSql(_selectList), 2, cancellationToken).ConfigureAwait(false), orDefault: false)!;

    /// <summary>Returns the only projected row; throws when there are none or several.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Matches LINQ's Single.")]
    public TResult Single() =>
        MicroSession.Single(MicroSession.Sync(Session.QueryCore<TResult>(false, _source.Limit(2).SelectSql(_selectList), 2, default)), orDefault: false)!;

    /// <summary>Builds the <c>SELECT</c> statement without running it.</summary>
    public Sql.Sql ToSql() => _source.SelectSql(_selectList);

    /// <inheritdoc />
    public override string ToString() => ToSql().Render(Session.Dialect).Text;
}
