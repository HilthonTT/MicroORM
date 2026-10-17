namespace MicroORM.Tests.Model;

/// <summary>A positional record, materialized through its constructor.</summary>
public sealed record PersonSummary(int Id, string Name, int Age);
