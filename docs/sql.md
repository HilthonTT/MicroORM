# Interpolated SQL

Every MicroORM method that takes SQL takes a `Sql`, not a `string`. `Sql` is a C#
[interpolated string handler](https://learn.microsoft.com/dotnet/csharp/advanced-topics/performance/interpolated-string-handler),
so the compiler gives each `{hole}` to MicroORM separately from the literal text. MicroORM sends each
hole as a database parameter.

```csharp
var name = "Robert'); DROP TABLE students;--";
await db.QueryAsync<User>($"SELECT * FROM users WHERE name = {name}");
// SELECT * FROM users WHERE name = @p0      (@p0 = "Robert'); DROP TABLE students;--")
```

A plain `string` does not convert to `Sql`, so this is a **compile error**, not a vulnerability:

```csharp
string query = "SELECT * FROM users WHERE name = '" + name + "'";
await db.QueryAsync<User>(query);   // error CS1503: cannot convert from 'string' to 'MicroORM.Sql'
```

## What a hole can hold

| Hole value | Rendered as |
|---|---|
| A scalar (`int`, `string`, `Guid`, `DateTime`, `byte[]`, …) | one parameter: `@p0` |
| `null` | a parameter with `DBNull.Value` |
| An enum | a parameter holding its underlying number |
| A collection (array, `List<T>`, any `IEnumerable` except `string` and `byte[]`) | a parameter list: `(@p0, @p1, @p2)` |
| An empty collection | `(NULL)`, so `x IN (NULL)` matches nothing |
| Another `Sql` | the fragment inlined, keeping its own parameters |

```csharp
int[] ids = [3, 5, 8];
await db.QueryAsync<User>($"SELECT * FROM users WHERE id IN {ids}");
// SELECT * FROM users WHERE id IN (@p0, @p1, @p2)
```

## Composing fragments

Build a query from parts without losing parameterization:

```csharp
Sql filter = onlyActive ? $"active = {true}" : Sql.Raw("1 = 1");
Sql order = Sql.Raw(sortDescending ? "created_at DESC" : "created_at");

var users = await db.QueryAsync<User>($"SELECT * FROM users WHERE {filter} ORDER BY {order}");
```

Helpers:

| Helper | Use |
|---|---|
| `Sql.Raw(text)` | Trusted SQL text, inserted verbatim. **Never pass user input.** |
| `Sql.Identifier(name)` | A table or column name, quoted for the dialect (`"users"`, `[users]`, `` `users` ``). Dotted names quote per part. |
| `Sql.Param(value)` | A single parameter as a fragment. |
| `Sql.Join(separator, fragments)` | Joins fragments, skipping empty ones. |
| `a + b` | Concatenates two fragments. |
| `Sql.Empty` | A new, empty fragment. |

Sorting by a column the user chose is the classic case for `Sql.Identifier`:

```csharp
var allowed = new HashSet<string> { "name", "created_at" };
var column = allowed.Contains(sortBy) ? sortBy : "name";
await db.QueryAsync<User>($"SELECT * FROM users ORDER BY {Sql.Identifier(column)}");
```

Quoting escapes the identifier, but checking it against an allow-list is still the safe design.

## Inspecting SQL

`ToString()` renders a fragment with SQLite syntax for debugging. `Render(dialect)` returns the
exact text and parameters for a dialect:

```csharp
Sql sql = $"SELECT * FROM users WHERE id IN {ids}";
RenderedSql rendered = sql.Render(SqlDialect.SqlServer);
Console.WriteLine(rendered.Text);
foreach (var p in rendered.Parameters) Console.WriteLine($"{p.Name} = {p.Value}");
```

To see every executed command, set `MicroOptions.Log` (see [CRUD and transactions](crud-and-transactions.md#logging)).

## Running SQL

| Method | Returns |
|---|---|
| `QueryAsync<T>(sql)` | `List<T>` |
| `StreamAsync<T>(sql)` | `IAsyncEnumerable<T>`, reading rows without buffering |
| `QueryFirstOrDefaultAsync<T>(sql)` | the first row, or `default` |
| `QuerySingleAsync<T>(sql)` | the only row; throws on zero or several |
| `QuerySingleOrDefaultAsync<T>(sql)` | the only row or `default`; throws on several |
| `ScalarAsync<T>(sql)` | the first column of the first row |
| `ExecuteAsync(sql)` | the number of affected rows |

`T` can be:

- an **entity or class**: columns fill settable properties.
- a **record or other constructor-only type**: columns fill constructor parameters by name.
- a **scalar** (`int`, `string`, `Guid`, …): the first column is used.
- **`Row`** or **`object`**: an untyped row, readable as `row["name"]`, `row.Get<int>("age")` or `dynamic`.

Columns match members by mapped column name, then by member name, ignoring case and
underscores. So `user_id`, `UserId` and `USERID` all fill `UserId`.
