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

    /// <summary>
    /// Gets the most parameters one command may carry; <c>InsertMany</c> sizes its batches to stay
    /// under it. The default suits any SQLite build (999).
    /// </summary>
    public virtual int MaxParameters => 999;

    /// <summary>Gets the SQL function returning a string's length in characters.</summary>
    public virtual string LengthFunction => "LENGTH";

    /// <summary>Gets the floating-point type used to average integers without truncation.</summary>
    public virtual string FloatType => "DOUBLE PRECISION";

    /// <summary>Gets the exact numeric type used to divide integers as decimals.</summary>
    public virtual string DecimalType => "NUMERIC";

    /// <summary>The characters C#'s <c>Trim()</c> removes that databases commonly store: space, tab, newlines, vertical tab and form feed.</summary>
    protected const string Whitespace = " \t\n\r\v\f";

    /// <summary>Concatenates two string expressions.</summary>
    public virtual SQL Concat(SQL left, SQL right) => SQL.Empty.Append("(").Append(left).Append(" || ").Append(right).Append(")");

    /// <summary>Gets a string's length in characters, trailing spaces included.</summary>
    public virtual SQL Length(SQL value) => SQL.Empty.Append(LengthFunction + "(").Append(value).Append(")");

    /// <summary>Divides; <paramref name="integral"/> means both sides are integers and the result truncates toward zero, as in C#.</summary>
    public virtual SQL Divide(SQL left, SQL right, bool integral) => SQL.Empty.Append("(").Append(left).Append(" / ").Append(right).Append(")");

    /// <summary>The remainder of a division, with the sign of <paramref name="left"/> as in C#.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <param name="integral">Whether both sides are integers.</param>
    public virtual SQL Modulo(SQL left, SQL right, bool integral) => integral
        ? SQL.Empty.Append("(").Append(left).Append(" % ").Append(right).Append(")")
        : SQL.Empty.Append("(").Append(left).Append(" - ").Append(right).Append(" * TRUNC(").Append(left).Append(" / ").Append(right).Append("))");

    /// <summary>Removes <see cref="Whitespace"/> from the start, the end or both ends of a string.</summary>
    public virtual SQL Trim(SQL value, bool leading, bool trailing) =>
        SQL.Empty.Append((leading && trailing ? "TRIM" : leading ? "LTRIM" : "RTRIM") + "(").Append(value).Append(", ").AppendParameter(Whitespace, typeof(string)).Append(")");

    /// <summary>
    /// Matches a string against <paramref name="text"/> case-sensitively where <c>=</c> is: with
    /// <paramref name="prefix"/>, anything may come before it; with <paramref name="suffix"/>, anything after.
    /// </summary>
    public virtual SQL StringMatch(SQL value, string text, bool prefix, bool suffix)
    {
        ArgumentNullException.ThrowIfNull(text);

        // '!' is the escape character because '\' means different things across dialects.
        var escaped = text.Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal)
            .Replace("[", "![", StringComparison.Ordinal);
        var pattern = (prefix ? "%" : string.Empty) + escaped + (suffix ? "%" : string.Empty);
        return SQL.Empty.Append("(").Append(value).Append(" LIKE ").AppendParameter(pattern, typeof(string)).Append(" ESCAPE '!')");
    }

    /// <summary>
    /// A subquery returning no rows, for an empty <c>IN</c> list: <c>IN</c> it is false and
    /// <c>NOT IN</c> true, where <c>IN (NULL)</c> would make both unknown.
    /// </summary>
    /// <param name="parameter">A <c>NULL</c> parameter typed as the list's items, so the column has a type.</param>
    public virtual string EmptyList(string parameter) => "(SELECT " + parameter + " WHERE 1 = 0)";

    /// <summary>Extracts a part of a date or time expression as an integer.</summary>
    public virtual SQL DatePart(DatePart part, SQL value) =>
        SQL.Empty.Append("EXTRACT(" + part.ToString().ToUpperInvariant() + " FROM ").Append(value).Append(")");

    /// <summary>Takes part of a string; <paramref name="start"/> is 1-based and <paramref name="length"/> is optional.</summary>
    public virtual SQL Substring(SQL value, SQL start, SQL? length)
    {
        var sql = SQL.Empty.Append("SUBSTR(").Append(value).Append(", ").Append(start);
        return (length is null ? sql : sql.Append(", ").Append(length)).Append(")");
    }

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

    /// <summary>Builds one <c>INSERT</c> of several rows, which all list the same, non-empty set of columns.</summary>
    public virtual SQL InsertRows(string table, IReadOnlyList<IReadOnlyList<ColumnValue>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows, nameof(rows));
        if (rows.Count == 0 || rows[0].Count == 0)
        {
            throw new ArgumentException("A multi-row insert needs at least one row and one column.", nameof(rows));
        }

        var sql = SQL.Empty.Append("INSERT INTO ").AppendIdentifier(table)
            .Append(" (").Append(SQL.Join(", ", rows[0].Select(v => SQL.Identifier(v.Column)))).Append(") VALUES ");
        for (var row = 0; row < rows.Count; row++)
        {
            sql.Append(row == 0 ? "(" : ", (");
            for (var i = 0; i < rows[row].Count; i++)
            {
                sql.Append(i == 0 ? string.Empty : ", ").AppendParameter(rows[row][i].Value, rows[row][i].ClrType);
            }

            sql.Append(")");
        }

        return sql;
    }

    /// <summary>
    /// Builds an <c>UPDATE</c> that also returns the <paramref name="returning"/> columns of every
    /// updated row, as a result set.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="assignments">The <c>SET</c> assignments.</param>
    /// <param name="condition">The <c>WHERE</c> condition, or <see langword="null"/> to update every row.</param>
    /// <param name="returning">The columns to return.</param>
    public virtual SQL UpdateReturning(string table, SQL assignments, SQL? condition, IReadOnlyList<string> returning)
    {
        ArgumentNullException.ThrowIfNull(assignments, nameof(assignments));
        ArgumentNullException.ThrowIfNull(returning, nameof(returning));

        var sql = SQL.Empty.Append("UPDATE ").AppendIdentifier(table).Append(" SET ").Append(assignments);
        if (condition is not null)
        {
            sql.Append(" WHERE ").Append(condition);
        }

        return sql.Append(" RETURNING ").Append(SQL.Join(", ", returning.Select(SQL.Identifier)));
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
