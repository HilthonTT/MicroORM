<p align="center">
  <img src="https://raw.githubusercontent.com/HilthonTT/MicroORM/main/assets/icon.png" width="112" alt="MicroORM logo: amber threads woven through light threads">
</p>

<h1 align="center">MicroORM</h1>

<p align="center">
  <b>SQL in, objects out.</b><br>
  A small, provider-agnostic ORM for .NET with injection-safe SQL, typed queries and zero ceremony.
</p>

<p align="center">
  <a href="https://github.com/HilthonTT/MicroORM/actions/workflows/ci.yml"><img src="https://github.com/HilthonTT/MicroORM/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://hilthontt.github.io/MicroORM/"><img src="https://img.shields.io/badge/docs-website-f59e0b.svg" alt="Documentation"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT license"></a>
</p>

---

```csharp
var db = new MicroDb(SqlDialect.PostgreSql, dataSource);

// Every {hole} becomes a parameter. Plain strings don't compile.
var recent = await db.QueryAsync<Order>($"SELECT * FROM orders WHERE placed_at > {since} AND status IN {statuses}");

// Typed queries translated to SQL, never silently run in memory.
var vips = await db.Table<Customer>().Where(c => c.Active && c.LifetimeSpend > 10_000).Take(20).ToListAsync();

await db.Table<Customer>().InsertAsync(customer);                   // customer.Id is now set
await db.Table<Customer>().UpsertAsync(customer, on: c => c.Email); // atomic insert-or-update
```

- **Injection-safe by type:** query methods take `Sql`, an interpolated string handler, so values are always parameters.
- **Typed queries:** filters, ordering, paging, projections, aggregates and set-based updates. Unsupported queries throw instead of running in memory.
- **CRUD without SQL:** generated keys, composite keys, upserts and optimistic concurrency.
- **Joins and result sets:** split rows into objects, or read several result sets from one command.
- **Type handlers:** value objects, strongly typed IDs, JSON columns and enums as text.
- **Sync and async:** every API has both forms. Reads keep pace with Dapper.
- **No dependencies:** bring your own ADO.NET provider for SQLite, PostgreSQL, SQL Server or MySQL.

## Getting started

MicroORM targets **.NET 10** and isn't published as a package. Reference the project and add your provider:

```shell
git clone https://github.com/HilthonTT/MicroORM.git
dotnet add reference MicroORM/src/MicroORM/MicroORM.csproj
dotnet add package Microsoft.Data.Sqlite   # or Npgsql, Microsoft.Data.SqlClient, MySqlConnector
```

```csharp
using Microsoft.Data.Sqlite;
using MicroORM.Dialects;
using MicroORM.Sessions;

var db = new MicroDb(SqlDialect.Sqlite, () => new SqliteConnection("Data Source=app.db"));

var books = db.Table<Book>();
await books.InsertAsync(new Book { Title = "Piranesi", Pages = 272 });
var unread = await books.Where(b => !b.Finished).OrderBy(b => b.Title).ToListAsync();

public class Book
{
    public int Id { get; set; }          // "Id" is the key; integer keys are database-generated
    public string Title { get; set; } = "";
    public int Pages { get; set; }
    public bool Finished { get; set; }
}
```

Run the full example with `dotnet run --project samples/MicroORM.Samples.QuickStart`.

## Documentation

Guides for mapping, querying, transactions, joins, type handlers, stored procedures and each
database are on the **[documentation site](https://hilthontt.github.io/MicroORM/)**.

## Contributing

```shell
dotnet test MicroORM.slnx    # unit tests and SQLite; other databases: see CONTRIBUTING.md
```

Contributions are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) and the
[Code of Conduct](CODE_OF_CONDUCT.md) first, and report security issues as described in
[SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE) © 2026 Hilthon
