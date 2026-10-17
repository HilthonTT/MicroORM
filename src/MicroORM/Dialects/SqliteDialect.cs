namespace MicroORM.Dialects;

internal sealed class SqliteDialect : SqlDialect
{
    public override string Name => "SQLite";

    public override bool SupportsStoredProcedures => false;

    public override string FloatType => "REAL";

    protected override string QuotePart(string part) => "\"" + part.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    public override void AppendPaging(Sql.Sql sql, int? skip, int? take, bool hasOrderBy)
    {
        if (skip is not null && take is null)
        {
            sql.Append(" LIMIT -1");
        }

        base.AppendPaging(sql, skip, take, hasOrderBy);
    }
}
