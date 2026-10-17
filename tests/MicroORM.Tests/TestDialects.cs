using MicroORM.Dialects;

namespace MicroORM.Tests;

/// <summary>Looks dialects up by the display names used in theory data.</summary>
internal static class TestDialects
{
    public static SqlDialect ByName(string name) => name switch
    {
        "SQLite" => SqlDialect.Sqlite,
        "PostgreSQL" => SqlDialect.PostgreSql,
        "SQL Server" => SqlDialect.SqlServer,
        "MySQL" => SqlDialect.MySql,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };
}
