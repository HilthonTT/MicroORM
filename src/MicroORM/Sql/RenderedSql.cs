namespace MicroORM.Sql;

/// <summary>SQL text and parameters rendered for a specific dialect.</summary>
/// <param name="Text">The command text.</param>
/// <param name="Parameters">The parameters referenced by <paramref name="Text"/>.</param>
public sealed record RenderedSql(string Text, IReadOnlyList<SqlParameterValue> Parameters);
