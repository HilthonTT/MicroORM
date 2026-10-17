# Stored procedures

Call a procedure by name with `ProcedureParameters`. The command runs with
`CommandType.StoredProcedure`, so the provider builds the call syntax.

```csharp
var parameters = new ProcedureParameters()
    .Add("customer_id", 42)
    .Output<decimal>("balance")
    .InputOutput("attempts", 0);

await db.ExecuteProcedureAsync("get_balance", parameters);

decimal balance = parameters.Get<decimal>("balance");
int attempts = parameters.Get<int>("attempts");
```

| Method | Adds |
|---|---|
| `Add(name, value)` | an input parameter |
| `Output<T>(name, size?)` | an output parameter; strings and byte arrays default to the maximum size |
| `InputOutput<T>(name, value, size?)` | a parameter that passes a value in and receives one back |
| `ReturnValue(name = "ReturnValue")` | the procedure's `RETURN` value (SQL Server) |

Give names without a prefix. MicroORM adds the one the provider expects (`@name` for SQL Server and
MySQL, a bare name for PostgreSQL).

Some providers write the procedure and parameter names into the generated `CALL` text, so MicroORM
only accepts identifiers: a procedure name is one to four dot-separated parts (`billing.get_balance`),
each bare or quoted the dialect's way (`"odd name"`, `[odd name]`, `` `odd name` ``), and a
parameter name uses letters, digits, `_` and `$`. Anything else throws `ArgumentException`. Never
build either from user input.

## Procedures that return rows

```csharp
var parameters = new ProcedureParameters().Add("min_age", 30).Output<int>("total_count");
var people = await db.QueryProcedureAsync<Person>("people_older_than", parameters);
int total = parameters.Get<int>("total_count");   // outputs are available once the rows are read
```

## Synchronous calls

`ExecuteProcedure` and `QueryProcedure<T>` are the synchronous forms.

## Per database

| Database | Notes |
|---|---|
| SQL Server | Inputs, outputs, input/outputs, return values and result sets. |
| MySQL / MariaDB | `IN`, `OUT` and `INOUT` parameters and result sets. |
| PostgreSQL | Procedures (`CALL`) with `OUT` and `INOUT` parameters. PostgreSQL procedures can't return rows: for a function that returns a table, use `db.QueryAsync<T>($"SELECT * FROM fn({arg})")`. |
| SQLite | No stored procedures; calls throw `NotSupportedException`. |

A `ProcedureParameters` instance holds the results of its most recent call. Use a new instance
for each call, and don't share one between concurrent calls.
