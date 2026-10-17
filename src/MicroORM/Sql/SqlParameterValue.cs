namespace MicroORM.Sql;

/// <summary>A rendered command parameter.</summary>
/// <param name="Name">The parameter name, including its prefix.</param>
/// <param name="Value">The value; <see langword="null"/> is sent as <see cref="DBNull"/>.</param>
/// <param name="ClrType">The declared CLR type when known, used to type <see langword="null"/> values.</param>
public sealed record SqlParameterValue(string Name, object? Value, Type? ClrType);
