using MicroORM.Sql;
using System.Data.Common;
using System.Globalization;
using SQL = MicroORM.Sql.Sql;

namespace MicroORM.Dialects;

/// <summary>
/// Describes the SQL flavor of a database: identifier quoting, paging, identity retrieval, upsert
/// and a handful of functions. MicroORM never references provider packages; a dialect is all it needs.
/// </summary>
public abstract class SqlDialect
{
    /// <summary>SQLite (Microsoft.Data.Sqlite, System.Data.SQLite).</summary>
    public readonly static SqlDialect Sqlite = new SqliteDialect();

    /// <summary>PostgreSQL (Npgsql).</summary>
    public readonly static SqlDialect PostgreSql = new PostgreSqlDialect();

    /// <summary>Microsoft SQL Server and Azure SQL (Microsoft.Data.SqlClient).</summary>
    public readonly static SqlDialect SqlServer = new SqlServerDialect();

    /// <summary>MySQL and MariaDB (MySqlConnector, MySql.Data).</summary>
    public readonly static SqlDialect MySql = new MySqlDialect();

    /// <summary>Gets a display name for the dialect.</summary>
    public abstract string Name { get; }

    /// <summary>Quotes one part of an identifier, escaping quote characters inside it.</summary>
    protected abstract string QuotePart(string part);

    /// <summary>Quotes an identifier; dotted names (<c>schema.table</c>) are quoted per part.</summary>
    public string QuoteIdentifier(string name)
    {
        ArgumentNullException.ThrowIfNull(name, nameof(name));

        return name.Contains('.', StringComparison.Ordinal)
            ? string.Join('.', name.Split('.').Select(QuotePart))
            : QuotePart(name);
    }

    /// <summary>Gets the placeholder name for the parameter at <paramref name="index"/>.</summary>
    public virtual string ParameterName(int index) => "@p" + index.ToString(CultureInfo.InvariantCulture);

    /// <summary>Gives a provider parameter any type hints the dialect needs.</summary>
    public virtual void ConfigureParameter(DbParameter parameter, SqlParameterValue value)
    {
    }

    /// <summary>Gets whether the database has stored procedures.</summary>
    public virtual bool SupportsStoredProcedures => true;

    /// <summary>Gets the provider parameter name for a stored procedure argument.</summary>
    public virtual string ProcedureParameterName(string name) => "@" + name;

    /// <summary>Gets the SQL function returning a string's length in characters.</summary>
    public virtual string LengthFunction => "LENGTH";

    /// <summary>Gets the floating-point type used to average integers without truncation.</summary>
    public virtual string FloatType => "DOUBLE PRECISION";

    /// <summary>Concatenates two string expressions.</summary>
    public virtual SQL Concat(SQL left, SQL right) => SQL.Empty.Append("(").Append(left).Append(" || ").Append(right).Append(")");

    /// <summary>Appends a paging clause to a <c>SELECT</c> statement.</summary>
    /// <param name="sql">The statement to append to.</param>
    /// <param name="skip">Rows to skip, if any.</param>
    /// <param name="take">Maximum rows to return, if any.</param>
    /// <param name="hasOrderBy">Whether the statement already has an <c>ORDER BY</c> clause.</param>
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

    /// <summary>
    /// Builds an <c>INSERT</c> statement. When <paramref name="identityColumn"/> is set, the
    /// statement must produce the generated key as a scalar result.
    /// </summary>
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

    /// <summary>
    /// Builds an insert-or-update statement: insert <paramref name="values"/>, or, when a row with
    /// the same <paramref name="conflictColumns"/> exists, update <paramref name="updateColumns"/>.
    /// When <paramref name="identityColumn"/> is set, the statement must produce the row's key as a
    /// scalar result in both cases.
    /// </summary>
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

    /// <summary>Appends <c>(columns) VALUES (values)</c>, or the dialect's form for no columns.</summary>
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

    /// <summary>Formats an integer for inlining into SQL.</summary>
    protected static string Invariant(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override string ToString() => Name;
}
