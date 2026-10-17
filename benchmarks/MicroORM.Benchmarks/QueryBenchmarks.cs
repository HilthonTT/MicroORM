using BenchmarkDotNet.Attributes;
using Dapper;
using MicroORM.Dialects;
using MicroORM.Sessions;
using Microsoft.Data.Sqlite;

namespace MicroORM.Benchmarks;

/// <summary>
/// MicroORM against Dapper on the same database, provider and connection handling: each operation
/// opens a pooled connection, as both libraries do in typical use.
/// </summary>
[MemoryDiagnoser]
public class QueryBenchmarks
{
    private BenchmarkDatabase _database = null!;
    private MicroDb _micro = null!;
    private int _id;

    [GlobalSetup]
    public void Setup()
    {
        _database = new BenchmarkDatabase();
        _micro = new MicroDb(SqlDialect.Sqlite, () => new SqliteConnection(_database.ConnectionString));
    }

    [GlobalCleanup]
    public void Cleanup() => _database.Dispose();

    private int NextId() => _id = (_id % 1000) + 1;

    [Benchmark(Baseline = true, Description = "Dapper: one row by id")]
    [BenchmarkCategory("Single")]
    public Post? Dapper_single()
    {
        using var connection = new SqliteConnection(_database.ConnectionString);
        return connection.QueryFirstOrDefault<Post>("SELECT id, title, body, views, published FROM posts WHERE id = @id", new { id = NextId() });
    }

    [Benchmark(Description = "MicroORM: one row by id (raw SQL)")]
    [BenchmarkCategory("Single")]
    public Post? MicroORM_single_raw() =>
        _micro.QueryFirstOrDefault<Post>($"SELECT id, title, body, views, published FROM posts WHERE id = {NextId()}");

    [Benchmark(Description = "MicroORM: one row by id (Find)")]
    [BenchmarkCategory("Single")]
    public Post? MicroORM_single_find() => _micro.Table<Post>().Find(NextId());

    [Benchmark(Description = "MicroORM: one row by id (typed Where)")]
    [BenchmarkCategory("Single")]
    public Post? MicroORM_single_where()
    {
        var id = NextId();
        return _micro.Table<Post>().FirstOrDefault(p => p.Id == id);
    }

    [Benchmark(Description = "Dapper: 500 rows")]
    [BenchmarkCategory("Many")]
    public List<Post> Dapper_many()
    {
        using var connection = new SqliteConnection(_database.ConnectionString);
        return connection.Query<Post>("SELECT id, title, body, views, published FROM posts WHERE id <= @max", new { max = 500 }).AsList();
    }

    [Benchmark(Description = "MicroORM: 500 rows")]
    [BenchmarkCategory("Many")]
    public List<Post> MicroORM_many() =>
        _micro.Query<Post>($"SELECT id, title, body, views, published FROM posts WHERE id <= {500}");
}
