using MicroORM.Configuration;
using MicroORM.Dialects;
using MicroORM.Mapping;
using MicroORM.Sessions;
using MicroORM.Tests.Model;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using System.Data.Common;

namespace MicroORM.Tests.Integration;

using Sql = MicroORM.Sql.Sql;

/// <summary>
/// A freshly created schema on one provider. SQLite always runs (in memory); PostgreSQL,
/// SQL Server and MySQL run when their connection string is in the environment:
/// <c>MICROORM_TEST_POSTGRES</c>, <c>MICROORM_TEST_SQLSERVER</c>, <c>MICROORM_TEST_MYSQL</c>.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly DbConnection? _keepAlive;

    private TestDatabase(string provider, MicroDb db, DbConnection? keepAlive)
    {
        Provider = provider;
        Db = db;
        _keepAlive = keepAlive;
    }

    public string Provider { get; }

    public MicroDb Db { get; }

    public List<CommandLog> Log { get; } = [];

    public static TheoryData<string> Providers => With(["sqlite", "postgres", "sqlserver", "mysql"]);

    /// <summary>The configured providers among <paramref name="candidates"/>.</summary>
    public static TheoryData<string> With(string[] candidates)
    {
        var providers = new TheoryData<string>();
        foreach (var name in candidates)
        {
            if (IsConfigured(name))
            {
                providers.Add(name);
            }
        }

        return providers;
    }

    /// <summary>Whether a provider can run: SQLite always, the others with a connection string.</summary>
    public static bool IsConfigured(string provider) => provider == "sqlite" || ConnectionString(provider) is not null;

    /// <summary>Options with the naming convention and type handlers every test database uses.</summary>
    public static MicroOptions CreateOptions() => new MicroOptions { Naming = NamingConvention.SnakeCase }
        .AddTypeHandler<Email, string>(e => e.Address, s => new Email(s))
        .AddTypeHandler<CustomerTier, string>(t => t.ToString(), s => Enum.Parse<CustomerTier>(s))
        .AddTypeHandler(new JsonbHandler<Address>())
        .AddJsonColumn<List<string>>();

    /// <param name="provider">The provider name.</param>
    /// <param name="configure">Changes to the options, such as query filters.</param>
    public static async Task<TestDatabase> CreateAsync(string provider, Action<MicroOptions>? configure = null)
    {
        var options = CreateOptions();
        configure?.Invoke(options);
        TestDatabase database;

        switch (provider)
        {
            case "sqlite":
                // A named in-memory database lives as long as one connection to it stays open.
                var connectionString = $"Data Source=microorm-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
                var keepAlive = new SqliteConnection(connectionString);
                await keepAlive.OpenAsync();
                database = new TestDatabase(provider, new MicroDb(SqlDialect.Sqlite, () => new SqliteConnection(connectionString), options), keepAlive);
                break;
            case "postgres":
                database = new TestDatabase(provider, new MicroDb(SqlDialect.PostgreSql, () => new NpgsqlConnection(ConnectionString(provider)), options), null);
                break;
            case "sqlserver":
                database = new TestDatabase(provider, new MicroDb(SqlDialect.SqlServer, () => new SqlConnection(ConnectionString(provider)), options), null);
                break;
            case "mysql":
                database = new TestDatabase(provider, new MicroDb(SqlDialect.MySql, () => new MySqlConnection(ConnectionString(provider)), options), null);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider));
        }

        options.Log = database.Log.Add;
        foreach (var statement in TestSchema.For(provider))
        {
            await database.Db.ExecuteAsync(Sql.Raw(statement));
        }

        database.Log.Clear();
        return database;
    }

    public async ValueTask DisposeAsync()
    {
        if (_keepAlive is not null)
        {
            await _keepAlive.DisposeAsync();
        }
    }

    private static string? ConnectionString(string provider) => provider switch
    {
        "postgres" => Environment.GetEnvironmentVariable("MICROORM_TEST_POSTGRES"),
        "sqlserver" => Environment.GetEnvironmentVariable("MICROORM_TEST_SQLSERVER"),
        "mysql" => Environment.GetEnvironmentVariable("MICROORM_TEST_MYSQL"),
        _ => null,
    };
}
