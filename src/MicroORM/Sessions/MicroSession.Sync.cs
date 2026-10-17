using System.Data.Common;
using System.Diagnostics;

namespace MicroORM.Sessions;

public abstract partial class MicroSession
{
    /// <summary>Runs a query and materializes every row as <typeparamref name="T"/>.</summary>
    public List<T> Query<T>(Sql.Sql sql) => Sync(QueryCore<T>(false, sql, null, default));

    /// <summary>Returns the first row, or default when there are none.</summary>
    public T? QueryFirstOrDefault<T>(Sql.Sql sql) => First(Sync(QueryCore<T>(false, sql, 1, default)));

    /// <summary>Returns the only row; throws when there are none or several.</summary>
    public T QuerySingle<T>(Sql.Sql sql) => Single(Sync(QueryCore<T>(false, sql, 2, default)), orDefault: false)!;

    /// <summary>Returns the only row, or default when there are none; throws when there are several.</summary>
    public T? QuerySingleOrDefault<T>(Sql.Sql sql) => Single(Sync(QueryCore<T>(false, sql, 2, default)), orDefault: true);

    /// <summary>Executes a statement and returns the number of affected rows.</summary>
    public int Execute(Sql.Sql sql) => Sync(ExecuteCore(false, sql, default));

    /// <summary>Executes a query and returns the first column of the first row.</summary>
    public T? Scalar<T>(Sql.Sql sql) => Sync(ScalarCore<T>(false, sql, default));

    /// <summary>Runs a query that returns several result sets; dispose the result when done.</summary>
    public ResultSets QueryMultiple(Sql.Sql sql) => Sync(QueryMultipleCore(false, sql, default));

    /// <summary>Runs a query and splits each row into two objects; see <see cref="QueryAsync{T1, T2, TResult}"/>.</summary>
    public List<TResult> Query<T1, T2, TResult>(Sql.Sql sql, Func<T1, T2, TResult> map, string splitOn = "Id")
    {
        ArgumentNullException.ThrowIfNull(map);
        return Sync(SplitCore(false, sql, [typeof(T1), typeof(T2)], splitOn, parts => map(As<T1>(parts[0]), As<T2>(parts[1])), default));
    }

    /// <summary>Runs a query and splits each row into three objects; see <see cref="QueryAsync{T1, T2, T3, TResult}"/>.</summary>
    public List<TResult> Query<T1, T2, T3, TResult>(Sql.Sql sql, Func<T1, T2, T3, TResult> map, string splitOn = "Id")
    {
        ArgumentNullException.ThrowIfNull(map);
        return Sync(SplitCore(false, sql, [typeof(T1), typeof(T2), typeof(T3)], splitOn, parts => map(As<T1>(parts[0]), As<T2>(parts[1]), As<T3>(parts[2])), default));
    }

    /// <summary>Runs a query and yields rows as they are read, without buffering.</summary>
    public IEnumerable<T> Stream<T>(Sql.Sql sql)
    {
        var prepared = Prepare(sql);
        var lease = Sync(LeaseCore(false, default));
        try
        {
            using var command = CreateCommand(lease, prepared);
            var stopwatch = Stopwatch.StartNew();
            DbDataReader reader;
            try
            {
                reader = command.ExecuteReader();
            }
            catch (Exception ex)
            {
                Log(prepared, stopwatch, ex);
                throw;
            }

            using (reader)
            {
                try
                {
                    Func<DbDataReader, T>? materialize = null;
                    while (reader.Read())
                    {
                        materialize ??= Engine.Materializer.For<T>(reader);
                        yield return materialize(reader);
                    }
                }
                finally
                {
                    Log(prepared, stopwatch, null);
                }
            }
        }
        finally
        {
            Sync(lease.DisposeCore(false));
        }
    }
}
