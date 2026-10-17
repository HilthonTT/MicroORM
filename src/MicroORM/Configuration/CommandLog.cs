using MicroORM.Sql;

namespace MicroORM.Configuration;

/// <summary>A record of one executed command.</summary>
/// <param name="Sql">The command text (the procedure name for stored procedures).</param>
/// <param name="Parameters">The parameters sent with the command, after type handlers ran.</param>
/// <param name="Elapsed">How long the command took, including reading results for queries.</param>
/// <param name="Error">The exception the command threw, if it failed.</param>
public sealed record CommandLog(string Sql, IReadOnlyList<SqlParameterValue> Parameters, TimeSpan Elapsed, Exception? Error);
