using Microsoft.Data.Sqlite;

namespace MicroORM.Benchmarks;

/// <summary>A shared in-memory SQLite database with 1,000 posts.</summary>
public sealed class BenchmarkDatabase : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    public BenchmarkDatabase()
    {
        ConnectionString = $"Data Source=bench-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();

        using var command = _keepAlive.CreateCommand();
        command.CommandText = """
            CREATE TABLE posts (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT NOT NULL, body TEXT NOT NULL, views INTEGER NOT NULL, published INTEGER NOT NULL);
            WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 1000)
            INSERT INTO posts (title, body, views, published) SELECT 'Post ' || i, printf('%.200c', 'x'), i * 7, i % 2 FROM n;
            """;
        command.ExecuteNonQuery();
    }

    public string ConnectionString { get; }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    public void Dispose() => _keepAlive.Dispose();
}
