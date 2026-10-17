using System.ComponentModel.DataAnnotations.Schema;

namespace MicroORM.Tests.Model;

/// <summary>An entity whose columns need type handlers: a value object, JSON and a text enum.</summary>
[Table("micro_customers")]
public sealed class Customer
{
    public int Id { get; set; }

    public Email Email { get; set; } = new(string.Empty);

    public string Name { get; set; } = string.Empty;

    public Address? Address { get; set; }

    public List<string> Tags { get; set; } = [];

    public CustomerTier Tier { get; set; }
}
