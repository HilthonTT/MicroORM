using MicroORM.Procedures;
using MicroORM.Sql;
using System.Data;

namespace MicroORM.Sessions;

public abstract partial class MicroSession
{
    /// <summary>Calls a stored procedure and returns the number of affected rows.</summary>
    /// <param name="name">The procedure name, optionally schema-qualified.</param>
    /// <param name="parameters">Inputs and outputs; read outputs from it after the call.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public Task<int> ExecuteProcedureAsync(string name, ProcedureParameters? parameters = null, CancellationToken cancellationToken = default) =>
        ExecuteCore(true, PrepareProcedure(name, parameters), cancellationToken).AsTask();

    /// <summary>Calls a stored procedure and returns the number of affected rows.</summary>
    public int ExecuteProcedure(string name, ProcedureParameters? parameters = null) =>
        Sync(ExecuteCore(false, PrepareProcedure(name, parameters), default));

    /// <summary>
    /// Calls a stored procedure that returns rows. Output parameters are available once the call
    /// completes. PostgreSQL procedures can't return rows; query a function with
    /// <c>SELECT * FROM fn(...)</c> instead.
    /// </summary>
    public Task<List<T>> QueryProcedureAsync<T>(string name, ProcedureParameters? parameters = null, CancellationToken cancellationToken = default) =>
        ReadCore(true, PrepareProcedure(name, parameters), Engine.Materializer.For<T>, null, cancellationToken).AsTask();

    /// <summary>Calls a stored procedure that returns rows.</summary>
    public List<T> QueryProcedure<T>(string name, ProcedureParameters? parameters = null) =>
        Sync(ReadCore(false, PrepareProcedure(name, parameters), Engine.Materializer.For<T>, null, default));

    private PreparedCommand PrepareProcedure(string name, ProcedureParameters? parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Dialect.SupportsStoredProcedures)
        {
            throw new NotSupportedException($"{Dialect.Name} has no stored procedures.");
        }

        if (!IsProcedureName(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a valid procedure name. Use an identifier, optionally schema-qualified (schema.name), quoting parts that need it as {Dialect.QuoteIdentifier("my proc")}.",
                nameof(name));
        }

        parameters ??= new ProcedureParameters();
        var values = parameters.Parameters
            .Select(p => new SqlParameterValue(Dialect.ProcedureParameterName(p.Name), p.Value, p.Type))
            .ToList();
        return Engine.Prepare(name, CommandType.StoredProcedure, values, parameters);
    }

    /// <summary>
    /// Whether <paramref name="name"/> is one to four dot-separated identifiers, each bare or quoted
    /// the dialect's way. Some providers (Npgsql, MySqlConnector) splice the name into
    /// <c>CALL name(...)</c> verbatim, so it must not be able to carry SQL of its own.
    /// </summary>
    private bool IsProcedureName(string name)
    {
        var quoted = Dialect.QuoteIdentifier("x");
        char open = quoted[0], close = quoted[^1];
        var parts = 0;
        var i = 0;
        while (true)
        {
            var start = i;
            if (i < name.Length && name[i] == open)
            {
                // Quoted: runs to the closing quote; a doubled closing quote is an escaped one.
                for (i++; i < name.Length; i++)
                {
                    if (name[i] == close)
                    {
                        if (i + 1 < name.Length && name[i + 1] == close)
                        {
                            i++;
                            continue;
                        }

                        break;
                    }
                }

                if (i >= name.Length || i == start + 1)
                {
                    return false;
                }

                i++;
            }
            else
            {
                while (i < name.Length && (char.IsLetter(name[i]) || name[i] == '_' || (i > start && (char.IsDigit(name[i]) || name[i] == '$'))))
                {
                    i++;
                }

                if (i == start)
                {
                    return false;
                }
            }

            if (++parts > 4)
            {
                return false;
            }

            if (i == name.Length)
            {
                return true;
            }

            if (name[i] != '.')
            {
                return false;
            }

            i++;
        }
    }
}
