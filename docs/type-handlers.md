# Type handlers

A type handler teaches MicroORM to store a CLR type that isn't a database scalar: a value object, a
strongly typed ID, an object kept as JSON, or an enum stored as text. Once a type is registered,
properties of that type are mapped as ordinary columns. Values pass through the handler in every
direction:

- inserts, updates, upserts and `ExecuteUpdate`
- predicates (`Where(c => c.Email == email)`) and interpolated SQL holes
- materialization of entities, records, projections, scalars and multi-mapped rows

Handlers belong to one `MicroDb` (through its `MicroOptions`), not to the whole process.

## Value objects and strongly typed IDs

Pass two conversion functions:

```csharp
public sealed record Email(string Address);
public readonly record struct OrderId(long Value);

var options = new MicroOptions()
    .AddTypeHandler<Email, string>(e => e.Address, s => new Email(s))
    .AddTypeHandler<OrderId, long>(id => id.Value, v => new OrderId(v))
    .Entity<Order>(e => e.HasKey(o => o.Id, ValueGeneration.Identity));   // a typed ID isn't an integer, so say it's generated
```

```csharp
var customer = await db.Table<Customer>().FirstOrDefaultAsync(c => c.Email == new Email("ada@example.com"));
```

## JSON columns

`AddJsonColumn<T>()` stores `T` as JSON text using `System.Text.Json`:

```csharp
var options = new MicroOptions()
    .AddJsonColumn<Address>()
    .AddJsonColumn<List<string>>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
```

This works with `TEXT`, `NVARCHAR(MAX)` and MySQL `JSON` columns as is. PostgreSQL `jsonb` columns
need the parameter typed as `jsonb`. Subclass `JsonTypeHandler<T>` and set the provider type:

```csharp
public sealed class JsonbHandler<T> : JsonTypeHandler<T>
{
    public override void ConfigureParameter(DbParameter parameter)
    {
        if (parameter is NpgsqlParameter npgsql)
            npgsql.NpgsqlDbType = NpgsqlDbType.Jsonb;
    }
}

options.AddTypeHandler(new JsonbHandler<Address>());
```

> **Collections in raw SQL:** a collection in an interpolated hole normally expands to an `IN`
> list. To send a JSON-handled list as one value, wrap it: `$"... tags = {Sql.Param(tags)}"`.

## Enums stored as text

Without a handler, enums are sent as their underlying number. To store names instead:

```csharp
options.AddTypeHandler<Tier, string>(t => t.ToString(), s => Enum.Parse<Tier>(s));
```

Comparisons such as `Where(c => c.Tier == Tier.Gold)` send `'Gold'` too, because the translator
keeps the enum type of compared values.

## Writing a handler class

For full control, subclass `TypeHandler<T>`:

```csharp
public sealed class MoneyHandler : TypeHandler<Money>
{
    public override object? Write(Money value) => value.Cents;                     // never called with null
    public override Money Read(object value) => new Money(Convert.ToInt64(value)); // never called with null or DBNull
    public override void ConfigureParameter(DbParameter parameter) => parameter.DbType = DbType.Int64;
}

options.AddTypeHandler(new MoneyHandler());
```

`Read` receives whatever the provider returns. SQLite and MySQL, for example, return `long` for
integer columns, so convert defensively.
