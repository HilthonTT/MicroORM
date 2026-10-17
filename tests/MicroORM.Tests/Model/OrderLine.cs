using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MicroORM.Tests.Model;

/// <summary>An entity with a composite primary key.</summary>
[Table("micro_order_lines")]
public sealed class OrderLine
{
    [Key]
    [Column(Order = 0)]
    public int OrderId { get; set; }

    [Key]
    [Column(Order = 1)]
    public int LineNo { get; set; }

    public string Product { get; set; } = string.Empty;

    public int Quantity { get; set; }
}
