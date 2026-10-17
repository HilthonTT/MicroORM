using MicroORM.Configuration;
using MicroORM.Dialects;
using System.Data;
using System.Data.Common;

namespace MicroORM.Sessions;

/// <summary>
/// The entry point to a database. Thread-safe and cheap to keep for the application's lifetime:
/// each operation opens a pooled connection and closes it when done.
/// </summary>
/// <example>
/// <code>
/// var db = new MicroDb(SqlDialect.Sqlite, () =&gt; new SqliteConnection("Data Source=app.db"));
/// var adults = await db.Table&lt;User&gt;().Where(u =&gt; u.Age &gt;= 18).ToListAsync();
/// </code>
/// </example>
public sealed class MicroDb : MicroSession
{
    private readonly Func<DbConnection>? _connectionFactory;
    private readonly DbDataSource? _dataSource;

    /// <summary>Creates a database from a connection factory. Connections are opened by MicroORM.</summary>
    public MicroDb(SqlDialect dialect, Func<DbConnection> connectionFactory, MicroOptions? options = null)
        : base(new MicroEngine(dialect ?? throw new ArgumentNullException(nameof(dialect)), options ?? new MicroOptions()))
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <summary>Creates a database from a provider data source (for example <c>NpgsqlDataSource</c>).</summary>
    public MicroDb(SqlDialect dialect, DbDataSource dataSource, MicroOptions? options = null)
        : base(new MicroEngine(dialect ?? throw new ArgumentNullException(nameof(dialect)), options ?? new MicroOptions()))
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    /// <summary>Starts a transaction. Dispose it without committing to roll back.</summary>
    public Task<MicroTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.Unspecified, CancellationToken cancellationToken = default) =>
        BeginTransactionCore(true, isolationLevel, cancellationToken).AsTask();

    /// <summary>Starts a transaction. Dispose it without committing to roll back.</summary>
    public MicroTransaction BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.Unspecified) =>
        Sync(BeginTransactionCore(false, isolationLevel, default));

    /// <summary>Runs <paramref name="work"/> in a transaction, committing if it completes and rolling back if it throws.</summary>
    public async Task TransactionAsync(Func<MicroTransaction, Task> work, IsolationLevel isolationLevel = IsolationLevel.Unspecified, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var transaction = await BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
        await work(transaction).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs <paramref name="work"/> in a transaction and returns its result, committing if it completes.</summary>
    public async Task<TResult> TransactionAsync<TResult>(Func<MicroTransaction, Task<TResult>> work, IsolationLevel isolationLevel = IsolationLevel.Unspecified, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var transaction = await BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
        var result = await work(transaction).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Runs <paramref name="work"/> in a transaction, committing if it completes and rolling back if it throws.</summary>
    public void Transaction(Action<MicroTransaction> work, IsolationLevel isolationLevel = IsolationLevel.Unspecified)
    {
        ArgumentNullException.ThrowIfNull(work);
        using var transaction = BeginTransaction(isolationLevel);
        work(transaction);
        transaction.Commit();
    }

    /// <summary>Runs <paramref name="work"/> in a transaction and returns its result, committing if it completes.</summary>
    public TResult Transaction<TResult>(Func<MicroTransaction, TResult> work, IsolationLevel isolationLevel = IsolationLevel.Unspecified)
    {
        ArgumentNullException.ThrowIfNull(work);
        using var transaction = BeginTransaction(isolationLevel);
        var result = work(transaction);
        transaction.Commit();
        return result;
    }

    internal async ValueTask<MicroTransaction> BeginTransactionCore(bool async, IsolationLevel isolationLevel, CancellationToken cancellationToken)
    {
        var connection = await OpenCore(async, cancellationToken).ConfigureAwait(false);
        try
        {
            var transaction = async
                ? await connection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false)
                : connection.BeginTransaction(isolationLevel);
            return new MicroTransaction(Engine, connection, transaction);
        }
        catch
        {
            await DisposeCore(connection, async).ConfigureAwait(false);
            throw;
        }
    }

    private protected override async ValueTask<ConnectionLease> LeaseCore(bool async, CancellationToken cancellationToken) =>
        new(await OpenCore(async, cancellationToken).ConfigureAwait(false), null, owned: true);

    private async ValueTask<DbConnection> OpenCore(bool async, CancellationToken cancellationToken)
    {
        if (_dataSource is not null)
        {
            return async ? await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false) : _dataSource.OpenConnection();
        }

        var connection = _connectionFactory!();
        try
        {
            if (connection.State != ConnectionState.Open)
            {
                if (async)
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    connection.Open();
                }
            }

            return connection;
        }
        catch
        {
            await DisposeCore(connection, async).ConfigureAwait(false);
            throw;
        }
    }
}
