using MicroORM.Sessions;

namespace MicroORM.Querying;

/// <summary>The rows of a set operation (<c>UNION</c>, <c>INTERSECT</c>...) over projections, which a projection selects from.</summary>
internal sealed class CombinedSource : IProjectionSource
{
    private readonly Sql.Sql _combined;

    public CombinedSource(MicroSession session, Sql.Sql combined)
    {
        Session = session;
        _combined = combined;
    }

    public MicroSession Session { get; }

    public Sql.Sql SelectSql(Sql.Sql selectList, bool distinct, int? limit, bool ordered)
    {
        var sql = Sql.Sql.Empty.Append(distinct ? "SELECT DISTINCT " : "SELECT ").Append(selectList)
            .Append(" FROM (").Append(_combined).Append(") AS ").AppendIdentifier("combined");
        if (limit is null)
        {
            return sql;
        }

        // SQL Server can't page a DISTINCT with its "ORDER BY (SELECT NULL)", so the distinct rows are paged outside.
        if (distinct)
        {
            sql = Sql.Sql.Empty.Append("SELECT * FROM (").Append(sql).Append(") AS ").AppendIdentifier("distinct_rows");
        }

        Session.Dialect.AppendPaging(sql, null, limit, hasOrderBy: false);
        return sql;
    }
}
