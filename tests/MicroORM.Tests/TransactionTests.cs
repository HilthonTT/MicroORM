using MicroORM.Configuration;
using MicroORM.Dialects;
using MicroORM.Sessions;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Data.Common;

namespace MicroORM.Tests;

/// <summary>Transaction lifecycle edge cases that need a misbehaving provider.</summary>
public class TransactionTests
{
    [Fact]
    public void A_failed_commit_is_not_hidden_by_the_rollback_on_dispose()
    {
        var transaction = new FailingTransaction();
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            using var tx = new MicroTransaction(new MicroEngine(SqlDialect.Sqlite, new MicroOptions()), new SqliteConnection(), transaction);
            tx.Commit();
        });

        Assert.Equal("commit failed", exception.Message);
        Assert.True(transaction.Disposed);
    }

    [Fact]
    public async Task A_failed_async_commit_is_not_hidden_by_the_rollback_on_dispose()
    {
        var transaction = new FailingTransaction();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var tx = new MicroTransaction(new MicroEngine(SqlDialect.Sqlite, new MicroOptions()), new SqliteConnection(), transaction);
            await tx.CommitAsync();
        });

        Assert.Equal("commit failed", exception.Message);
        Assert.True(transaction.Disposed);
    }

    /// <summary>Behaves like a provider whose commit fails and leaves the transaction unusable.</summary>
    private sealed class FailingTransaction : DbTransaction
    {
        public bool Disposed { get; private set; }

        public override IsolationLevel IsolationLevel => IsolationLevel.Unspecified;

        protected override DbConnection? DbConnection => null;

        public override void Commit() => throw new InvalidOperationException("commit failed");

        public override void Rollback() => throw new InvalidOperationException("This transaction has completed; it is no longer usable.");

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
