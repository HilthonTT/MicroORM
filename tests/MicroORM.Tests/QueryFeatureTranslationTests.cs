using MicroORM.Configuration;
using MicroORM.Dialects;
using MicroORM.Mapping;
using MicroORM.Querying;
using MicroORM.Sessions;
using MicroORM.Tests.Model;
using System.Data.Common;

namespace MicroORM.Tests;

using Sql = MicroORM.Sql.Sql;

/// <summary>Checks the SQL of query filters, grouping, Distinct, subqueries and multi-row writes without a database.</summary>
public class QueryFeatureTranslationTests
{
    private const string PersonColumns = "\"id\", \"name\", \"email\", \"age\", \"active\", \"mood\", \"created_at\", \"external_id\"";

    private static readonly MicroDb Db = new(SqlDialect.Sqlite, NoConnection, new MicroOptions { Naming = NamingConvention.SnakeCase });

    private static Table<Person> People => Db.Table<Person>();

    private static Table<Order> Orders => Db.Table<Order>();

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

    // ---- Distinct ------------------------------------------------------------------------------

    [Fact]
    public void Distinct_drops_the_ordering_and_pages_first()
    {
        Assert.Equal("SELECT DISTINCT \"mood\" AS \"value\" FROM \"micro_people\" WHERE (\"age\" > @p0)", People.Where(p => p.Age > 1).OrderBy(p => p.Name).Select(p => p.Mood).Distinct().ToString());
        Assert.Equal(
            "SELECT DISTINCT \"mood\" AS \"value\" FROM (SELECT " + PersonColumns + " FROM \"micro_people\" ORDER BY \"age\" LIMIT 3) AS \"paged\"",
            People.OrderBy(p => p.Age).Take(3).Select(p => p.Mood).Distinct().ToString());
    }

    // ---- Subqueries ----------------------------------------------------------------------------

    [Fact]
    public void Any_becomes_a_correlated_exists()
    {
        Assert.Equal(
            "EXISTS (SELECT 1 FROM \"micro_orders\" AS \"s0\" WHERE (\"s0\".\"person_id\" = \"micro_people\".\"id\"))",
            WhereOf(People.Where(p => Orders.Any(o => o.PersonId == p.Id))));
        Assert.Equal("EXISTS (SELECT 1 FROM \"micro_orders\" AS \"s0\")", WhereOf(People.Where(p => Orders.Any())));
    }

    [Fact]
    public void Filtered_and_paged_subqueries_keep_their_filters_inside_a_derived_table()
    {
        var large = Orders.Where(o => o.Amount > 100);
        Assert.Equal(
            "NOT (EXISTS (SELECT 1 FROM (SELECT \"id\", \"person_id\", \"amount\" FROM \"micro_orders\" WHERE (\"amount\" > @p0)) AS \"s0\" WHERE (\"s0\".\"person_id\" = \"micro_people\".\"id\")))",
            WhereOf(People.Where(p => !large.Any(o => o.PersonId == p.Id))));

        var cheapest = Orders.OrderBy(o => o.Amount).Take(2);
        Assert.Equal(
            "EXISTS (SELECT 1 FROM (SELECT \"id\", \"person_id\", \"amount\" FROM \"micro_orders\" ORDER BY \"amount\" LIMIT 2) AS \"s0\" WHERE (\"s0\".\"person_id\" = \"micro_people\".\"id\"))",
            WhereOf(People.Where(p => cheapest.Any(o => o.PersonId == p.Id))));
    }

    [Fact]
    public void Nested_subqueries_alias_each_level()
    {
        Assert.Equal(
            "EXISTS (SELECT 1 FROM \"micro_people\" AS \"s0\" WHERE ((\"s0\".\"age\" > \"micro_people\".\"age\") AND EXISTS (SELECT 1 FROM \"micro_orders\" AS \"s1\" WHERE (\"s1\".\"person_id\" = \"s0\".\"id\"))))",
            WhereOf(People.Where(p => People.Any(q => q.Age > p.Age && Orders.Any(o => o.PersonId == q.Id)))));
    }

    [Fact]
    public void Contains_on_a_projection_matches_nulls_like_csharp()
    {
        Assert.Equal(
            "EXISTS (SELECT 1 FROM (SELECT \"person_id\" AS \"value\" FROM \"micro_orders\") AS \"s0\" WHERE (\"s0\".\"value\" = \"micro_people\".\"id\"))",
            WhereOf(People.Where(p => Orders.Select(o => o.PersonId).Contains(p.Id))));

        var ids = People.Select(p => (int?)p.Id);
        Assert.EndsWith(
            "WHERE (\"s0\".\"value\" = \"micro_orders\".\"person_id\" OR (\"s0\".\"value\" IS NULL AND \"micro_orders\".\"person_id\" IS NULL)))",
            Orders.Where(o => ids.Contains(o.PersonId)).ToString(),
            StringComparison.Ordinal);
        Assert.EndsWith("WHERE (\"s0\".\"value\" IS NULL))", People.Where(p => Orders.Select(o => o.PersonId).Contains(null)).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Subqueries_project_as_case_expressions()
    {
        Assert.Equal(
            "SELECT \"name\" AS \"Name\", CASE WHEN EXISTS (SELECT 1 FROM \"micro_orders\" AS \"s0\" WHERE (\"s0\".\"person_id\" = \"micro_people\".\"id\")) THEN 1 ELSE 0 END AS \"HasOrders\" FROM \"micro_people\"",
            People.Select(p => new { p.Name, HasOrders = Orders.Any(o => o.PersonId == p.Id) }).ToString());
    }

    [Fact]
    public void Subqueries_quote_for_the_dialect()
    {
        var db = new MicroDb(SqlDialect.SqlServer, NoConnection);
        Assert.EndsWith(
            "WHERE EXISTS (SELECT 1 FROM [micro_orders] AS [s0] WHERE ([s0].[PersonId] = [micro_people].[Id]))",
            db.Table<Person>().Where(p => db.Table<Order>().Any(o => o.PersonId == p.Id)).ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_subqueries_throw_instead_of_running()
    {
        // The source can't depend on the row, must be for the same dialect and must project one value.
        Assert.Throws<NotSupportedException>(() => People.Where(p => Orders.Where(o => o.PersonId == p.Id).Any()));
        var postgres = new MicroDb(SqlDialect.PostgreSql, NoConnection).Table<Order>();
        Assert.Throws<NotSupportedException>(() => People.Where(p => postgres.Any(o => o.PersonId == p.Id)));
        var pairs = Orders.Select(o => new { o.PersonId });
        Assert.Throws<NotSupportedException>(() => People.Where(p => pairs.Contains(new { PersonId = (int?)p.Id })));
    }

    // ---- Multi-row writes ----------------------------------------------------------------------

    [Theory]
    [InlineData("SQLite", "INSERT INTO \"t\" (\"a\", \"b\") VALUES (@p0, @p1), (@p2, @p3)")]
    [InlineData("SQL Server", "INSERT INTO [t] ([a], [b]) VALUES (@p0, @p1), (@p2, @p3)")]
    [InlineData("MySQL", "INSERT INTO `t` (`a`, `b`) VALUES (@p0, @p1), (@p2, @p3)")]
    public void Insert_rows_per_dialect(string dialect, string expected)
    {
        var d = TestDialects.ByName(dialect);
        static IReadOnlyList<ColumnValue> Row(int a, string b) => [new("a", a, typeof(int)), new("b", b, typeof(string))];
        Assert.Equal(expected, d.InsertRows("t", [Row(1, "x"), Row(2, "y")]).Render(d).Text);
    }

    [Theory]
    [InlineData("SQLite", "UPDATE \"t\" SET \"a\" = 1 WHERE b > 2 RETURNING \"id\", \"a\"")]
    [InlineData("PostgreSQL", "UPDATE \"t\" SET \"a\" = 1 WHERE b > 2 RETURNING \"id\", \"a\"")]
    [InlineData("SQL Server", "UPDATE [t] SET [a] = 1 OUTPUT INSERTED.[id], INSERTED.[a] WHERE b > 2")]
    public void Update_returning_per_dialect(string dialect, string expected)
    {
        var d = TestDialects.ByName(dialect);
        Assert.Equal(expected, d.UpdateReturning("t", Sql.Identifier("a") + Sql.Raw(" = 1"), Sql.Raw("b > 2"), ["id", "a"]).Render(d).Text);
    }

    [Fact]
    public void MySql_cannot_return_updated_rows()
    {
        Assert.Throws<NotSupportedException>(() => SqlDialect.MySql.UpdateReturning("t", Sql.Raw("a = 1"), null, ["id"]));
    }

    private static DbConnection NoConnection() => throw new InvalidOperationException("No database in translation tests.");

    private sealed class TenantScope
    {
        public int MinimumAge { get; set; }
    }
}
