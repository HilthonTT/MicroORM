using MicroORM.Configuration;
using MicroORM.Dialects;
using MicroORM.Mapping;
using MicroORM.Materialization;
using MicroORM.Querying;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace MicroORM.Sessions;

/// <summary>
/// The operations shared by <see cref="MicroDb"/> (a connection per operation) and
/// <see cref="MicroTransaction"/> (one connection and transaction for every operation).
/// Every operation comes in an asynchronous and a synchronous form.
/// </summary>
/// <remarks>
/// Internally each operation is written once as a <c>...Core(bool async, ...)</c> method. With
/// <c>async: false</c> it only calls synchronous ADO.NET methods, so the returned task is already
/// complete and the synchronous wrappers never block on asynchronous work.
/// </remarks>
public abstract partial class MicroSession
{
    private protected MicroSession(MicroEngine engine) => Engine = engine;

    internal MicroEngine Engine { get; }

    /// <summary>Gets the SQL dialect of the database.</summary>
    public SqlDialect Dialect => Engine.Dialect;

    /// <summary>Gets the mapping for an entity type.</summary>
    public EntityMap MapOf<T>() => Engine.Mappings.GetMap(typeof(T));

    /// <summary>Gets a typed gateway to <typeparamref name="T"/>'s table for CRUD and queries.</summary>
    public Table<T> Table<T>()
        where T : class => new(this, MapOf<T>());

    /// <summary>Runs a query and materializes every row as <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">An entity, record, scalar, <see cref="Row"/>, or <see langword="object"/> for <see langword="dynamic"/> rows.</typeparam>
    public Task<List<T>> QueryAsync<T>(Sql.Sql sql, CancellationToken cancellationToken = default) =>
        QueryCore<T>(true, sql, null, cancellationToken).AsTask();

    /// <summary>Returns the first row, or default when there are none.</summary>
    public async Task<T?> QueryFirstOrDefaultAsync<T>(Sql.Sql sql, CancellationToken cancellationToken = default) =>
        First(await QueryCore<T>(true, sql, 1, cancellationToken).ConfigureAwait(false));

    /// <summary>Returns the only row; throws when there are none or several.</summary>
    public async Task<T> QuerySingleAsync<T>(Sql.Sql sql, CancellationToken cancellationToken = default) =>
        Single(await QueryCore<T>(true, sql, 2, cancellationToken).ConfigureAwait(false), orDefault: false)!;

    /// <summary>Returns the only row, or default when there are none; throws when there are several.</summary>
    public async Task<T?> QuerySingleOrDefaultAsync<T>(Sql.Sql sql, CancellationToken cancellationToken = default) =>
        Single(await QueryCore<T>(true, sql, 2, cancellationToken).ConfigureAwait(false), orDefault: true);

    /// <summary>Executes a statement and returns the number of affected rows.</summary>
    public Task<int> ExecuteAsync(Sql.Sql sql, CancellationToken cancellationToken = default) =>
        ExecuteCore(true, sql, cancellationToken).AsTask();

    /// <summary>Executes a query and returns the first column of the first row, converted to <typeparamref name="T"/>.</summary>
    public Task<T?> ScalarAsync<T>(Sql.Sql sql, CancellationToken cancellationToken = default) =>
        ScalarCore<T>(true, sql, cancellationToken).AsTask();

    /// <summary>
    /// Runs a query that returns several result sets (for example, several <c>SELECT</c>
    /// statements). Read the sets in order from the returned reader, then dispose it.
    /// </summary>
    public Task<ResultSets> QueryMultipleAsync(Sql.Sql sql, CancellationToken cancellationToken = default) =>
        QueryMultipleCore(true, sql, cancellationToken).AsTask();

    /// <summary>
    /// Runs a query and splits each row into two objects at the <paramref name="splitOn"/> column,
    /// then combines them with <paramref name="map"/>. Use it to read joins. When the second
    /// object's first column is <c>NULL</c> (an unmatched <c>LEFT JOIN</c>), it is <see langword="null"/>.
    /// </summary>
    /// <param name="sql">The query; list the first object's columns first.</param>
    /// <param name="map">Combines the objects of one row.</param>
    /// <param name="splitOn">The column where the second object starts. Defaults to <c>Id</c>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public Task<List<TResult>> QueryAsync<T1, T2, TResult>(Sql.Sql sql, Func<T1, T2, TResult> map, string splitOn = "Id", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        return SplitCore(true, sql, [typeof(T1), typeof(T2)], splitOn, parts => map(As<T1>(parts[0]), As<T2>(parts[1])), cancellationToken).AsTask();
    }

    /// <summary>Like <see cref="QueryAsync{T1, T2, TResult}"/>, for three objects per row. Separate split columns with commas.</summary>
    public Task<List<TResult>> QueryAsync<T1, T2, T3, TResult>(Sql.Sql sql, Func<T1, T2, T3, TResult> map, string splitOn = "Id", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        return SplitCore(true, sql, [typeof(T1), typeof(T2), typeof(T3)], splitOn, parts => map(As<T1>(parts[0]), As<T2>(parts[1]), As<T3>(parts[2])), cancellationToken).AsTask();
    }

    /// <summary>Runs a query and yields rows as they are read, without buffering.</summary>
    public async IAsyncEnumerable<T> StreamAsync<T>(Sql.Sql sql, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prepared = Prepare(sql);
        var lease = await LeaseCore(true, cancellationToken).ConfigureAwait(false);
        try
        {
            using var command = CreateCommand(lease, prepared);
            var stopwatch = Stopwatch.StartNew();
            DbDataReader reader;
            try
            {
                reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log(prepared, stopwatch, ex);
                throw;
            }

            await using (reader.ConfigureAwait(false))
            {
                // finally, so callers that stop early (FirstOrDefault, break) are still logged.
                try
                {
                    Func<DbDataReader, T>? materialize = null;
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
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
            await lease.DisposeCore(true).ConfigureAwait(false);
        }
    }

    internal ValueTask<List<T>> QueryCore<T>(bool async, Sql.Sql sql, int? maxRows, CancellationToken cancellationToken) =>
        ReadCore(async, Prepare(sql), Engine.Materializer.For<T>, maxRows, cancellationToken);

    internal ValueTask<int> ExecuteCore(bool async, Sql.Sql sql, CancellationToken cancellationToken) =>
        ExecuteCore(async, Prepare(sql), cancellationToken);

    internal ValueTask<int> ExecuteCore(bool async, PreparedCommand prepared, CancellationToken cancellationToken) =>
        RunCore(async, prepared, static async (command, async, ct) =>
            async ? await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) : command.ExecuteNonQuery(), cancellationToken);

    internal ValueTask<T?> ScalarCore<T>(bool async, Sql.Sql sql, CancellationToken cancellationToken) =>
        RunCore(async, Prepare(sql), async (command, async, ct) =>
        {
            var value = async ? await command.ExecuteScalarAsync(ct).ConfigureAwait(false) : command.ExecuteScalar();
            return (T?)Engine.Handlers.Convert<T>(value);
        }, cancellationToken);

    internal ValueTask<List<TResult>> SplitCore<TResult>(bool async, Sql.Sql sql, Type[] types, string splitOn, Func<object?[], TResult> map, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(splitOn);
        return ReadCore(async, Prepare(sql), reader => Engine.Materializer.ForSplit(reader, types, splitOn, map), null, cancellationToken);
    }

    internal async ValueTask<ResultSets> QueryMultipleCore(bool async, Sql.Sql sql, CancellationToken cancellationToken)
    {
        var prepared = Prepare(sql);
        var lease = await LeaseCore(async, cancellationToken).ConfigureAwait(false);
        DbCommand? command = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            command = CreateCommand(lease, prepared);
            var reader = async ? await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false) : command.ExecuteReader();
            return new ResultSets(this, lease, command, reader, prepared, stopwatch);
        }
        catch (Exception ex)
        {
            Log(prepared, stopwatch, ex);
            command?.Dispose();
            await lease.DisposeCore(async).ConfigureAwait(false);
            throw;
        }
    }

    internal ValueTask<List<T>> ReadCore<T>(
        bool async,
        PreparedCommand prepared,
        Func<DbDataReader, Func<DbDataReader, T>> materializer,
        int? maxRows,
        CancellationToken cancellationToken) =>
        RunCore(async, prepared, async (command, async, ct) =>
        {
            var reader = async ? await command.ExecuteReaderAsync(ct).ConfigureAwait(false) : command.ExecuteReader();
            try
            {
                return await ReadRows(reader, async, materializer, maxRows, ct).ConfigureAwait(false);
            }
            finally
            {
                await DisposeCore(reader, async).ConfigureAwait(false);
            }
        }, cancellationToken);

    internal static async ValueTask<List<T>> ReadRows<T>(
        DbDataReader reader,
        bool async,
        Func<DbDataReader, Func<DbDataReader, T>> materializer,
        int? maxRows,
        CancellationToken cancellationToken)
    {
        var rows = new List<T>();
        Func<DbDataReader, T>? materialize = null;
        while ((maxRows is null || rows.Count < maxRows) &&
            (async ? await reader.ReadAsync(cancellationToken).ConfigureAwait(false) : reader.Read()))
        {
            materialize ??= materializer(reader);
            rows.Add(materialize(reader));
        }

        return rows;
    }

    internal async ValueTask<TResult> RunCore<TResult>(
        bool async,
        PreparedCommand prepared,
        Func<DbCommand, bool, CancellationToken, ValueTask<TResult>> run,
        CancellationToken cancellationToken)
    {
        var lease = await LeaseCore(async, cancellationToken).ConfigureAwait(false);
        try
        {
            using var command = CreateCommand(lease, prepared);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await run(command, async, cancellationToken).ConfigureAwait(false);
                prepared.Procedure?.CollectOutputs(command);
                Log(prepared, stopwatch, null);
                return result;
            }
            catch (Exception ex)
            {
                Log(prepared, stopwatch, ex);
                throw;
            }
        }
        finally
        {
            await lease.DisposeCore(async).ConfigureAwait(false);
        }
    }

    private protected abstract ValueTask<ConnectionLease> LeaseCore(bool async, CancellationToken cancellationToken);

    internal PreparedCommand Prepare(Sql.Sql sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var rendered = sql.Render(Dialect);
        return Engine.Prepare(rendered.Text, CommandType.Text, rendered.Parameters, null);
    }

    internal DbCommand CreateCommand(ConnectionLease lease, PreparedCommand prepared)
    {
        var command = lease.Connection.CreateCommand();
        command.CommandText = prepared.Text;
        command.CommandType = prepared.CommandType;
        command.Transaction = lease.Transaction;
        if (Engine.Options.CommandTimeout is int timeout)
        {
            command.CommandTimeout = timeout;
        }

        for (var i = 0; i < prepared.Parameters.Count; i++)
        {
            var value = prepared.Parameters[i];
            var parameter = command.CreateParameter();
            parameter.ParameterName = value.Name;
            parameter.Value = value.Value ?? DBNull.Value;
            Dialect.ConfigureParameter(parameter, value);
            prepared.Handlers[i]?.ConfigureParameter(parameter);
            prepared.Procedure?.Configure(parameter, i);
            command.Parameters.Add(parameter);
        }

        return command;
    }

    internal void Log(PreparedCommand prepared, Stopwatch stopwatch, Exception? error) =>
        Engine.Options.Log?.Invoke(new CommandLog(prepared.Text, prepared.Parameters, stopwatch.Elapsed, error));

    internal static ValueTask DisposeCore(IAsyncDisposable disposable, bool async)
    {
        if (async)
        {
            return disposable.DisposeAsync();
        }

        ((IDisposable)disposable).Dispose();
        return ValueTask.CompletedTask;
    }

    internal static T Sync<T>(ValueTask<T> task)
    {
        Debug.Assert(task.IsCompleted, "A synchronous core must complete synchronously.");
        return task.GetAwaiter().GetResult();
    }

    internal static void Sync(ValueTask task)
    {
        Debug.Assert(task.IsCompleted, "A synchronous core must complete synchronously.");
        task.GetAwaiter().GetResult();
    }

    internal static T? First<T>(List<T> rows) => rows.Count > 0 ? rows[0] : default;

    internal static T? Single<T>(List<T> rows, bool orDefault) => rows.Count switch
    {
        0 when orDefault => default,
        0 => throw new InvalidOperationException("The query returned no rows; exactly one was expected."),
        1 => rows[0],
        _ => throw new InvalidOperationException("The query returned more than one row; exactly one was expected."),
    };

    private static T As<T>(object? value) => value is T typed ? typed : default!;
}
