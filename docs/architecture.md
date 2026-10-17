# Architecture

This page is for contributors. It explains how a call flows through MicroORM and where each
responsibility lives.

## Source layout

Every type has its own file, named after the type. Folders group files by responsibility, and
each folder is its own namespace (`MicroORM.Sessions`, `MicroORM.Querying`, `MicroORM.Sql`, ...).
Inside the library, `Sql` names the `MicroORM.Sql` namespace, so code refers to the type as
`Sql.Sql` (or through a `using SQL = MicroORM.Sql.Sql;` alias).

```
src/MicroORM/
├── Sql/              Sql (interpolated string handler, composition, rendering), RenderedSql,
│                     SqlParameterValue, and the internal part records
├── Sessions/         MicroSession (+ .Sync, .Procedures partials), MicroDb, MicroTransaction,
│                     ResultSets, MicroEngine, PreparedCommand, ConnectionLease
├── Configuration/    MicroOptions, CommandLog
├── Dialects/         SqlDialect and one class per database; ColumnValue
├── Mapping/          EntityMap, ColumnMap, ValueGeneration, MappingRegistry, EntityBuilder,
│                     EntityConfiguration, NamingConvention and its implementations
├── Materialization/  Materializer, ValueConverter, ScalarTypes, Row, RowSchema
├── Querying/         Query, Table, Projection, UpdateBuilder, ExpressionTranslator, EntityFinder
├── TypeHandlers/     TypeHandler<T>, ITypeHandler, JsonTypeHandler, DelegateTypeHandler,
│                     TypeHandlerRegistry
├── Procedures/       ProcedureParameters, ProcedureParameter, DbTypes
└── Exceptions/       MicroConcurrencyException
```

## Sync and async from one implementation

Each operation is written once, as `...Core(bool async, ...)` returning a `ValueTask`. The public
`...Async` method passes `true`. The synchronous method passes `false` and unwraps the result
with `MicroSession.Sync`, which asserts that the task already completed. With `async: false`, a
core only calls synchronous ADO.NET methods (`ExecuteReader`, `Read`, `Open`), so no
asynchronous work is ever blocked on. Npgsql and Microsoft.Data.SqlClient use the same pattern
internally.

## Type handlers

`MicroEngine.Prepare` runs after rendering. For each parameter, it applies the registered
handler for the value's type (or for its declared type, when the value is `null`), then turns
enums into numbers. `CreateCommand` lets the handler configure the provider parameter. When
reading, the materializer compiles a call to the handler for handled member types.

## Flow of a typed query

```
db.Table<User>().Where(u => u.Age > min).ToListAsync()
        │
        ▼
MappingRegistry.GetMap(User)          cached EntityMap: table, columns, key
        │
        ▼
ExpressionTranslator.Predicate        u.Age > min  →  Sql: ("age" > {param 30})
        │
        ▼
Query<T>.ToSql()                      SELECT <columns> FROM <table> WHERE ... ORDER BY ... <paging>
        │
        ▼
Sql.Render(dialect)                   text with @p0..@pn + parameter list; identifiers quoted
        │
        ▼
MicroSession.StreamAsync               lease connection → DbCommand → reader → Log
        │
        ▼
Materializer.For<T>(reader)           compiled Func<DbDataReader, T>, cached per column layout
```

## Design principles

1. **`Sql` is the only way SQL enters MicroORM.** Every API takes `Sql`, and `Sql` is built from
   interpolation, `Raw`, `Identifier` or composition. Rendering is deferred until the dialect is
   known, so fragments are dialect-neutral and parameters are numbered once, globally.
2. **Fail loudly.** The translator throws on unsupported shapes rather than evaluating client-side.
   Materialization throws `InvalidCastException` naming the value and target type.
3. **No provider references.** Anything provider-specific goes through `SqlDialect`, including
   `ConfigureParameter` for type hints.
4. **One session model.** `MicroDb` and `MicroTransaction` differ only in `LeaseAsync`, the method
   that hands out a connection (and transaction). Every operation is written once, in `MicroSession`.
5. **Cache what reflection builds.** Entity maps are cached per `MicroDb`. Row readers are cached
   per (type, column names). Property getters are compiled once per column.

## Adding a translation

1. Add a case in `ExpressionTranslator.VisitCall`, `VisitMember` or `VisitBinary`.
2. If the SQL differs by database, add a virtual member to `SqlDialect` and override it in `Dialects.cs`.
3. Add a SQL-shape test in `QueryTranslationTests` and a behavior test in `Integration/ProviderTests`
   so it runs against all four databases in CI.
4. Document it in the table in [querying.md](querying.md#what-translates).
