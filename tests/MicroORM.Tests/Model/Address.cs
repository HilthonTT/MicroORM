namespace MicroORM.Tests.Model;

/// <summary>A nested object stored as a JSON column.</summary>
public sealed class Address
{
    public string Street { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;
}
