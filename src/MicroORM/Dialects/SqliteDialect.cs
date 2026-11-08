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

    // SQLite has no row locks: a write transaction locks the whole database.
    public override bool SupportsForUpdate => false;

    public override string ForUpdateClause => string.Empty;

    public override Sql.Sql ToInteger(Sql.Sql value) => Sql.Sql.Empty.Append("CAST(").Append(value).Append(" AS INTEGER)");

    // SQLite has no LPAD; hex(zeroblob(n)) is n "00" pairs, each replaced with the padding.
    public override Sql.Sql Pad(Sql.Sql value, Sql.Sql width, Sql.Sql padding, bool left)
    {
        var fill = Sql.Sql.Empty.Append("REPLACE(HEX(ZEROBLOB(").Append(width).Append(")), '00', ").Append(padding).Append(")");
        var padded = left
            ? Sql.Sql.Empty.Append("SUBSTR(").Append(fill).Append(" || ").Append(value).Append(", -").Append(width).Append(")")
            : Sql.Sql.Empty.Append("SUBSTR(").Append(value).Append(" || ").Append(fill).Append(", 1, ").Append(width).Append(")");
        return Sql.Sql.Empty.Append("CASE WHEN ").Append(Length(value)).Append(" >= ").Append(width).Append(" THEN ").Append(value)
            .Append(" ELSE ").Append(padded).Append(" END");
    }

    // Microsoft.Data.Sqlite stores a DateTime as "yyyy-MM-dd HH:mm:ss.FFFFFFF" and a DateOnly as
    // "yyyy-MM-dd"; results keep that format so they compare with stored values and parameters.
    // "floor" clamps Jan 31 + 1 month to Feb 28, as C# does, instead of rolling over to March.
    public override Sql.Sql DateAdd(DatePart unit, Sql.Sql value, Sql.Sql amount, bool wholeAmount, bool dateOnly)
    {
        var name = unit switch
        {
            Dialects.DatePart.Year => " years",
            Dialects.DatePart.Month => " months",
            Dialects.DatePart.Day => " days",
            Dialects.DatePart.Hour => " hours",
            Dialects.DatePart.Minute => " minutes",
            Dialects.DatePart.Second => " seconds",
            _ => throw new ArgumentOutOfRangeException(nameof(unit)),
        };
        var modifier = Sql.Sql.Empty.Append("CAST(").Append(amount).Append(" AS TEXT) || '" + name + "'");
        if (unit is Dialects.DatePart.Year or Dialects.DatePart.Month)
        {
            modifier.Append(", 'floor'");
        }

        return dateOnly
            ? Sql.Sql.Empty.Append("DATE(").Append(value).Append(", ").Append(modifier).Append(")")
            : DateTimeText(Sql.Sql.Empty.Append(value).Append(", ").Append(modifier));
    }

    public override Sql.Sql StartOfDay(Sql.Sql value) => DateTimeText(Sql.Sql.Empty.Append(value).Append(", 'start of day'"));

    public override Sql.Sql SecondsBetween(Sql.Sql earlier, Sql.Sql later) =>
        Sql.Sql.Empty.Append("(UNIXEPOCH(").Append(later).Append(", 'subsec') - UNIXEPOCH(").Append(earlier).Append(", 'subsec'))");

    public override void AppendPaging(Sql.Sql sql, int? skip, int? take, bool hasOrderBy)
    {
        if (skip is not null && take is null)
        {
            sql.Append(" LIMIT -1");
        }

        base.AppendPaging(sql, skip, take, hasOrderBy);
    }

    /// <summary>Formats a date and time like Microsoft.Data.Sqlite: fractional seconds only when there are any.</summary>
    private static Sql.Sql DateTimeText(Sql.Sql arguments) =>
        Sql.Sql.Empty.Append("RTRIM(RTRIM(STRFTIME('%Y-%m-%d %H:%M:%f', ").Append(arguments).Append("), '0'), '.')");
}
