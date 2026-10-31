using MicroORM.Configuration;
using MicroORM.Dialects;
using MicroORM.Mapping;
using MicroORM.Procedures;
using MicroORM.Querying;
using MicroORM.Sessions;
using MicroORM.Tests.Model;
using System.Data.Common;
using System.Linq.Expressions;

namespace MicroORM.Tests;

using Sql = MicroORM.Sql.Sql;

/// <summary>Checks generated SQL without touching a database.</summary>
public class QueryTranslationTests
{
    private static readonly MicroDb Db = new(SqlDialect.Sqlite, NoConnection, new MicroOptions { Naming = NamingConvention.SnakeCase });

    private static Table<Person> People => Db.Table<Person>();

    private static string Where(Expression<Func<Person, bool>> predicate) =>
        People.Where(predicate).ToSql().Render(SqlDialect.Sqlite).Text.Split(" WHERE ")[1];

    [Fact]
    public void Select_lists_mapped_columns_only()
    {
        Assert.Equal(
            "SELECT \"id\", \"name\", \"email\", \"age\", \"active\", \"mood\", \"created_at\", \"external_id\" FROM \"micro_people\"",
            People.ToString());
    }

    [Fact]
    public void Comparisons_and_logic()
    {
        Assert.Equal("((\"age\" >= @p0) AND (\"name\" <> @p1))", Where(p => p.Age >= 18 && p.Name != "x"));
        Assert.Equal("((\"age\" < @p0) OR (\"age\" > @p1))", Where(p => p.Age < 1 || p.Age > 99));
    }

    [Fact]
    public void Bare_booleans_become_comparisons()
    {
        Assert.Equal("(\"active\" = @p0)", Where(p => p.Active));
        Assert.Equal("NOT ((\"active\" = @p0))", Where(p => !p.Active));
    }

    [Fact]
    public void Null_comparisons_use_is_null()
    {
        string? none = null;
        Assert.Equal("(\"email\" IS NULL)", Where(p => p.Email == null));
        Assert.Equal("(\"email\" IS NOT NULL)", Where(p => p.Email != none));
        Assert.Equal("(\"external_id\" IS NOT NULL)", Where(p => p.ExternalId.HasValue));
    }

    [Fact]
    public void Captured_values_become_parameters()
    {
        var minimum = 30;
        var query = People.Where(p => p.Age > minimum).ToSql().Render(SqlDialect.Sqlite);

        Assert.EndsWith("WHERE (\"age\" > @p0)", query.Text, StringComparison.Ordinal);
        Assert.Equal(30, Assert.Single(query.Parameters).Value);
    }

    [Fact]
    public void Enum_comparisons_keep_the_enum_type_for_type_handlers()
    {
        var mood = Mood.Grumpy;
        Assert.Equal(Mood.Grumpy, Assert.Single(People.Where(p => p.Mood == mood).ToSql().Render(SqlDialect.Sqlite).Parameters).Value);
        Assert.Equal(Mood.Happy, Assert.Single(People.Where(p => p.Mood != Mood.Happy).ToSql().Render(SqlDialect.Sqlite).Parameters).Value);
    }

    [Fact]
    public void String_methods_become_escaped_like_patterns()
    {
        var postgres = new MicroDb(SqlDialect.PostgreSql, NoConnection, new MicroOptions { Naming = NamingConvention.SnakeCase }).Table<Person>();
        var query = postgres.Where(p => p.Name.Contains("50%_off!")).ToSql().Render(SqlDialect.PostgreSql);

        Assert.EndsWith("(\"name\" LIKE @p0 ESCAPE '!')", query.Text, StringComparison.Ordinal);
        Assert.Equal("%50!%!_off!!%", query.Parameters[0].Value);
        Assert.Equal("%!_", postgres.Where(p => p.Name.EndsWith('_')).ToSql().Render(SqlDialect.PostgreSql).Parameters[0].Value);

        // SQLite's LIKE ignores case, unlike its =; GLOB doesn't, and brackets match its wildcards literally.
        Assert.Equal("(\"name\" GLOB @p0)", Where(p => p.Name.StartsWith("A")));
        Assert.Equal("[*]a[?][[]*", People.Where(p => p.Name.StartsWith("*a?[")).ToSql().Render(SqlDialect.Sqlite).Parameters[0].Value);
        Assert.Equal("*_", People.Where(p => p.Name.EndsWith('_')).ToSql().Render(SqlDialect.Sqlite).Parameters[0].Value);
        Assert.Equal("(UPPER(\"name\") = @p0)", Where(p => p.Name.ToUpper() == "ANN"));
        Assert.Equal("(LENGTH(\"name\") > @p0)", Where(p => p.Name.Length > 3));
    }

    [Fact]
    public void More_string_methods_translate()
    {
        Assert.Equal("(LTRIM(\"name\", @p0) = @p1)", Where(p => p.Name.TrimStart() == "x"));
        Assert.Equal("(RTRIM(\"name\", @p0) = @p1)", Where(p => p.Name.TrimEnd() == "x"));
        Assert.Equal("(REPLACE(\"name\", @p0, @p1) = @p2)", Where(p => p.Name.Replace('-', ' ') == "x"));
        Assert.Equal("(\"email\" IS NULL OR TRIM(\"email\", @p0) = '')", Where(p => string.IsNullOrWhiteSpace(p.Email)));
        Assert.Equal(" \t\n\r\v\f", People.Where(p => p.Name.Trim() == "x").ToSql().Render(SqlDialect.Sqlite).Parameters[0].Value);
        Assert.Equal("(SUBSTR(\"name\", (\"age\" + 1)) = @p0)", Where(p => p.Name.Substring(p.Age) == "x"));

        var query = People.Where(p => p.Name.Substring(1, 2) == "da").ToSql().Render(SqlDialect.Sqlite);
        Assert.EndsWith("(SUBSTR(\"name\", @p0, @p1) = @p2)", query.Text, StringComparison.Ordinal);
        Assert.Equal(2, query.Parameters[0].Value);

        var sqlServer = new MicroDb(SqlDialect.SqlServer, NoConnection).Table<Person>();
        Assert.Contains("(SUBSTRING([Name], @p0, DATALENGTH([Name])) = @p1)", sqlServer.Where(p => p.Name.Substring(1) == "x").ToString(), StringComparison.Ordinal);
        Assert.Equal(" ", People.Where(p => p.Name.Replace('-', ' ') == "x").ToSql().Render(SqlDialect.Sqlite).Parameters[1].Value);
    }

    [Fact]
    public void Date_parts_follow_the_dialect()
    {
        Assert.Equal("(CAST(strftime('%Y', \"created_at\") AS INTEGER) = @p0)", Where(p => p.CreatedAt.Year == 2026));
        Assert.Equal("(CAST(strftime('%M', \"created_at\") AS INTEGER) = @p0)", Where(p => p.CreatedAt.Minute == 30));

        static string On(SqlDialect dialect) =>
            new MicroDb(dialect, NoConnection).Table<Person>().Where(p => p.CreatedAt.Month == 10).ToSql().Render(dialect).Text.Split(" WHERE ")[1];

        Assert.Equal("(CAST(FLOOR(EXTRACT(MONTH FROM \"CreatedAt\")) AS INTEGER) = @p0)", On(SqlDialect.PostgreSql));
        Assert.Equal("(DATEPART(month, [CreatedAt]) = @p0)", On(SqlDialect.SqlServer));
        Assert.Equal("(EXTRACT(MONTH FROM `CreatedAt`) = @p0)", On(SqlDialect.MySql));
        Assert.Throws<NotSupportedException>(() => People.Where(p => p.CreatedAt.DayOfWeek == DayOfWeek.Monday));
    }

    [Fact]
    public void Math_functions_and_like_translate()
    {
        Assert.Equal("(ABS((\"age\" - @p0)) < @p1)", Where(p => Math.Abs(p.Age - 30) < 5));
        Assert.Equal("(FLOOR((\"age\" / @p0)) = @p1)", Where(p => Math.Floor(p.Age / 10.0) == 3));
        Assert.Equal("(CEILING((\"age\" / @p0)) = @p1)", Where(p => Math.Ceiling(p.Age / 10.0) == 4));
        Assert.Throws<NotSupportedException>(() => People.Where(p => Math.Round((double)p.Age) == 3));

        var query = People.Where(p => SqlFunctions.Like(p.Email, "%@example.___")).ToSql().Render(SqlDialect.Sqlite);
        Assert.EndsWith("(\"email\" LIKE @p0)", query.Text, StringComparison.Ordinal);
        Assert.Equal("%@example.___", query.Parameters[0].Value);
        Assert.Throws<InvalidOperationException>(() => SqlFunctions.Like("a", "a"));
    }

    [Fact]
    public void Collection_contains_becomes_in()
    {
        var ids = new[] { 1, 2, 3 };
        var list = new List<string> { "a", "b" };

        Assert.Equal("(\"id\" IN (@p0, @p1, @p2))", Where(p => ids.Contains(p.Id)));
        Assert.Equal("(\"name\" IN (@p0, @p1))", Where(p => list.Contains(p.Name)));
    }

    [Fact]
    public void Arithmetic_and_concatenation_follow_the_dialect()
    {
        Assert.Equal("((\"age\" + @p0) > @p1)", Where(p => p.Age + 1 > 10));

        var sqlServer = new MicroDb(SqlDialect.SqlServer, NoConnection).Table<Person>();
        var mySql = new MicroDb(SqlDialect.MySql, NoConnection).Table<Person>();
        Assert.Contains("(CONCAT([Name], @p0) = @p1)", sqlServer.Where(p => p.Name + "!" == "x!").ToString(), StringComparison.Ordinal);

        // SQL Server has no boolean values: a condition used as one becomes a CASE.
        Assert.Contains("ORDER BY CASE WHEN ([Email] IS NULL) THEN 1 ELSE 0 END", sqlServer.OrderBy(p => p.Email == null).ToString(), StringComparison.Ordinal);
        Assert.Contains("(CONCAT(`Name`, @p0) = @p1)", mySql.Where(p => p.Name + "!" == "x!").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Comparisons_keep_csharp_null_semantics()
    {
        // A nullable column differs from a value when it is NULL; a non-nullable one needs no check.
        Assert.Equal("(\"email\" <> @p0 OR \"email\" IS NULL)", Where(p => p.Email != "x"));
        Assert.Equal("(\"name\" <> @p0)", Where(p => p.Name != "x"));
        Assert.Equal("(\"external_id\" = \"external_id\" OR (\"external_id\" IS NULL AND \"external_id\" IS NULL))", Where(p => p.ExternalId == p.ExternalId));

        // NOT of a condition that can be unknown would drop the rows with a NULL.
        Assert.Equal("(CASE WHEN (\"email\" GLOB @p0) THEN 0 ELSE 1 END = 1)", Where(p => !p.Email!.StartsWith("a")));
        Assert.Equal("NOT ((\"active\" = @p0))", Where(p => !p.Active));
        Assert.Equal("NOT ((\"email\" <> @p0 OR \"email\" IS NULL))", Where(p => !(p.Email != "x")));
    }

    [Fact]
    public void Arithmetic_keeps_csharp_types_on_every_dialect()
    {
        Assert.Equal("((CAST(\"age\" AS REAL) / (\"id\" + @p0)) > @p1)", Where(p => (double)p.Age / (p.Id + 9) > 1));
        Assert.Equal("((\"age\" / @p0) = @p1)", Where(p => p.Age / 10.0 == 3));
        Assert.Equal("(((\"age\" + @p0) - @p1 * CAST((\"age\" + @p2) / @p3 AS INTEGER)) = @p4)", Where(p => (p.Age + 0.5) % 2 == 0.5));
        Assert.Equal("((COALESCE(\"email\", '') || \"name\") = @p0)", Where(p => p.Email + p.Name == "x"));

        var sqlServer = new MicroDb(SqlDialect.SqlServer, NoConnection).Table<Person>();
        var mySql = new MicroDb(SqlDialect.MySql, NoConnection).Table<Person>();
        var flag = true;
        Assert.Contains("(CASE WHEN ([Age] > @p0) THEN 1 ELSE 0 END = @p1)", sqlServer.Where(p => (p.Age > 18) == flag).ToString(), StringComparison.Ordinal);

        // The flag is compared as the CASE's integer: PostgreSQL has no integer = boolean operator.
        var compared = sqlServer.Where(p => (p.Age > 18) == flag).ToSql().Render(SqlDialect.PostgreSql).Parameters[1];
        Assert.Equal((1, typeof(int)), (compared.Value, compared.ClrType));
        Assert.Contains("((LEN([Name] + N'x') - 1) = @p0)", sqlServer.Where(p => p.Name.Length == 4).ToString(), StringComparison.Ordinal);
        Assert.Contains("(([Age] - @p0 * ROUND([Age] / @p1, 0, 1)) = @p2)", sqlServer.Where(p => p.Age % 2.5 == 1).ToString(), StringComparison.Ordinal);
        Assert.Contains("(TRIM(@p0 FROM [Name]) = @p1)", sqlServer.Where(p => p.Name.Trim() == "x").ToString(), StringComparison.Ordinal);
        Assert.Contains("((CAST([Age] AS DECIMAL(38, 18)) / [Id]) > @p0)", sqlServer.Where(p => (decimal)p.Age / p.Id > 1).ToString(), StringComparison.Ordinal);
        Assert.Contains("((`Age` DIV @p0) = @p1)", mySql.Where(p => p.Age / 10 == 3).ToString(), StringComparison.Ordinal);
        Assert.Contains("(CONCAT(`Name`, COALESCE(`Email`, '')) = @p0)", mySql.Where(p => p.Name + p.Email == "x").ToString(), StringComparison.Ordinal);
        Assert.Contains("(REGEXP_REPLACE(`Name`, '^[[:space:]]+', '') = @p0)", mySql.Where(p => p.Name.TrimStart() == "x").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Constant_conditions_fold()
    {
        var everyone = true;
        Assert.Equal("1 = 1", Where(p => everyone));
    }

    [Fact]
    public void Unsupported_expressions_throw_instead_of_running_client_side()
    {
        var error = Assert.Throws<NotSupportedException>(() => People.Where(p => p.DisplayName == "x"));
        Assert.Contains("not a mapped column", error.Message, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => People.Where(p => p.Name.PadLeft(3) == "x"));
    }

    [Fact]
    public void Ordering_and_paging()
    {
        var sql = People.Where(p => p.Active).OrderBy(p => p.Name).ThenByDescending(p => p.Age).Skip(20).Take(10).ToString();
        Assert.EndsWith("ORDER BY \"name\", \"age\" DESC LIMIT 10 OFFSET 20", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Skip_and_take_compose_in_call_order_like_linq()
    {
        Assert.EndsWith("LIMIT 5 OFFSET 5", People.Take(10).Skip(5).ToString(), StringComparison.Ordinal);
        Assert.EndsWith("LIMIT -1 OFFSET 5", People.Skip(2).Skip(3).ToString(), StringComparison.Ordinal);
        Assert.EndsWith("LIMIT 2 OFFSET 1", People.Skip(1).Take(5).Take(2).ToString(), StringComparison.Ordinal);
        Assert.EndsWith("LIMIT 5 OFFSET 1", People.Skip(1).Take(5).Take(10).ToString(), StringComparison.Ordinal);
        Assert.EndsWith("WHERE 1 = 0", People.Take(3).Skip(5).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Projections_alias_by_member_or_parameter_name()
    {
        Assert.Equal(
            "SELECT \"name\" AS \"Name\", (\"age\" + @p0) AS \"Next\" FROM \"micro_people\"",
            People.Select(p => new { p.Name, Next = p.Age + 1 }).ToString());
        Assert.Equal(
            "SELECT \"id\" AS \"Id\", \"name\" AS \"Name\", \"age\" AS \"Age\" FROM \"micro_people\"",
            People.Select(p => new PersonSummary(p.Id, p.Name, p.Age)).ToString());
        Assert.Equal(
            "SELECT UPPER(\"name\") AS \"Upper\" FROM \"micro_people\"",
            People.Select(p => new NameDto { Upper = p.Name.ToUpper() }).ToString());
        Assert.Equal("SELECT \"name\" AS \"value\" FROM \"micro_people\" WHERE (\"age\" > @p0)", People.Where(p => p.Age > 1).Select(p => p.Name).ToString());
    }

    [Fact]
    public void Projected_conditions_become_case_expressions()
    {
        Assert.Equal(
            "SELECT \"active\" AS \"Active\", CASE WHEN (\"age\" >= @p0) THEN 1 ELSE 0 END AS \"Adult\" FROM \"micro_people\"",
            People.Select(p => new { p.Active, Adult = p.Age >= 18 }).ToString());
        Assert.Throws<NotSupportedException>(() => People.Select(p => p));
    }

    [Theory]
    [InlineData("SQLite", null, 5, " LIMIT 5")]
    [InlineData("SQLite", 5, null, " LIMIT -1 OFFSET 5")]
    [InlineData("PostgreSQL", 5, null, " OFFSET 5")]
    [InlineData("MySQL", 5, null, " LIMIT 18446744073709551615 OFFSET 5")]
    [InlineData("SQL Server", 5, 10, " ORDER BY (SELECT NULL) OFFSET 5 ROWS FETCH NEXT 10 ROWS ONLY")]
    [InlineData("SQL Server", null, 10, " ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY")]
    public void Paging_per_dialect(string dialect, int? skip, int? take, string expected)
    {
        var sql = Sql.Empty;
        TestDialects.ByName(dialect).AppendPaging(sql, skip, take, hasOrderBy: false);
        Assert.Equal(expected, sql.Render(SqlDialect.Sqlite).Text);
    }

    [Theory]
    [InlineData("SQLite", "INSERT INTO \"t\" (\"name\") VALUES (@p0) RETURNING \"id\"")]
    [InlineData("PostgreSQL", "INSERT INTO \"t\" (\"name\") VALUES (@p0) RETURNING \"id\"")]
    [InlineData("SQL Server", "INSERT INTO [t] ([name]) VALUES (@p0); SELECT CAST(SCOPE_IDENTITY() AS bigint)")]
    [InlineData("MySQL", "INSERT INTO `t` (`name`) VALUES (@p0); SELECT LAST_INSERT_ID()")]
    public void Insert_returns_identity_per_dialect(string dialect, string expected)
    {
        var d = TestDialects.ByName(dialect);
        Assert.Equal(expected, d.Insert("t", [new ColumnValue("name", "x", typeof(string))], "id").Render(d).Text);
    }

    [Fact]
    public void Insert_without_columns_uses_default_values()
    {
        Assert.Equal("INSERT INTO \"t\" DEFAULT VALUES", SqlDialect.Sqlite.Insert("t", [], null).Render(SqlDialect.Sqlite).Text);
        Assert.Equal("INSERT INTO `t` () VALUES ()", SqlDialect.MySql.Insert("t", [], null).Render(SqlDialect.MySql).Text);
    }

    [Theory]
    [InlineData("SQLite", "INSERT INTO \"t\" (\"email\", \"name\") VALUES (@p0, @p1) ON CONFLICT (\"email\") DO UPDATE SET \"name\" = excluded.\"name\" RETURNING \"id\"")]
    [InlineData("PostgreSQL", "INSERT INTO \"t\" (\"email\", \"name\") VALUES (@p0, @p1) ON CONFLICT (\"email\") DO UPDATE SET \"name\" = excluded.\"name\" RETURNING \"id\"")]
    [InlineData("MySQL", "INSERT INTO `t` (`email`, `name`) VALUES (@p0, @p1) ON DUPLICATE KEY UPDATE `id` = LAST_INSERT_ID(`id`), `name` = VALUES(`name`); SELECT LAST_INSERT_ID()")]
    [InlineData("SQL Server", "MERGE INTO [t] WITH (HOLDLOCK) AS [target] USING (VALUES (@p0, @p1)) AS [source] ([email], [name]) ON [target].[email] = [source].[email] WHEN MATCHED THEN UPDATE SET [target].[name] = [source].[name] WHEN NOT MATCHED THEN INSERT ([email], [name]) VALUES ([source].[email], [source].[name]) OUTPUT INSERTED.[id];")]
    public void Upsert_per_dialect(string dialect, string expected)
    {
        var d = TestDialects.ByName(dialect);
        ColumnValue[] values = [new("email", "a@b.c", typeof(string)), new("name", "Ada", typeof(string))];
        Assert.Equal(expected, d.Upsert("t", values, ["email"], ["name"], "id").Render(d).Text);
    }

    [Fact]
    public void Upsert_without_updatable_columns_still_assigns_for_returning()
    {
        var sql = SqlDialect.PostgreSql.Upsert("t", [new("k", 1, typeof(int))], ["k"], [], null).Render(SqlDialect.PostgreSql).Text;
        Assert.EndsWith("ON CONFLICT (\"k\") DO UPDATE SET \"k\" = excluded.\"k\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Set_based_operations_reject_ordering()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => People.OrderBy(p => p.Id).ExecuteDeleteAsync());
    }

    [Fact]
    public void Sql_conditions_are_parenthesized_so_or_cannot_escape_other_filters()
    {
        var sql = People.Where(p => p.Age > 1).Where($"name = {"a"} OR 1 = 1").ToSql().Render(SqlDialect.Sqlite).Text;
        Assert.EndsWith("WHERE (\"age\" > @p0) AND (name = @p1 OR 1 = 1)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Closed_conditions_become_constant_predicates_not_parameters()
    {
        var limit = 3;
        int? maybe = 1;
        var ids = new[] { 5 };
        Assert.Equal("((\"active\" = @p0) AND 1 = 1)", Where(p => p.Active && limit > 2));
        Assert.Equal("((\"active\" = @p0) AND 1 = 1)", Where(p => p.Active && maybe.HasValue));
        Assert.Equal("((\"active\" = @p0) AND 1 = 0)", Where(p => p.Active && ids.Contains(4)));
    }

    [Fact]
    public void Bitwise_complement_is_not_translated_as_logical_not()
    {
        Assert.Throws<NotSupportedException>(() => Where(p => ~p.Age == 0));
    }

    [Fact]
    public void Contains_with_a_null_item_also_matches_null()
    {
        string?[] emails = ["a@b.c", null];
        Assert.Equal("((\"email\" IN (@p0)) OR \"email\" IS NULL)", Where(p => emails.Contains(p.Email)));
    }

    [Theory]
    [InlineData("SQLite")]
    [InlineData("SQL Server")]
    public void Take_zero_selects_nothing_without_paging(string dialect)
    {
        var d = TestDialects.ByName(dialect);
        var sql = new MicroDb(d, NoConnection).Table<Person>().OrderBy(p => p.Id).Take(0).ToSql().Render(d).Text;
        Assert.EndsWith("WHERE 1 = 0", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Limit_never_raises_a_smaller_take()
    {
        Assert.EndsWith("LIMIT 1", People.Take(1).Limit(2).ToString(), StringComparison.Ordinal);
        Assert.EndsWith("LIMIT 2", People.Take(10).Limit(2).ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("get_balance")]
    [InlineData("billing.get_balance")]
    [InlineData("\"odd name\".\"get\"\"balance\"")]
    public void Valid_procedure_names_reach_the_provider(string name)
    {
        var db = new MicroDb(SqlDialect.PostgreSql, NoConnection);
        Assert.Throws<InvalidOperationException>(() => db.ExecuteProcedure(name));
    }

    [Theory]
    [InlineData("get_balance(); DROP TABLE users; --")]
    [InlineData("get_balance --")]
    [InlineData("[x; DROP TABLE users]")]
    [InlineData("\"unterminated")]
    [InlineData("a..b")]
    public void Procedure_names_carrying_sql_are_rejected(string name)
    {
        var db = new MicroDb(SqlDialect.PostgreSql, NoConnection);
        Assert.Throws<ArgumentException>(() => db.ExecuteProcedure(name));
    }

    [Fact]
    public void Procedure_parameter_names_must_be_identifiers()
    {
        Assert.Throws<ArgumentException>(() => new ProcedureParameters().Add("a => 1); DROP TABLE users; --", 1));
        _ = new ProcedureParameters().Add("@customer_id", 1).Output<int>("total$2");
    }

    private static DbConnection NoConnection() => throw new InvalidOperationException("No database in translation tests.");
}
