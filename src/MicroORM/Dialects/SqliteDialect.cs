namespace MicroORM.Dialects;

internal sealed class SqliteDialect : SqlDialect
{
    public override string Name => "SQLite";

    public override bool SupportsStoredProcedures => false;

    public override string FloatType => "REAL";

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
