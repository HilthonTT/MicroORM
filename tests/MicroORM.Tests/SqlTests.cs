using MicroORM.Dialects;
using MicroORM.Sql;

namespace MicroORM.Tests;

using Sql = MicroORM.Sql.Sql;

public class SqlTests
{
    private static RenderedSql Render(Sql sql, SqlDialect? dialect = null) => sql.Render(dialect ?? SqlDialect.Sqlite);

    [Fact]
    public void Holes_become_parameters_not_text()
    {
        var name = "Robert'); DROP TABLE students;--";
        var rendered = Render($"SELECT * FROM users WHERE name = {name}");

        Assert.Equal("SELECT * FROM users WHERE name = @p0", rendered.Text);
        Assert.Equal(name, Assert.Single(rendered.Parameters).Value);
    }

    [Fact]
    public void Interpolation_without_holes_still_produces_sql()
    {
        Assert.Equal("SELECT 1", Render($"SELECT 1").Text);
    }

    [Fact]
    public void Collections_expand_to_parameter_lists()
    {
        var ids = new List<int> { 4, 5, 6 };
        var rendered = Render($"SELECT * FROM t WHERE id IN {ids}");

        Assert.Equal("SELECT * FROM t WHERE id IN (@p0, @p1, @p2)", rendered.Text);
        Assert.Equal([4, 5, 6], rendered.Parameters.Select(p => p.Value));
    }

    [Fact]
    public void Empty_collections_match_nothing()
    {
        Assert.Equal("id IN (NULL)", Render($"id IN {Array.Empty<int>()}").Text);
    }

    [Fact]
    public void Param_keeps_a_collection_as_one_parameter()
    {
        var tags = new List<string> { "a", "b" };
        var rendered = Render($"tags = {Sql.Param(tags)}");

        Assert.Equal("tags = @p0", rendered.Text);
        Assert.Same(tags, Assert.Single(rendered.Parameters).Value);
    }

    [Fact]
    public void Strings_and_byte_arrays_are_single_parameters()
    {
        var rendered = Render($"{"abc"} {new byte[] { 1, 2 }}");
        Assert.Equal("@p0 @p1", rendered.Text);
    }

    [Fact]
    public void Nested_fragments_compose_and_number_parameters_globally()
    {
        Sql filter = $"age > {18}";
        var rendered = Render($"SELECT * FROM t WHERE name = {"x"} AND {filter}");

        Assert.Equal("SELECT * FROM t WHERE name = @p0 AND age > @p1", rendered.Text);
        Assert.Equal(["x", 18], rendered.Parameters.Select(p => p.Value));
    }

    [Fact]
    public void Join_skips_empty_fragments()
    {
        var joined = Sql.Join(" AND ", [$"a = {1}", Sql.Empty, $"b = {2}"]);
        Assert.Equal("a = @p0 AND b = @p1", Render(joined).Text);
    }

    [Fact]
    public void Plus_operator_concatenates()
    {
        var sql = Sql.Raw("SELECT * FROM t") + $" WHERE id = {7}";
        Assert.Equal("SELECT * FROM t WHERE id = @p0", Render(sql).Text);
    }

    [Theory]
    [InlineData("SQLite", "\"dbo\".\"we\"\"ird\"")]
    [InlineData("PostgreSQL", "\"dbo\".\"we\"\"ird\"")]
    [InlineData("SQL Server", "[dbo].[we\"ird]")]
    [InlineData("MySQL", "`dbo`.`we\"ird`")]
    public void Identifiers_are_quoted_per_dialect(string dialect, string expected)
    {
        Assert.Equal(expected, Render(Sql.Identifier("dbo.we\"ird"), TestDialects.ByName(dialect)).Text);
    }

    [Fact]
    public void Identifier_quoting_escapes_closing_characters()
    {
        Assert.Equal("[a]]b]", SqlDialect.SqlServer.QuoteIdentifier("a]b"));
        Assert.Equal("`a``b`", SqlDialect.MySql.QuoteIdentifier("a`b"));
    }
}
