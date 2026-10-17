namespace MicroORM.Dialects;

/// <summary>A column name paired with the value to write to it.</summary>
/// <param name="Column">The column name (unquoted).</param>
/// <param name="Value">The value.</param>
/// <param name="ClrType">The property type the value came from.</param>
public readonly record struct ColumnValue(string Column, object? Value, Type ClrType);
