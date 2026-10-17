using System.ComponentModel.DataAnnotations.Schema;

[Table("books")]
internal sealed class Book
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    public string Author { get; set; } = "";

    public int Pages { get; set; }

    public bool Finished { get; set; }
}
