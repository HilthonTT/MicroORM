namespace MicroORM.Dialects;

internal sealed class MySqlDialect : SqlDialect
{
    public override string Name => "MySQL";

    public override string LengthFunction => "CHAR_LENGTH";

    public override string FloatType => "DOUBLE";

    public override int MaxParameters => 32000;

    protected override string QuotePart(string part) => "`" + part.Replace("`", "``", StringComparison.Ordinal) + "`";

    public override Sql.Sql Concat(Sql.Sql left, Sql.Sql right) =>
        Sql.Sql.Empty.Append("CONCAT(").Append(left).Append(", ").Append(right).Append(")");

    public override void AppendPaging(Sql.Sql sql, int? skip, int? take, bool hasOrderBy)
    {
        // MySQL requires LIMIT before OFFSET; this is its documented "all rows" value.
        if (skip is not null && take is null)
        {
            sql.Append(" LIMIT 18446744073709551615");
        }

        base.AppendPaging(sql, skip, take, hasOrderBy);
    }

    public override Sql.Sql Insert(string table, IReadOnlyList<ColumnValue> values, string? identityColumn)
    {
        var sql = Sql.Sql.Empty.Append("INSERT INTO ").AppendIdentifier(table);
        AppendInsertBody(sql, values);
        if (identityColumn is not null)
        {
            sql.Append("; SELECT LAST_INSERT_ID()");
        }

        return sql;
    }

    public override Sql.Sql Upsert(
        string table,
        IReadOnlyList<ColumnValue> values,
        IReadOnlyList<string> conflictColumns,
        IReadOnlyList<string> updateColumns,
        string? identityColumn)
    {
        // MySQL resolves conflicts on any unique key, so conflictColumns only serves the no-op assignment.
        // VALUES(col) is deprecated in MySQL 8 but is the form both MySQL and MariaDB accept.
        var assignments = updateColumns.Select(c => Sql.Sql.Identifier(c).Append(" = VALUES(").AppendIdentifier(c).Append(")")).ToList();
        if (identityColumn is not null)
        {
            // LAST_INSERT_ID(expr) makes the existing row's key what LAST_INSERT_ID() returns.
            assignments.Insert(0, Sql.Sql.Identifier(identityColumn).Append(" = LAST_INSERT_ID(").AppendIdentifier(identityColumn).Append(")"));
        }
        else if (assignments.Count == 0)
        {
            assignments.Add(Sql.Sql.Identifier(conflictColumns[0]).Append(" = ").AppendIdentifier(conflictColumns[0]));
        }

        var sql = Sql.Sql.Empty.Append("INSERT INTO ").AppendIdentifier(table);
        AppendInsertBody(sql, values);
        sql.Append(" ON DUPLICATE KEY UPDATE ").Append(Sql.Sql.Join(", ", assignments));
        if (identityColumn is not null)
        {
            sql.Append("; SELECT LAST_INSERT_ID()");
        }

        return sql;
    }

    public override Sql.Sql UpdateReturning(string table, Sql.Sql assignments, Sql.Sql? condition, IReadOnlyList<string> returning) =>
        throw new NotSupportedException("MySQL and MariaDB can't return the rows an UPDATE changed; run ExecuteUpdate, then query the rows.");

    protected override void AppendInsertBody(Sql.Sql sql, IReadOnlyList<ColumnValue> values)
    {
        if (values.Count == 0)
        {
            sql.Append(" () VALUES ()");
            return;
        }

        base.AppendInsertBody(sql, values);
    }
}
