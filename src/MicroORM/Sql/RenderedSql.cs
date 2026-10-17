namespace MicroORM.Sql;

public sealed record RenderedSql(string Text, IReadOnlyList<SqlParameterValue> Parameters);
