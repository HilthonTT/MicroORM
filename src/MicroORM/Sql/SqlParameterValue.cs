namespace MicroORM.Sql;

public sealed record SqlParameterValue(string Name, object? Value, Type? ClrType);
