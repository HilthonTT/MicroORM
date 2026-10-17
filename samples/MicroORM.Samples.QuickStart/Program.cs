// MicroORM quick start: a tiny reading list on SQLite. Run with `dotnet run --project samples/MicroORM.Samples.QuickStart`.

using MicroORM.Configuration;
using MicroORM.Dialects;
using MicroORM.Mapping;
using MicroORM.Materialization;
using MicroORM.Sessions;
using Microsoft.Data.Sqlite;

var connectionString = "Data Source=quickstart;Mode=Memory;Cache=Shared";
await using var keepAlive = new SqliteConnection(connectionString); // keeps the in-memory database alive
await keepAlive.OpenAsync();

var db = new MicroDb(
    SqlDialect.Sqlite,
    () => new SqliteConnection(connectionString),
    new MicroOptions
    {
        Naming = NamingConvention.SnakeCase,
        Log = entry => Console.WriteLine($"  sql> {entry.Sql}"),
    });

// 1. Raw SQL. Holes in the interpolated string are sent as parameters, never spliced into the text.
await db.ExecuteAsync($"""
    CREATE TABLE books (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        title TEXT NOT NULL,
        author TEXT NOT NULL,
        pages INTEGER NOT NULL,
        finished INTEGER NOT NULL)
    """);

// 2. CRUD through a typed table. Generated keys are read back into the entity.
var books = db.Table<Book>();
await books.InsertManyAsync(
[
    new Book { Title = "The Left Hand of Darkness", Author = "Ursula K. Le Guin", Pages = 304 },
    new Book { Title = "Piranesi", Author = "Susanna Clarke", Pages = 272, Finished = true },
    new Book { Title = "A Memory Called Empire", Author = "Arkady Martine", Pages = 462 },
]);

var piranesi = await books.FirstOrDefaultAsync(b => b.Title == "Piranesi");
Console.WriteLine($"Found #{piranesi!.Id}: {piranesi.Title}");

// 3. Typed queries translated to SQL.
var maxPages = 400;
var shortReads = await books.Where(b => !b.Finished && b.Pages <= maxPages).OrderBy(b => b.Title).ToListAsync();
Console.WriteLine($"Unread and short: {string.Join(", ", shortReads.Select(b => b.Title))}");

// 4. Set-based update in one statement.
await books.Where(b => b.Author.StartsWith("Ursula")).ExecuteUpdateAsync(s => s.Set(b => b.Finished, true));

// 5. Projections into records, scalars and dynamic rows.
var stats = await db.QuerySingleAsync<ReadingStats>($"SELECT COUNT(*) AS total, SUM(finished) AS finished FROM books");
Console.WriteLine($"Finished {stats.Finished} of {stats.Total} books");

foreach (dynamic row in await db.QueryAsync<Row>($"SELECT author, pages FROM books ORDER BY pages DESC"))
{
    Console.WriteLine($"  {row.author}: {row.pages} pages");
}

// 6. Transactions roll back unless committed.
try
{
    await db.TransactionAsync(async tx =>
    {
        await tx.Table<Book>().ExecuteDeleteAsync();
        throw new InvalidOperationException("Changed my mind.");
    });
}
catch (InvalidOperationException)
{
    Console.WriteLine($"Rolled back; {await books.CountAsync()} books remain.");
}
