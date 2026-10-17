using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MicroORM.Tests.Model;

/// <summary>Exercises every supported data annotation.</summary>
[Table("entries", Schema = "audit")]
public sealed class Annotated
{
    [Key]
    [Column("entry_no")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Number { get; set; }

    public string? Note { get; set; }

    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public decimal Total { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
