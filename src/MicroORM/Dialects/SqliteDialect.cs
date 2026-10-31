namespace MicroORM.Dialects;

internal sealed class SqliteDialect : SqlDialect
{
    public override string Name => "SQLite";

    public override bool SupportsStoredProcedures => false;

    public override string FloatType => "REAL";

    // SQLite has no exact decimal type; NUMERIC would leave an integer an integer.
    public override string DecimalType => "REAL";

    // SQLite's % works on integers only, truncating floats first.
    public override Sql.Sql Modulo(Sql.Sql left, Sql.Sql right, bool integral) => integral
        ? base.Modulo(left, right, integral)
        : Sql.Sql.Empty.Append("(").Append(left).Append(" - ").Append(right).Append(" * CAST(").Append(left).Append(" / ").Append(right).Append(" AS INTEGER))");

    // LIKE ignores ASCII case in SQLite, while = doesn't; GLOB is case-sensitive.
    public override Sql.Sql StringMatch(Sql.Sql value, string text, bool prefix, bool suffix)
    {
        ArgumentNullException.ThrowIfNull(text);

        // GLOB has no escape character; a bracket expression matches a wildcard literally.
        var escaped = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            escaped.Append(c is '*' or '?' or '[' ? "[" + c + "]" : c.ToString());
        }

        var pattern = (prefix ? "*" : string.Empty) + escaped + (suffix ? "*" : string.Empty);
        return Sql.Sql.Empty.Append("(").Append(value).Append(" GLOB ").AppendParameter(pattern, typeof(string)).Append(")");
    }

    protected override string QuotePart(string part) => "\"" + part.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    public override Sql.Sql DatePart(DatePart part, Sql.Sql value)
    {
        var format = part switch
        {
            Dialects.DatePart.Year => "%Y",
            Dialects.DatePart.Month => "%m",
            Dialects.DatePart.Day => "%d",
            Dialects.DatePart.Hour => "%H",
            Dialects.DatePart.Minute => "%M",
            Dialects.DatePart.Second => "%S",
            _ => throw new ArgumentOutOfRangeException(nameof(part)),
        };
        return Sql.Sql.Empty.Append("CAST(strftime('" + format + "', ").Append(value).Append(") AS INTEGER)");
    }

    public override void AppendPaging(Sql.Sql sql, int? skip, int? take, bool hasOrderBy)
    {
        if (skip is not null && take is null)
        {
            sql.Append(" LIMIT -1");
        }

        base.AppendPaging(sql, skip, take, hasOrderBy);
    }
}
