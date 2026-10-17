using System.Data.Common;

namespace MicroORM.Sessions;

/// <summary>
/// A database transaction. Every operation runs on the same connection, so operations must not
/// run concurrently. Disposing an uncommitted transaction rolls it back.
/// </summary>
public sealed class MicroTransaction : MicroSession, IAsyncDisposable, IDisposable
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private bool _completed;
    private bool _disposed;

    internal MicroTransaction(MicroEngine engine, DbConnection connection, DbTransaction transaction)
        : base(engine)
    {
        _connection = connection;
        _transaction = transaction;
    }

    /// <summary>Gets the underlying provider transaction, for interop with other ADO.NET code.</summary>
    public DbTransaction DbTransaction => _transaction;

    /// <summary>Commits the transaction.</summary>
    public Task CommitAsync(CancellationToken cancellationToken = default) => CompleteCore(true, commit: true, cancellationToken).AsTask();

    /// <summary>Commits the transaction.</summary>
    public void Commit() => Sync(CompleteCore(false, commit: true, default));

    /// <summary>Rolls the transaction back.</summary>
    public Task RollbackAsync(CancellationToken cancellationToken = default) => CompleteCore(true, commit: false, cancellationToken).AsTask();

    /// <summary>Rolls the transaction back.</summary>
    public void Rollback() => Sync(CompleteCore(false, commit: false, default));

    /// <inheritdoc />
    public ValueTask DisposeAsync() => DisposeCore(true);

    /// <inheritdoc />
    public void Dispose() => Sync(DisposeCore(false));

    private protected override ValueTask<ConnectionLease> LeaseCore(bool async, CancellationToken cancellationToken)
    {
        ThrowIfCompleted();
        return ValueTask.FromResult(new ConnectionLease(_connection, _transaction, owned: false));
    }

    private async ValueTask CompleteCore(bool async, bool commit, CancellationToken cancellationToken)
    {
        ThrowIfCompleted();
        if (commit)
        {
            if (async)
            {
                await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _transaction.Commit();
            }
        }
        else if (async)
        {
            await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _transaction.Rollback();
        }

        _completed = true;
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
            if (!_completed)
            {
                if (async)
                {
                    await _transaction.RollbackAsync().ConfigureAwait(false);
                }
                else
                {
                    _transaction.Rollback();
                }
            }
        }
        finally
        {
            await DisposeCore(_transaction, async).ConfigureAwait(false);
            await DisposeCore(_connection, async).ConfigureAwait(false);
        }
    }

    private void ThrowIfCompleted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            throw new InvalidOperationException("The transaction has already been committed or rolled back.");
        }
    }
}
