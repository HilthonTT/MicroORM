# Getting started

## 1. Install

Add MicroORM and the ADO.NET provider for your database:

```shell
dotnet add package MicroORM
dotnet add package Npgsql                     # PostgreSQL
# or: Microsoft.Data.Sqlite, Microsoft.Data.SqlClient, MySqlConnector
```

## 2. Create a `MicroDb`

`MicroDb` is the entry point. It is thread-safe and meant to live for the whole application,
for example as a singleton in dependency injection. Give it a dialect and a way to get connections:

```csharp
using MicroORM.Configuration;
using MicroORM.Dialects;
using MicroORM.Mapping;
using MicroORM.Sessions;

// From a connection factory. MicroORM opens and closes each connection; the provider pools them.
var db = new MicroDb(SqlDialect.SqlServer, () => new SqlConnection(connectionString));

// From a DbDataSource (Npgsql's recommended pattern).
await using var dataSource = NpgsqlDataSource.Create(connectionString);
var db = new MicroDb(SqlDialect.PostgreSql, dataSource, new MicroOptions
{
    Naming = NamingConvention.SnakeCase,   // UserId <-> user_id
});
```

With ASP.NET Core:

```csharp
builder.Services.AddSingleton(_ => new MicroDb(
    SqlDialect.PostgreSql,
    NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Default")!),
    new MicroOptions { Naming = NamingConvention.SnakeCase }));
```

### Namespaces

Each area of MicroORM has its own namespace. Import the ones you use:

| Namespace | Main types |
|---|---|
| `MicroORM.Sessions` | `MicroDb`, `MicroTransaction`, `MicroSession`, `ResultSets` |
| `MicroORM.Dialects` | `SqlDialect` |
| `MicroORM.Configuration` | `MicroOptions`, `CommandLog` |
| `MicroORM.Sql` | `Sql` (interpolated SQL), `RenderedSql` |
| `MicroORM.Querying` | `Table<T>`, `Query<T>`, `Projection<T, TResult>`, `UpdateBuilder<T>` |
| `MicroORM.Mapping` | `NamingConvention`, `EntityBuilder<T>`, `EntityMap`, `ValueGeneration` |
| `MicroORM.Materialization` | `Row` |
| `MicroORM.TypeHandlers` | `TypeHandler<T>`, `JsonTypeHandler<T>` |
| `MicroORM.Procedures` | `ProcedureParameters`, `DbTypes` |
| `MicroORM.Exceptions` | `MicroConcurrencyException` |

> If your own code lives in a namespace that starts with `MicroORM.`, the name `Sql` resolves to
> the `MicroORM.Sql` namespace. Add `using Sql = MicroORM.Sql.Sql;` after your `namespace` line.

## 3. Describe an entity

Any class with public properties works. No base class or attributes are required:

```csharp
public class Customer
{
    public int Id { get; set; }                 // key by convention; database-generated
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public bool Active { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

See [Mapping](mapping.md) to change table or column names, keys, and generated columns.

## 4. Query and write

```csharp
var customers = db.Table<Customer>();

await customers.InsertAsync(new Customer { Name = "Ada", Active = true, CreatedAt = DateTime.UtcNow });

var active = await customers.Where(c => c.Active).OrderBy(c => c.Name).ToListAsync();
var count = await customers.Where(c => c.Email != null).CountAsync();

// Raw SQL when you need it; holes are parameters.
var domain = "example.com";
var rows = await db.QueryAsync<Customer>($"SELECT * FROM customer WHERE email LIKE {"%@" + domain}");
```

## Next steps

- [Interpolated SQL](sql.md): composing SQL safely
- [Querying](querying.md): what translates, paging, set-based updates
- [CRUD and transactions](crud-and-transactions.md)
