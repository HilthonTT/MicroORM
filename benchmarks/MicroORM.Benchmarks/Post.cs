using System.ComponentModel.DataAnnotations.Schema;

namespace MicroORM.Benchmarks;

[Table("posts")]
public class Post
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public int Views { get; set; }

    public bool Published { get; set; }
}
