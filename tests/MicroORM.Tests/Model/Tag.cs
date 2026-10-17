using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MicroORM.Tests.Model;

[Table("micro_tags")]
public sealed class Tag
{
    [Key]
    public Guid Code { get; set; }

    public string Label { get; set; } = string.Empty;
}
