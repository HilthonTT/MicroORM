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
  <a href="https://www.nuget.org/packages/MicroORM"><img src="https://img.shields.io/nuget/v/MicroORM.svg" alt="NuGet"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT license"></a>
</p>

---

MicroORM turns rows into objects and objects back into rows. You write SQL where SQL is the best
tool, typed queries where they're clearer, and every value you pass is a parameter.

```csharp
var db = new MicroDb(SqlDialect.PostgreSql, dataSource, new() { Naming = NamingConvention.SnakeCase });

// Interpolated SQL: every {hole} becomes a parameter. Plain strings don't compile.
var recent = await db.QueryAsync<Order>($"SELECT * FROM orders WHERE placed_at > {since} AND status IN {statuses}");

// Typed queries translated to SQL, never silently run in memory.
var vips = await db.Table<Customer>()
    .Where(c => c.Active && c.LifetimeSpend > 10_000 && c.Email.EndsWith("@example.com"))
    .OrderByDescending(c => c.LifetimeSpend)
    .Take(20)
    .ToListAsync();

// CRUD with generated keys read back.
await db.Table<Customer>().InsertAsync(customer);   // customer.Id is now set

// Set-based updates in one statement.
await db.Table<Customer>().Where(c => c.LastSeen < cutoff).ExecuteUpdateAsync(s => s.Set(c => c.Active, false));

// Projections and aggregates.
var names = await db.Table<Customer>().Where(c => c.Active).Select(c => new { c.Id, c.Name }).ToListAsync();
var revenue = await db.Table<Order>().Where(o => o.Paid).SumAsync(o => o.Total);

// Joins split into objects, and atomic upserts.
var rows = await db.QueryAsync<Order, Customer, (Order, Customer?)>(
    $"SELECT o.id, o.total, c.id, c.name FROM orders o LEFT JOIN customers c ON c.id = o.customer_id",
    (o, c) => (o, c));
await db.Table<Customer>().UpsertAsync(customer, on: c => c.Email);

// Every API also has a synchronous twin.
var count = db.Table<Customer>().Count();
```

## Why MicroORM?

MicroORM sits between a micro-ORM and a full ORM. It takes ideas from the libraries it learned
from and adds a few of its own:

| | Dapper | Massive | EF Core | **MicroORM** |
|---|---|---|---|---|
| Raw SQL, mapped to objects | ✅ | ✅ | ✅ | ✅ |
| SQL injection blocked by the type system | — | — | ✅ (`FromSql`) | ✅ **every API** |
| Typed LINQ-style queries, projections, aggregates | — | — | ✅ | ✅ (a focused subset) |
| Unsupported query → exception, not client eval | n/a | n/a | ✅ | ✅ |
| CRUD without writing SQL | — | ✅ | ✅ | ✅ |
| Composite keys and optimistic concurrency | — | — | ✅ | ✅ |
| Atomic upsert | — | — | — | ✅ |
| Multi-mapping joins, multiple result sets | ✅ | — | — | ✅ |
| Custom type handlers (value objects, JSON) | ✅ | — | ✅ | ✅ |
| Stored procedures with output parameters | ✅ | ✅ | ✅ (raw SQL) | ✅ |
| Sync and async APIs | ✅ | ✅ | ✅ | ✅ |
| Change tracking, migrations, navigation loading | — | — | ✅ | — *by design* |
| Dynamic rows | ✅ | ✅ | — | ✅ |
| Dependencies | none | none | many | **none** |

On reading rows, MicroORM keeps pace with Dapper: 500 rows take the same time and memory. See
[the benchmarks](docs/benchmarks.md).

MicroORM's own ideas:

- **`Sql` is an interpolated string handler.** Query methods take `Sql`, not `string`, so `$"... {value}"` is always parameterized and string concatenation doesn't compile. Fragments compose (`$"... WHERE {filter}"`) and collections expand to `IN` lists.
- **One model of a session.** `MicroDb` opens a pooled connection per operation. `MicroTransaction` has the *same* API and runs everything on one connection. Code written against `MicroSession` works either way.
- **Honest translation.** The query translator supports a deliberately small, documented subset. Anything else throws with a message that points you to `.Where($"...")`.
- **Dialect objects, not provider references.** The MicroORM package references no database driver. You choose the provider and pass a `SqlDialect`.

## Install

```shell
dotnet add package MicroORM
dotnet add package Microsoft.Data.Sqlite   # or Npgsql, Microsoft.Data.SqlClient, MySqlConnector
```

MicroORM targets **.NET 10**.

## Quick start

```csharp
using Microsoft.Data.Sqlite;
using MicroORM.Dialects;
using MicroORM.Sessions;

var db = new MicroDb(SqlDialect.Sqlite, () => new SqliteConnection("Data Source=app.db"));

var books = db.Table<Book>();
var book = new Book { Title = "Piranesi", Pages = 272 };
await books.InsertAsync(book);                         // book.Id assigned
book.Finished = true;
await books.UpdateAsync(book);
var unread = await books.Where(b => !b.Finished).OrderBy(b => b.Title).ToListAsync();
var found = await books.FindAsync(book.Id);
await books.DeleteAsync(book);

public class Book
{
    public int Id { get; set; }          // "Id" is the key; integer keys are database-generated
    public string Title { get; set; } = "";
    public int Pages { get; set; }
    public bool Finished { get; set; }
}
```

A runnable version is in [`samples/MicroORM.Samples.QuickStart`](samples/MicroORM.Samples.QuickStart/Program.cs):

```shell
dotnet run --project samples/MicroORM.Samples.QuickStart
```

## Supported databases

| Database | Dialect | Tested with |
|---|---|---|
| SQLite | `SqlDialect.Sqlite` | Microsoft.Data.Sqlite |
| PostgreSQL | `SqlDialect.PostgreSql` | Npgsql, PostgreSQL 17 |
| SQL Server / Azure SQL | `SqlDialect.SqlServer` | Microsoft.Data.SqlClient, SQL Server 2022 |
| MySQL / MariaDB | `SqlDialect.MySql` | MySqlConnector, MySQL 8.4 |

CI runs the full integration suite against every database on every push.

## Documentation

| Guide | |
|---|---|
| [Getting started](docs/getting-started.md) | Install, connect, first queries |
| [Interpolated SQL](docs/sql.md) | Parameters, fragments, `IN` lists, `Sql.Raw` and `Sql.Identifier` |
| [Mapping](docs/mapping.md) | Conventions, attributes, fluent configuration, composite keys, naming |
| [Querying](docs/querying.md) | What translates to SQL, projections, aggregates, paging, set-based operations |
| [CRUD and transactions](docs/crud-and-transactions.md) | Insert, update, delete, find, upsert, optimistic concurrency, transactions, sync API, logging |
| [Joins and result sets](docs/joins-and-result-sets.md) | Multi-mapping joins into objects, multiple result sets |
| [Type handlers](docs/type-handlers.md) | Value objects, strongly typed IDs, JSON columns, enums as text |
| [Stored procedures](docs/stored-procedures.md) | Input, output and return values, procedures that return rows |
| [Benchmarks](docs/benchmarks.md) | MicroORM vs Dapper, and how to run the benchmarks |
| [Databases](docs/databases.md) | Per-dialect behavior and caveats |
| [Architecture](docs/architecture.md) | How MicroORM works inside, for contributors |

## Building from source

```shell
git clone https://github.com/HilthonTT/MicroORM.git
cd MicroORM
dotnet test MicroORM.slnx
```

The default test run covers unit tests and SQLite. To run the integration tests against the
other databases, see [CONTRIBUTING.md](CONTRIBUTING.md#integration-tests).

## Contributing

Contributions are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) and the
[Code of Conduct](CODE_OF_CONDUCT.md) first. Report security issues privately as described in
[SECURITY.md](SECURITY.md).

## Acknowledgements

MicroORM was inspired by [Dapper](https://github.com/DapperLib/Dapper) (fast, honest mapping),
[Massive](https://github.com/FransBouma/Massive) (dynamic rows and tiny surface area) and
[Entity Framework Core](https://github.com/dotnet/efcore) (typed queries and conventions).
MicroORM shares no code with any of them.

## License

[MIT](LICENSE) © 2026 Hilthon
