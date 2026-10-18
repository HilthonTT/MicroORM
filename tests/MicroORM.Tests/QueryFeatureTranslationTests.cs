using MicroORM.Configuration;
using MicroORM.Dialects;
using MicroORM.Mapping;
using MicroORM.Querying;
using MicroORM.Sessions;
using MicroORM.Tests.Model;
using System.Data.Common;

namespace MicroORM.Tests;

/// <summary>Checks the SQL of query filters, grouping, Distinct, subqueries and multi-row writes without a database.</summary>
public class QueryFeatureTranslationTests
{
    private const string PersonColumns = "\"id\", \"name\", \"email\", \"age\", \"active\", \"mood\", \"created_at\", \"external_id\"";

    private static string WhereOf(Query<Person> query) => query.ToString().Split(" WHERE ", 2)[1];

    // ---- Query filters -------------------------------------------------------------------------

    [Fact]
    public void Query_filters_come_first_and_can_be_ignored()
    {
        var db = new MicroDb(SqlDialect.Sqlite, NoConnection, new MicroOptions { Naming = NamingConvention.SnakeCase }
            .Entity<Person>(e => e.HasQueryFilter(p => p.Active).HasQueryFilter(p => p.Age >= 18 || p.Email != null)));
        var people = db.Table<Person>();

        Assert.Equal("(\"active\" = @p0) AND ((\"age\" >= @p1) OR (\"email\" IS NOT NULL)) AND (\"name\" = @p2)", WhereOf(people.Where(p => p.Name == "x")));
        Assert.Equal("SELECT " + PersonColumns + " FROM \"micro_people\"", people.IgnoreQueryFilters().ToString());
        Assert.Equal("(\"name\" = @p0)", WhereOf(people.Where(p => p.Name == "x").IgnoreQueryFilters()));
    }

    [Fact]
    public void Query_filters_read_captured_values_for_every_statement()
    {
        var tenant = new TenantScope { MinimumAge = 18 };
        var db = new MicroDb(SqlDialect.Sqlite, NoConnection, new MicroOptions().Entity<Person>(e => e.HasQueryFilter(p => p.Age >= tenant.MinimumAge)));
        var query = db.Table<Person>().Where(p => p.Active);

        Assert.Equal(18, query.ToSql().Render(SqlDialect.Sqlite).Parameters[0].Value);
        tenant.MinimumAge = 21;
        Assert.Equal(21, query.ToSql().Render(SqlDialect.Sqlite).Parameters[0].Value);
    }

    private static DbConnection NoConnection() => throw new InvalidOperationException("No database in translation tests.");

    private sealed class TenantScope
    {
        public int MinimumAge { get; set; }
    }
}
