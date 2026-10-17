using System.ComponentModel.DataAnnotations.Schema;

namespace MicroORM.Tests.Model;

[Table("micro_people")]
public sealed class Person
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }

    public int Age { get; set; }

    public bool Active { get; set; }

    public Mood Mood { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? ExternalId { get; set; }

    [NotMapped]
    public string DisplayName => $"{Name} ({Age})";

    public List<Tag> Tags { get; } = [];
}
