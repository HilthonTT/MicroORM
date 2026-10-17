using MicroORM.Sql;
using System.Data.Common;
using System.Globalization;
using SQL = MicroORM.Sql.Sql;

namespace MicroORM.Dialects;

public abstract class SqlDialect
{
    public readonly static SqlDialect Sqlite = new SqliteDialect();

    public readonly static SqlDialect PostgreSql = new PostgreSqlDialect();

    public readonly static SqlDialect SqlServer = new SqlServerDialect();

    public readonly static SqlDialect MySql = new MySqlDialect();

    public abstract string Name { get; }

    protected abstract string QuotePart(string part);

    public string QuoteIdentifier(string name)
    {
        ArgumentNullException.ThrowIfNull(name, nameof(name));

        return name.Contains('.', StringComparison.Ordinal)
            ? string.Join('.', name.Split('.').Select(QuotePart))
            : QuotePart(name);
    }

    public virtual string ParameterName(int index) => "@p" + index.ToString(CultureInfo.InvariantCulture);

    public virtual void ConfigureParameter(DbParameter parameter, SqlParameterValue value)
    {
    }

    public virtual bool SupportsStoredProcedures => true;

    public virtual string ProcedureParameterName(string name) => "@" + name;

    public virtual string LengthFunction => "LENGTH";

    public virtual string FloatType => "DOUBLE PRECISION";

    public virtual SQL Concat(SQL left, SQL right) => SQL.Empty.Append("(").Append(left).Append(" || ").Append(right).Append(")");

    public virtual void AppendPaging(SQL sql, int? skip, int? take, bool hasOrderBy)
    {
        ArgumentNullException.ThrowIfNull(sql, nameof(sql));

        if (take is int t)
        {
            sql.Append(" LIMIT " + Invariant(t));
        }

        if (skip is int s)
        {
            sql.Append(" OFFSET " + Invariant(s));
        }
    }

    public virtual SQL Insert(
        string table, 
        IReadOnlyList<ColumnValue> values, 
        string? identityColumn)
    {
        ArgumentNullException.ThrowIfNull(values, nameof(values));

        var sql = SQL.Empty.Append("INSERT INTO ").AppendIdentifier(table);

        AppendInsertBody(sql, values);
        if (identityColumn is not null)
        {
            sql.Append(" RETURNING ").AppendIdentifier(identityColumn);
        }

        return sql;
    }

    public virtual SQL Upsert(
        string table, 
        IReadOnlyList<ColumnValue> values, 
        IReadOnlyList<string> conflictColumns, 
        IReadOnlyList<string> updateColumns, 
        string? identityColumn)
    {
        ArgumentNullException.ThrowIfNull(values, nameof(values));
        ArgumentNullException.ThrowIfNull(conflictColumns, nameof(conflictColumns));
        ArgumentNullException.ThrowIfNull(updateColumns, nameof(updateColumns));

        // ON CONFLICT ... DO UPDATE always assigns something, so RETURNING yields the row on both paths.
        IReadOnlyList<string> assigned = updateColumns.Count > 0 ? updateColumns : [conflictColumns[0]];
        SQL sql = SQL.Empty.Append("INSERT INTO ").AppendIdentifier(table);
        AppendInsertBody(sql, values);

        sql.Append(" ON CONFLICT (").Append(SQL.Join(", ", conflictColumns.Select(SQL.Identifier))).Append(") DO UPDATE SET ")
            .Append(SQL.Join(", ", assigned.Select(c => SQL.Identifier(c).Append(" = excluded.").AppendIdentifier(c))));

        if (identityColumn is not null)
        {
            sql.Append(" RETURNING ").AppendIdentifier(identityColumn);
        }

        return sql;
    }

    protected virtual void AppendInsertBody(SQL sql, IReadOnlyList<ColumnValue> values)
    {
        ArgumentNullException.ThrowIfNull(sql, nameof(sql));
        ArgumentNullException.ThrowIfNull(values, nameof(values));

        if (values.Count == 0)
        {
            sql.Append(" DEFAULT VALUES");
            return;
        }

        sql.Append(" (").Append(SQL.Join(", ", values.Select(v => SQL.Identifier(v.Column)))).Append(") VALUES (");
        for (int i = 0; i < values.Count; i++)
        {
            sql.Append(i == 0 ? string.Empty : ", ").AppendParameter(values[i].Value, values[i].ClrType);
        }
        sql.Append(")");
    }

    protected static string Invariant(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public override string ToString() => Name;
}
