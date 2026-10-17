using System.Data.Common;

namespace MicroORM.Sessions;

internal readonly struct ConnectionLease(DbConnection connection, DbTransaction? transaction, bool owned)
{
    public DbConnection Connection { get; } = connection;

    public DbTransaction? Transaction { get; } = transaction;

    public ValueTask DisposeCore(bool async) => owned ? MicroSession.DisposeCore(Connection, async) : ValueTask.CompletedTask;
}
