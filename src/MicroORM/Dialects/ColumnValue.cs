namespace MicroORM.Dialects;

public readonly record struct ColumnValue(string Column, object? Value, Type ClrType);
