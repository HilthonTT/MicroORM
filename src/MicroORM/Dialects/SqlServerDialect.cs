using MicroORM.Sql;
using System.Data;
using System.Data.Common;

namespace MicroORM.Dialects;

internal sealed class SqlServerDialect : SqlDialect
{
    public override string Name => "SQL Server";

    public override string LengthFunction => "LEN";

    // SQL Server allows 2100 parameters per command.
    public override int MaxParameters => 2000;

    public override string FloatType => "FLOAT";

    protected override string QuotePart(string part) => "[" + part.Replace("]", "]]", StringComparison.Ordinal) + "]";

    // Not "+": with a number on one side SQL Server adds, converting the text to a number.
    public override Sql.Sql Concat(Sql.Sql left, Sql.Sql right) =>
        Sql.Sql.Empty.Append("CONCAT(").Append(left).Append(", ").Append(right).Append(")");

    public override Sql.Sql DatePart(DatePart part, Sql.Sql value) =>
        Sql.Sql.Empty.Append("DATEPART(" + part.ToString().ToLowerInvariant() + ", ").Append(value).Append(")");

    // SUBSTRING requires a length; the size in bytes is never less than the length in characters.
    public override Sql.Sql Substring(Sql.Sql value, Sql.Sql start, Sql.Sql? length) =>
        Sql.Sql.Empty.Append("SUBSTRING(").Append(value).Append(", ").Append(start).Append(", ")
            .Append(length ?? Sql.Sql.Empty.Append("DATALENGTH(").Append(value).Append(")")).Append(")");

    public override void ConfigureParameter(DbParameter parameter, SqlParameterValue value)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(value);
        Type? type = Nullable.GetUnderlyingType(value.ClrType ?? typeof(object)) ?? value.ClrType;
        switch (value.Value)
        {
            case string s:
                // A fixed size keeps one cached plan per statement instead of one per string length.
                parameter.Size = s.Length <= 4000 ? 4000 : -1;
                break;
            case DateTime:
                parameter.DbType = DbType.DateTime2;
                break;
            case null when type == typeof(byte[]):
                parameter.DbType = DbType.Binary;
                break;
            case null when type == typeof(DateTime):
                parameter.DbType = DbType.DateTime2;
                break;
        }
    }

    public override void AppendPaging(Sql.Sql sql, int? skip, int? take, bool hasOrderBy)
    {
        ArgumentNullException.ThrowIfNull(sql);
        if (skip is null && take is null)
        {
            return;
        }

        // OFFSET/FETCH requires ORDER BY; ordering by a constant keeps the server's order.
        if (!hasOrderBy)
        {
            sql.Append(" ORDER BY (SELECT NULL)");
        }

        sql.Append(" OFFSET " + Invariant(skip ?? 0) + " ROWS");
        if (take is int t)
        {
            sql.Append(" FETCH NEXT " + Invariant(t) + " ROWS ONLY");
        }
    }

    public override Sql.Sql Insert(string table, IReadOnlyList<ColumnValue> values, string? identityColumn)
    {
        var sql = Sql.Sql.Empty.Append("INSERT INTO ").AppendIdentifier(table);
        AppendInsertBody(sql, values);

        // SCOPE_IDENTITY works with triggers, unlike OUTPUT without INTO.
        if (identityColumn is not null)
        {
            sql.Append("; SELECT CAST(SCOPE_IDENTITY() AS bigint)");
        }

        return sql;
    }

    // OUTPUT comes before WHERE. It fails on tables with triggers, which need OUTPUT ... INTO.
    public override Sql.Sql UpdateReturning(string table, Sql.Sql assignments, Sql.Sql? condition, IReadOnlyList<string> returning)
    {
        var sql = Sql.Sql.Empty.Append("UPDATE ").AppendIdentifier(table).Append(" SET ").Append(assignments)
            .Append(" OUTPUT ").Append(Sql.Sql.Join(", ", returning.Select(c => Sql.Sql.Raw("INSERTED.").AppendIdentifier(c))));
        return condition is null ? sql : sql.Append(" WHERE ").Append(condition);
    }

    public override Sql.Sql Upsert(
        string table,
        IReadOnlyList<ColumnValue> values,
        IReadOnlyList<string> conflictColumns,
        IReadOnlyList<string> updateColumns,
        string? identityColumn)
    {
        if (values.Count == 0)
        {
            throw new NotSupportedException("An upsert needs at least one column value.");
        }

        // MERGE with HOLDLOCK makes the existence check and the write atomic.
        IReadOnlyList<string> assigned = updateColumns.Count > 0 ? updateColumns : [conflictColumns[0]];
        var sql = Sql.Sql.Empty.Append("MERGE INTO ").AppendIdentifier(table).Append(" WITH (HOLDLOCK) AS [target] USING (VALUES (");
        for (var i = 0; i < values.Count; i++)
        {
            sql.Append(i == 0 ? string.Empty : ", ").AppendParameter(values[i].Value, values[i].ClrType);
        }

        var columns = values.Select(v => v.Column).ToList();
        sql.Append(")) AS [source] (").Append(Sql.Sql.Join(", ", columns.Select(Sql.Sql.Identifier))).Append(") ON ")
            .Append(Sql.Sql.Join(" AND ", conflictColumns.Select(c => Sql.Sql.Raw("[target].").AppendIdentifier(c).Append(" = [source].").AppendIdentifier(c))))
            .Append(" WHEN MATCHED THEN UPDATE SET ")
            .Append(Sql.Sql.Join(", ", assigned.Select(c => Sql.Sql.Raw("[target].").AppendIdentifier(c).Append(" = [source].").AppendIdentifier(c))))
            .Append(" WHEN NOT MATCHED THEN INSERT (").Append(Sql.Sql.Join(", ", columns.Select(Sql.Sql.Identifier)))
            .Append(") VALUES (").Append(Sql.Sql.Join(", ", columns.Select(c => Sql.Sql.Raw("[source].").AppendIdentifier(c)))).Append(")");
        if (identityColumn is not null)
        {
            sql.Append(" OUTPUT INSERTED.").AppendIdentifier(identityColumn);
        }

        return sql.Append(";");
    }
}
