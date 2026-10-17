using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MicroORM.Tests.Model;

/// <summary>An entity with an optimistic concurrency version.</summary>
[Table("micro_documents")]
public sealed class Document
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    [ConcurrencyCheck]
    public int Version { get; set; }
}
