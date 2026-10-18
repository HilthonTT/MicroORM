# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Query filters: `EntityBuilder<T>.HasQueryFilter(x => ...)` adds a condition (such as a soft delete or a tenant) to every typed query, `Find`, `ExecuteUpdate` and `ExecuteDelete` of the entity. Captured values are read per statement. `Query<T>.IgnoreQueryFilters()` skips them.
- Typed queries translate more expressions: `DateTime` and `DateOnly` `Year`/`Month`/`Day` (and `Hour`/`Minute`/`Second` for `DateTime`), `Math.Abs`/`Floor`/`Ceiling`, string `Substring`, `Replace`, `TrimStart`, `TrimEnd` and `string.IsNullOrWhiteSpace`.
- `SqlFunctions.Like(value, pattern)` for `LIKE` patterns with your own wildcards.
- `SqlDialect.DatePart` and `SqlDialect.Substring` for dialects to override.

### Security

- `Where(Sql)` conditions are now parenthesized, so an `OR` in a raw condition can no longer escape the `AND` that joins it to other filters (for example a tenant filter).
- Stored procedure names must be identifiers (optionally schema-qualified, quoted the dialect's way), and procedure parameter names must be letters, digits, `_` and `$`. Npgsql and MySqlConnector write these into `CALL name(...)` verbatim.

### Fixed

- `FirstOrDefault`, `Single` and `Any` no longer override a smaller `Take`: `Take(0).Any()` is `false` and `Take(1).Single()` no longer throws when more rows exist.
- `Take(0)` works on SQL Server, which rejects `FETCH NEXT 0 ROWS`.
- Conditions that don't reference the entity (`flag`, `x > 3`, `nullable.HasValue`) become `1 = 1` / `1 = 0` instead of a bare parameter that SQL Server can't use as a predicate.
- Closed `array.Contains(value)` expressions no longer throw under C# 14 first-class spans.
- `list.Contains(x.Column)` matches `NULL` when the list contains `null`, as in C#.
- The bitwise complement `~x` is rejected instead of being translated as a logical `NOT`.
- The string `"0"` converts to `false`.
- `Skip` and `Take` compose in call order, as in LINQ: `Take(10).Skip(5)` returns 5 rows (not 10), `Skip(2).Skip(3)` skips 5 and `Take(5).Take(10)` returns 5.
- A failed `Commit` is no longer hidden by a second exception from the rollback on dispose.
- Type handlers registered for a property's declared type (such as `AddJsonColumn<IReadOnlyList<string>>()`) now convert parameter values whose runtime type differs (a `List<string>`).
- `[Key]` properties with `[Column]` but no `Order` sort after keys with an explicit `Order` instead of before them.
- `ValueConverter.To<object>` and `Scalar<object>` return `null` for `DBNull`.

## [0.1.0] - 2026-10-16

### Added

- `Sql` interpolated string handler: parameterized holes, composable fragments, `IN` list expansion, `Sql.Raw`, `Sql.Identifier`, `Sql.Join`.
- `MicroDb` with connection-factory and `DbDataSource` constructors; `MicroTransaction` with the same API; scoped `TransactionAsync`.
- Raw SQL execution: `QueryAsync`, `StreamAsync`, `QueryFirstOrDefaultAsync`, `QuerySingleAsync`, `QuerySingleOrDefaultAsync`, `ScalarAsync`, `ExecuteAsync`.
- Materialization into classes, records (constructor binding), scalars and dynamic `Row`s, with compiled, cached readers.
- Mapping by conventions, data annotation attributes and fluent `MicroOptions.Entity<T>()`, with `AsIs`, `SnakeCase` and `LowerCase` naming.
- `Table<T>` CRUD: `InsertAsync` (generated keys read back), `InsertManyAsync`, `UpdateAsync`, `DeleteAsync`, `DeleteByKeyAsync`, `FindAsync`.
- Typed queries: `Where`, `OrderBy`/`ThenBy` (and descending), `Skip`, `Take`, `ToListAsync`, `StreamAsync`, `FirstOrDefaultAsync`, `SingleAsync`, `CountAsync`, `AnyAsync`.
- Set-based `ExecuteUpdateAsync` (`Set`, `SetFrom`) and `ExecuteDeleteAsync`.
- Dialects for SQLite, PostgreSQL, SQL Server and MySQL.
- Command logging through `MicroOptions.Log`.
- Projections with `Select` (anonymous types, records, DTOs, single values) and aggregates: `SumAsync`, `MinAsync`, `MaxAsync`, `AverageAsync`.
- Multi-mapping joins into two or three objects (`QueryAsync<T1, T2, TResult>`, `splitOn`), with `null` for unmatched `LEFT JOIN`s.
- Multiple result sets with `QueryMultipleAsync` and `ResultSets`.
- Type handlers: `TypeHandler<T>`, `AddTypeHandler<T, TProvider>` for value objects and typed IDs, `AddJsonColumn<T>` / `JsonTypeHandler<T>`.
- Composite primary keys (`[Key]` with `[Column(Order)]`, or `HasKey(x => new { ... })`); keys are passed as tuples.
- `UpsertAsync` on the key or on unique columns (`ON CONFLICT`, `MERGE`, `ON DUPLICATE KEY UPDATE`), reading back generated keys.
- Optimistic concurrency with `[ConcurrencyCheck]` / `IsVersion`, raising `MicroConcurrencyException`.
- Stored procedures: `ExecuteProcedureAsync`, `QueryProcedureAsync` and `ProcedureParameters` with output, input/output and return values.
- A synchronous twin for every asynchronous API.
- A BenchmarkDotNet project comparing MicroORM with Dapper.

[Unreleased]: https://github.com/HilthonTT/MicroORM/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/HilthonTT/MicroORM/releases/tag/v0.1.0
