using MicroORM.Configuration;
using MicroORM.Dialects;
using MicroORM.Mapping;
using MicroORM.Querying;
using MicroORM.Sessions;
using MicroORM.Tests.Model;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Linq.Expressions;

namespace MicroORM.Tests;

/// <summary>Checks, without a database, the SQL each dialect gets for the typed query API's newer operators and translations.</summary>
public class QueryApiTranslationTests
{
    private const string PersonColumns = "\"id\", \"name\", \"email\", \"age\", \"active\", \"mood\", \"created_at\", \"external_id\"";

    private static readonly DateTime Moment = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Table<Person> People(SqlDialect dialect) => Db(dialect).Table<Person>();

    private static MicroDb Db(SqlDialect dialect) => new(dialect, NoConnection, new MicroOptions { Naming = NamingConvention.SnakeCase });

    private static string Where(SqlDialect dialect, Expression<Func<Person, bool>> predicate) =>
        People(dialect).Where(predicate).ToSql().Render(dialect).Text.Split(" WHERE ", 2)[1];

    private static string Select<T>(SqlDialect dialect, Expression<Func<Person, T>> selector) =>
        People(dialect).Select(selector).ToSql().Render(dialect).Text.Split(" FROM ", 2)[0]["SELECT ".Length..];

    // ---- Conditionals --------------------------------------------------------------------------

    [Fact]
    public void Conditionals_become_case_expressions()
    {
        var sqlite = SqlDialect.Sqlite;
        Assert.Equal("(CASE WHEN (\"active\" = @p0) THEN \"age\" ELSE @p1 END > @p2)", Where(sqlite, p => (p.Active ? p.Age : 0) > 3));
        Assert.Equal("CASE WHEN (\"email\" IS NULL) THEN @p0 ELSE \"email\" END AS \"value\"", Select(sqlite, p => p.Email == null ? "none" : p.Email));

        // A bool result is 1 or 0, so SQL Server can use it, and PostgreSQL compare it with 1.
        Assert.Equal(
            "(CASE WHEN ([age] > @p0) THEN CASE WHEN ([active] = @p1) THEN 1 WHEN NOT ([active] = @p2) THEN 0 END " +
            "ELSE CASE WHEN ([email] IS NOT NULL) THEN 1 ELSE 0 END END = 1)",
            Where(SqlDialect.SqlServer, p => p.Age > 30 ? p.Active : p.Email != null));

        // A test that doesn't depend on the row picks its branch in .NET.
        var flag = false;
        Assert.Equal("\"age\" AS \"value\"", Select(sqlite, p => flag ? p.Id : p.Age));
    }

    // ---- Dates ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("SQLite", "(RTRIM(RTRIM(STRFTIME('%Y-%m-%d %H:%M:%f', \"created_at\", CAST(@p0 AS TEXT) || ' days'), '0'), '.') > @p1)")]
    [InlineData("PostgreSQL", "((\"created_at\" + (@p0) * INTERVAL '1 day') > @p1)")]
    [InlineData("SQL Server", "(DATEADD(day, @p0, [created_at]) > @p1)")]
    [InlineData("MySQL", "(DATE_ADD(`created_at`, INTERVAL @p0 DAY) > @p1)")]
    public void Adding_days(string dialectName, string expected)
    {
        var dialect = TestDialects.ByName(dialectName);
        Assert.Equal(expected, Where(dialect, p => p.CreatedAt.AddDays(-7) > Moment));

        // A whole amount is sent as an integer, which DATEADD needs.
        Assert.Equal(-7, People(dialect).Where(p => p.CreatedAt.AddDays(-7) > Moment).ToSql().Render(dialect).Parameters[0].Value);
    }

    [Theory]
    [InlineData("SQLite", "(RTRIM(RTRIM(STRFTIME('%Y-%m-%d %H:%M:%f', \"created_at\", CAST(\"age\" AS TEXT) || ' months', 'floor'), '0'), '.') > @p0)")]
    [InlineData("PostgreSQL", "((\"created_at\" + (\"age\") * INTERVAL '1 month') > @p0)")]
    [InlineData("SQL Server", "(DATEADD(month, [age], [created_at]) > @p0)")]
    [InlineData("MySQL", "(DATE_ADD(`created_at`, INTERVAL `age` MONTH) > @p0)")]
    public void Adding_months_clamps_to_the_month_end(string dialectName, string expected) =>
        Assert.Equal(expected, Where(TestDialects.ByName(dialectName), p => p.CreatedAt.AddMonths(p.Age) > Moment));

    [Theory]
    [InlineData("SQL Server")]
    [InlineData("MySQL")]
    public void Fractional_amounts_throw_where_the_database_rounds_them(string dialectName)
    {
        var dialect = TestDialects.ByName(dialectName);
        var error = Assert.Throws<NotSupportedException>(() => People(dialect).Where(p => p.CreatedAt.AddDays(1.5) > Moment));
        Assert.Contains("whole days", error.Message, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => People(dialect).Where(p => p.CreatedAt.AddHours(p.Age / 2.0) > Moment));
    }

    [Fact]
    public void Date_only_values_stay_dates()
    {
        Assert.Equal(
            "(DATE(\"day\", CAST(@p0 AS TEXT) || ' days') = @p1)",
            Db(SqlDialect.Sqlite).Table<Holiday>().Where(h => h.Day.AddDays(1) == new DateOnly(2026, 1, 2)).ToString().Split(" WHERE ")[1]);
        Assert.Equal(
            "(CAST((\"day\" + (@p0) * INTERVAL '1 year') AS DATE) = @p1)",
            Db(SqlDialect.PostgreSql).Table<Holiday>().Where(h => h.Day.AddYears(1) == new DateOnly(2027, 1, 1)).ToSql().Render(SqlDialect.PostgreSql).Text.Split(" WHERE ")[1]);
    }

    [Theory]
    [InlineData("SQLite", "RTRIM(RTRIM(STRFTIME('%Y-%m-%d %H:%M:%f', \"created_at\", 'start of day'), '0'), '.')", "((UNIXEPOCH(@p0, 'subsec') - UNIXEPOCH(\"created_at\", 'subsec')) / 86400.0)")]
    [InlineData("PostgreSQL", "DATE_TRUNC('day', \"created_at\")", "(EXTRACT(EPOCH FROM (@p0 - \"created_at\")) / 86400.0)")]
    [InlineData("SQL Server", "CAST(CAST([created_at] AS DATE) AS DATETIME2)", "((CAST(DATEDIFF_BIG(microsecond, [created_at], @p0) AS FLOAT) / 1000000) / 86400.0)")]
    [InlineData("MySQL", "CAST(DATE(`created_at`) AS DATETIME(6))", "((TIMESTAMPDIFF(MICROSECOND, `created_at`, @p0) / 1000000) / 86400.0)")]
    public void Start_of_day_and_date_differences(string dialectName, string date, string days)
    {
        var dialect = TestDialects.ByName(dialectName);
        Assert.Equal("(" + date + " = @p0)", Where(dialect, p => p.CreatedAt.Date == Moment));
        Assert.Equal("(" + days + " > @p1)", Where(dialect, p => (Moment - p.CreatedAt).TotalDays > 7));
    }

    [Fact]
    public void Date_arithmetic_without_a_translation_throws()
    {
        // A TimeSpan value adds its seconds; a difference of dates is only read through its totals.
        Assert.Equal("(DATEADD(second, @p0, [created_at]) > @p1)", Where(SqlDialect.SqlServer, p => p.CreatedAt + TimeSpan.FromHours(2) > Moment));
        Assert.Equal(7200, People(SqlDialect.SqlServer).Where(p => p.CreatedAt + TimeSpan.FromHours(2) > Moment).ToSql().Render(SqlDialect.SqlServer).Parameters[0].Value);
        Assert.Throws<NotSupportedException>(() => People(SqlDialect.Sqlite).Where(p => Moment - p.CreatedAt > TimeSpan.FromDays(1)));
        Assert.Throws<NotSupportedException>(() => People(SqlDialect.Sqlite).Where(p => (Moment - p.CreatedAt).Days > 1));
    }

    // ---- Strings -------------------------------------------------------------------------------

    [Theory]
    [InlineData("SQLite", "((INSTR(\"name\", @p0) - 1) = @p1)")]
    [InlineData("PostgreSQL", "((STRPOS(\"name\", @p0) - 1) = @p1)")]
    [InlineData("SQL Server", "((CHARINDEX(@p0, [name]) - 1) = @p1)")]
    [InlineData("MySQL", "((INSTR(`name`, @p0) - 1) = @p1)")]
    public void Index_of_is_zero_based(string dialectName, string expected)
    {
        var dialect = TestDialects.ByName(dialectName);
        Assert.Equal(expected, Where(dialect, p => p.Name.IndexOf("a") == 1));

        // The empty string is at 0, as in C#; SQL Server's CHARINDEX would say it isn't there.
        Assert.Equal("(@p0 = @p1)", Where(dialect, p => p.Name.IndexOf("") == 0));
    }

    [Theory]
    [InlineData("SQLite", "CASE WHEN LENGTH(\"name\") >= @p0 THEN \"name\" ELSE SUBSTR(REPLACE(HEX(ZEROBLOB(@p1)), '00', @p2) || \"name\", -@p3) END AS \"value\"")]
    [InlineData("PostgreSQL", "CASE WHEN LENGTH(\"name\") >= @p0 THEN \"name\" ELSE LPAD(\"name\", @p1, @p2) END AS \"value\"")]
    [InlineData("SQL Server", "CASE WHEN (LEN([name] + N'x') - 1) >= @p0 THEN [name] ELSE RIGHT(REPLICATE(@p1, @p2) + [name], @p3) END AS [value]")]
    [InlineData("MySQL", "CASE WHEN CHAR_LENGTH(`name`) >= @p0 THEN `name` ELSE LPAD(`name`, @p1, @p2) END AS `value`")]
    public void Padding_never_truncates(string dialectName, string expected)
    {
        var dialect = TestDialects.ByName(dialectName);
        var select = People(dialect).Select(p => p.Name.PadLeft(6, '*')).ToSql().Render(dialect);
        Assert.Equal(expected, select.Text.Split(" FROM ", 2)[0]["SELECT ".Length..]);
        Assert.Equal("*", select.Parameters[dialectName == "SQL Server" ? 1 : 2].Value);
    }

    [Fact]
    public void Compare_orders_null_first_like_csharp()
    {
        Assert.Equal(
            "(CASE WHEN \"name\" = @p0 THEN 0 WHEN \"name\" < @p1 THEN -1 ELSE 1 END < @p2)",
            Where(SqlDialect.Sqlite, p => string.Compare(p.Name, "C") < 0));
        Assert.Equal(
            "(CASE WHEN \"email\" IS NULL AND @p0 IS NULL THEN 0 WHEN \"email\" IS NULL THEN -1 WHEN @p1 IS NULL THEN 1 " +
            "WHEN \"email\" = @p2 THEN 0 WHEN \"email\" < @p3 THEN -1 ELSE 1 END > @p4)",
            Where(SqlDialect.Sqlite, p => string.Compare(p.Email, "b") > 0));
    }

    [Theory]
    [InlineData("SQLite", "(((\"name\" || @p0) || CAST(\"age\" AS TEXT)) || @p1) AS \"value\"")]
    [InlineData("SQL Server", "CONCAT(CONCAT(CONCAT([name], @p0), CAST([age] AS NVARCHAR(MAX))), @p1) AS [value]")]
    [InlineData("MySQL", "CONCAT(CONCAT(CONCAT(`name`, @p0), CAST(`age` AS CHAR)), @p1) AS `value`")]
    public void Interpolated_strings_concatenate(string dialectName, string expected)
    {
        var dialect = TestDialects.ByName(dialectName);
        var select = People(dialect).Select(p => $"{p.Name} ({p.Age})").ToSql().Render(dialect);
        Assert.Equal(expected, select.Text.Split(" FROM ", 2)[0]["SELECT ".Length..]);
        Assert.Equal([" (", ")"], select.Parameters.Select(p => p.Value));
    }

    [Fact]
    public void Concatenations_treat_null_as_empty_and_reject_culture_formatting()
    {
        Assert.Equal(
            "((\"name\" || @p0) || COALESCE(\"email\", '')) AS \"value\"",
            Select(SqlDialect.Sqlite, p => string.Concat(p.Name, ":", p.Email)));
        Assert.Equal("CAST(\"age\" AS TEXT) AS \"value\"", Select(SqlDialect.Sqlite, p => p.Age.ToString()));

        // Fractions and dates format by culture in .NET, differently in SQL.
        Assert.Throws<NotSupportedException>(() => People(SqlDialect.Sqlite).Select(p => $"{p.Name} {p.CreatedAt}"));
        Assert.Throws<NotSupportedException>(() => People(SqlDialect.Sqlite).Select(p => $"{p.Age:N2}"));
        Assert.Throws<NotSupportedException>(() => People(SqlDialect.Sqlite).Select(p => $"{p.Age,5}"));
        Assert.Throws<NotSupportedException>(() => People(SqlDialect.Sqlite).Select(p => p.CreatedAt.ToString()));
    }

    // ---- Math and casts ------------------------------------------------------------------------

    [Theory]
    [InlineData("SQLite", "(CAST((\"age\" / @p0) AS INTEGER) = @p1)", "REAL")]
    [InlineData("PostgreSQL", "(CAST(TRUNC((\"age\" / @p0)) AS BIGINT) = @p1)", "DOUBLE PRECISION")]
    [InlineData("SQL Server", "(CAST(([age] / @p0) AS BIGINT) = @p1)", "FLOAT")]
    [InlineData("MySQL", "(CAST(TRUNCATE((`age` / @p0), 0) AS SIGNED) = @p1)", "DOUBLE")]
    public void Casts_truncate_and_powers_use_floats(string dialectName, string cast, string floatType)
    {
        var dialect = TestDialects.ByName(dialectName);
        Assert.Equal(cast, Where(dialect, p => (int)(p.Age / 7.0) == 5));

        var quote = dialect.QuoteIdentifier("age");
        Assert.Equal($"(POWER(CAST({quote} AS {floatType}), @p0) > SQRT({quote}))", Where(dialect, p => Math.Pow(p.Age, 0.5) > Math.Sqrt(p.Age)));
    }

    [Fact]
    public void Min_and_max_of_two_values()
    {
        Assert.Equal(
            "(CASE WHEN \"age\" >= @p0 THEN \"age\" ELSE @p1 END + CASE WHEN \"age\" <= @p2 THEN \"age\" ELSE @p3 END) AS \"value\"",
            Select(SqlDialect.Sqlite, p => Math.Max(p.Age, 30) + Math.Min(p.Age, 10)));
    }

    // ---- Terminal operators --------------------------------------------------------------------

    [Fact]
    public void All_looks_for_a_row_that_fails()
    {
        var people = People(SqlDialect.Sqlite);
        var orders = Db(SqlDialect.Sqlite).Table<Order>();
        Assert.Equal(
            "NOT EXISTS (SELECT 1 FROM \"micro_orders\" AS \"s0\" WHERE NOT (((\"s0\".\"person_id\" <> \"micro_people\".\"id\" OR \"s0\".\"person_id\" IS NULL) OR (\"s0\".\"amount\" > @p0))))",
            Where(SqlDialect.Sqlite, p => orders.All(o => o.PersonId != p.Id || o.Amount > 10)));

        // The unknown of a NULL counts as failing, as false does in C#.
        Assert.Equal(
            "NOT EXISTS (SELECT 1 FROM \"micro_orders\" AS \"s0\" WHERE (CASE WHEN (\"s0\".\"person_id\" > @p0) THEN 0 ELSE 1 END = 1))",
            Where(SqlDialect.Sqlite, p => orders.All(o => o.PersonId > 0)));
    }

    [Fact]
    public void Last_reverses_the_order_and_needs_one()
    {
        var people = People(SqlDialect.SqlServer);
        Assert.Throws<NotSupportedException>(() => people.Last());
        Assert.Throws<NotSupportedException>(() => people.Where(p => p.Active).LastOrDefault());
    }

    // ---- Distinct and set operations ------------------------------------------------------------

    [Fact]
    public void Distinct_reads_from_a_derived_table_that_composes()
    {
        Assert.Equal(
            $"SELECT {PersonColumns} FROM (SELECT DISTINCT {PersonColumns} FROM \"micro_people\") AS \"micro_people\" WHERE (\"age\" > @p0) ORDER BY \"name\" LIMIT 2",
            People(SqlDialect.Sqlite).Distinct().Where(p => p.Age > 3).OrderBy(p => p.Name).Take(2).ToString());
    }

    [Fact]
    public void Set_operations_page_each_side_in_a_derived_table()
    {
        var people = People(SqlDialect.SqlServer);
        var columns = string.Join(", ", PersonColumns.Split(", ").Select(c => "[" + c.Trim('"') + "]"));
        Assert.Equal(
            $"SELECT {columns} FROM (SELECT {columns} FROM [micro_people] WHERE ([age] > @p0) UNION " +
            $"SELECT {columns} FROM (SELECT {columns} FROM [micro_people] ORDER BY [age] OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY) AS [micro_people]) AS [micro_people] ORDER BY [name]",
            people.Where(p => p.Age > 40).Union(people.OrderBy(p => p.Age).Take(1)).OrderBy(p => p.Name).ToSql().Render(SqlDialect.SqlServer).Text);

        var sqlite = People(SqlDialect.Sqlite);
        Assert.Contains(" UNION ALL ", sqlite.Concat(sqlite).ToString(), StringComparison.Ordinal);
        Assert.Contains(" INTERSECT ", sqlite.Intersect(sqlite).ToString(), StringComparison.Ordinal);
        Assert.Contains(" EXCEPT ", sqlite.Except(sqlite).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Set_operations_combine_projections_of_any_table()
    {
        var db = Db(SqlDialect.Sqlite);
        Assert.Equal(
            "SELECT * FROM (SELECT * FROM (SELECT \"id\" AS \"value\" FROM \"micro_people\") AS \"operand\" UNION " +
            "SELECT * FROM (SELECT \"person_id\" AS \"value\" FROM \"micro_orders\" ORDER BY \"amount\" LIMIT 3) AS \"operand\") AS \"combined\"",
            db.Table<Person>().Select(p => (int?)p.Id).Union(db.Table<Order>().OrderBy(o => o.Amount).Take(3).Select(o => o.PersonId)).ToString());
    }

    [Fact]
    public void Set_operations_reject_mismatched_queries_and_writes()
    {
        var sqlite = People(SqlDialect.Sqlite);
        Assert.Throws<NotSupportedException>(() => sqlite.Union(People(SqlDialect.PostgreSql)));

        var plain = new MicroDb(SqlDialect.Sqlite, NoConnection).Table<Person>();
        Assert.Throws<NotSupportedException>(() => sqlite.Union(plain));

        Assert.Throws<NotSupportedException>(() => sqlite.Distinct().ExecuteDelete());
        Assert.Throws<NotSupportedException>(() => sqlite.Union(sqlite).ExecuteUpdate(s => s.Set(p => p.Age, 1)));
    }

    // ---- Writes and locks ------------------------------------------------------------------------

    [Theory]
    [InlineData("SQLite", "DELETE FROM \"micro_people\" WHERE age > @p0 RETURNING \"id\", \"name\"")]
    [InlineData("PostgreSQL", "DELETE FROM \"micro_people\" WHERE age > @p0 RETURNING \"id\", \"name\"")]
    [InlineData("SQL Server", "DELETE FROM [micro_people] OUTPUT DELETED.[id], DELETED.[name] WHERE age > @p0")]
    public void Delete_returning(string dialectName, string expected)
    {
        var dialect = TestDialects.ByName(dialectName);
        MicroORM.Sql.Sql condition = $"age > {3}";
        Assert.Equal(expected, dialect.DeleteReturning("micro_people", condition, ["id", "name"]).Render(dialect).Text);
        Assert.Throws<NotSupportedException>(() => People(SqlDialect.MySql).ExecuteDeleteReturning());
    }

    [Theory]
    [InlineData("PostgreSQL", "SELECT \"name\" AS \"value\" FROM \"micro_people\" WHERE (\"age\" > @p0) ORDER BY \"id\" LIMIT 1 FOR UPDATE")]
    [InlineData("SQL Server", "SELECT [name] AS [value] FROM [micro_people] WITH (UPDLOCK, ROWLOCK) WHERE ([age] > @p0) ORDER BY [id] OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY")]
    [InlineData("MySQL", "SELECT `name` AS `value` FROM `micro_people` WHERE (`age` > @p0) ORDER BY `id` LIMIT 1 FOR UPDATE")]
    public void For_update_locks_the_rows_read(string dialectName, string expected)
    {
        var dialect = TestDialects.ByName(dialectName);
        var locked = People(dialect).Where(p => p.Age > 3).OrderBy(p => p.Id).Take(1).ForUpdate();
        Assert.Equal(expected, locked.Select(p => p.Name).ToSql().Render(dialect).Text);

        // Anything that would read the rows without the lock throws instead.
        Assert.Throws<NotSupportedException>(() => locked.Count());
        Assert.Throws<NotSupportedException>(() => locked.Any());
        Assert.Throws<NotSupportedException>(() => locked.Distinct());
        Assert.Throws<NotSupportedException>(() => locked.GroupBy(p => p.Age));
        Assert.Throws<NotSupportedException>(() => locked.Union(People(dialect)));
        Assert.Throws<NotSupportedException>(() => locked.Select(p => p.Name).Distinct().ToSql());
        Assert.Throws<NotSupportedException>(() => People(dialect).Where(p => locked.Any(q => q.Id == p.Id)));

        // A derived table's rows can't be locked either (SQL Server would drop the hint).
        Assert.Throws<NotSupportedException>(() => People(dialect).Distinct().ForUpdate());
        Assert.Throws<NotSupportedException>(() => People(dialect).Union(People(dialect)).ForUpdate());
    }

    [Fact]
    public void For_update_throws_on_sqlite() =>
        Assert.Throws<NotSupportedException>(() => People(SqlDialect.Sqlite).ForUpdate());

    private static DbConnection NoConnection() => throw new InvalidOperationException("No database in translation tests.");

    [Table("holidays")]
    private sealed class Holiday
    {
        public int Id { get; set; }

        public DateOnly Day { get; set; }
    }
}
