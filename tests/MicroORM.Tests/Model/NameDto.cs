namespace MicroORM.Tests.Model;

/// <summary>A DTO filled through property setters.</summary>
public sealed class NameDto
{
    public int Id { get; set; }

    public string Upper { get; set; } = string.Empty;
}
