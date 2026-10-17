using System.Data.Common;
using System.Diagnostics;

namespace MicroORM.Sessions;

/// <summary>
/// The result sets of one command, read in order. Returned by
/// <see cref="MicroSession.QueryMultipleAsync"/>; dispose it to release the connection.
/// </summary>
/// <example>
/// <code>
/// await using var results = await db.QueryMultipleAsync($"SELECT * FROM orders WHERE id = {id}; SELECT * FROM order_lines WHERE order_id = {id}");
/// var order = await results.ReadSingleAsync&lt;Order&gt;();
/// var lines = await results.ReadAsync&lt;OrderLine&gt;();
/// </code>
/// </example>
public sealed class ResultSets : IAsyncDisposable, IDisposable
{
    private readonly MicroSession _session;
    private readonly ConnectionLease _lease;
    private readonly DbCommand _command;
    private readonly DbDataReader _reader;
    private readonly PreparedCommand _prepared;
    private readonly Stopwatch _stopwatch;
    private bool _started;
    private bool _disposed;

    internal ResultSets(
        MicroSession session,
        ConnectionLease lease,
        DbCommand command,
        DbDataReader reader,
        PreparedCommand prepared,
        Stopwatch stopwatch)
    {
        _session = session;
        _lease = lease;
        _command = command;
        _reader = reader;
        _prepared = prepared;
        _stopwatch = stopwatch;
    }

    /// <summary>Reads the next result set completely.</summary>
    public Task<List<T>> ReadAsync<T>(CancellationToken cancellationToken = default) =>
        ReadCore<T>(true, null, cancellationToken).AsTask();

    /// <summary>Reads the next result set completely.</summary>
    public List<T> Read<T>() => MicroSession.Sync(ReadCore<T>(false, null, default));

    /// <summary>Reads the first row of the next result set, or default.</summary>
    public async Task<T?> ReadFirstOrDefaultAsync<T>(CancellationToken cancellationToken = default) =>
        MicroSession.First(await ReadCore<T>(true, 1, cancellationToken).ConfigureAwait(false));

    /// <summary>Reads the first row of the next result set, or default.</summary>
    public T? ReadFirstOrDefault<T>() => MicroSession.First(MicroSession.Sync(ReadCore<T>(false, 1, default)));

    /// <summary>Reads the only row of the next result set; throws when it has none or several.</summary>
    public async Task<T> ReadSingleAsync<T>(CancellationToken cancellationToken = default) =>
        MicroSession.Single(await ReadCore<T>(true, 2, cancellationToken).ConfigureAwait(false), orDefault: false)!;

    /// <summary>Reads the only row of the next result set; throws when it has none or several.</summary>
    public T ReadSingle<T>() => MicroSession.Single(MicroSession.Sync(ReadCore<T>(false, 2, default)), orDefault: false)!;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => DisposeCore(true);

    /// <inheritdoc />
    public void Dispose() => MicroSession.Sync(DisposeCore(false));

    private async ValueTask<List<T>> ReadCore<T>(bool async, int? maxRows, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            var hasMore = async ? await _reader.NextResultAsync(cancellationToken).ConfigureAwait(false) : _reader.NextResult();
            if (!hasMore)
            {
                throw new InvalidOperationException("The command returned no more result sets.");
            }
        }

        _started = true;
        return await MicroSession.ReadRows(_reader, async, _session.Engine.Materializer.For<T>, maxRows, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask DisposeCore(bool async)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await MicroSession.DisposeCore(_reader, async).ConfigureAwait(false);
            _command.Dispose();
        }
        finally
        {
            await _lease.DisposeCore(async).ConfigureAwait(false);
            _session.Log(_prepared, _stopwatch, null);
        }
    }
}
