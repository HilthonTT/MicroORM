using System.ComponentModel.DataAnnotations.Schema;

namespace MicroORM.Tests.Model;

[Table("micro_orders")]
public sealed class Order
{
    public int Id { get; set; }

    public int? PersonId { get; set; }

    public int Amount { get; set; }
}
