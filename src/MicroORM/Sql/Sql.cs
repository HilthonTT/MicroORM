using MicroORM.Dialects;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;

namespace MicroORM.Sql;

/// <summary>
/// A composable, injection-safe SQL fragment.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Sql"/> is an interpolated string handler: every hole in <c>$"..."</c> becomes a
/// database parameter instead of being spliced into the text. Plain strings do not convert to
/// <see cref="Sql"/>, so building SQL by string concatenation is a compile error rather than a
/// security bug.
/// </para>
/// <code>
/// var minAge = 18;
/// var ids = new[] { 1, 2, 3 };
/// await db.QueryAsync&lt;User&gt;($"SELECT * FROM users WHERE age &gt;= {minAge} AND id IN {ids}");
/// // SELECT * FROM users WHERE age &gt;= @p0 AND id IN (@p1, @p2, @p3)
/// </code>
/// <para>
/// Holes of type <see cref="Sql"/> are inlined as fragments, so queries compose without losing
/// parameterization. Collections (other than <see cref="string"/> and <c>byte[]</c>) expand to a
/// parenthesized parameter list for use with <c>IN</c>.
/// </para>
/// </remarks>
[InterpolatedStringHandler]
public sealed class Sql
{
    private readonly List<object> _parts;

    /// <summary>Creates a handler for an interpolated string. Called by the compiler.</summary>
    public Sql(int literalLength, int formattedCount)
    {
        _parts = new List<object>((formattedCount * 2) + 1);
    }

    private Sql()
    {
        _parts = [];
    }

    /// <summary>Gets a new, empty fragment.</summary>
    public static Sql Empty => new();

    /// <summary>Gets whether this fragment contains no SQL at all.</summary>
    public bool IsEmpty => _parts.Count == 0;

    /// <summary>Appends literal SQL text. Called by the compiler.</summary>
    public void AppendLiteral(string literal)
    {
        if (literal.Length > 0)
        {
            _parts.Add(literal);
        }
    }

    /// <summary>Appends a nested fragment verbatim (its own parameters are preserved).</summary>
    public void AppendFormatted(Sql? fragment)
    {
        if (fragment is not null)
        {
            _parts.Add(fragment);
        }
    }

    /// <summary>Appends a value as a parameter, or a collection as a parameter list. Called by the compiler.</summary>
    public void AppendFormatted<T>(T value) => AddValue(value, typeof(T));

    /// <summary>
    /// Creates a fragment from trusted SQL text. Never pass user input to this method.
    /// </summary>
    public static Sql Raw(string text)
    {
        ArgumentNullException.ThrowIfNull(text, nameof(text));
        var sql = new Sql();
        sql.AppendLiteral(text);
        return sql;
    }

    /// <summary>
    /// Creates a fragment holding exactly one parameter, even for a collection. Use it for values a
    /// type handler stores in one column, such as a list kept as JSON.
    /// </summary>
    public static Sql Param(object? value)
    {
        var sql = new Sql();
        sql._parts.Add(new ParameterPart(value, value?.GetType() ?? typeof(object)));
        return sql;
    }

    /// <summary>
    /// Creates a fragment holding an identifier (table or column name), quoted for the target
    /// dialect when the SQL is rendered. Dotted names such as <c>dbo.users</c> are quoted per part.
    /// </summary>
    public static Sql Identifier(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        var sql = new Sql();
        sql._parts.Add(new IdentifierPart(name));
        return sql;
    }

    /// <summary>Joins fragments with a literal separator, skipping empty fragments.</summary>
    public static Sql Join(string separator, IEnumerable<Sql> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments, nameof(fragments));
        var sql = new Sql();
        bool first = true;

        foreach (Sql fragment in fragments)
        {
            if (fragment.IsEmpty)
            {
                continue;
            }

            if (!first)
            {
                sql.AppendLiteral(separator);
            }
            sql.Append(fragment);
            first = false;
        }

        return sql;
    }

    /// <summary>Concatenates two fragments.</summary>
    public static Sql operator +(Sql left, Sql right)
    {
        ArgumentNullException.ThrowIfNull(left, nameof(left));
        ArgumentNullException.ThrowIfNull(right, nameof(right));
        var sql = new Sql();
        sql._parts.Add(left);
        sql._parts.Add(right);
        return sql;
    }

    /// <summary>Concatenates two fragments.</summary>
    public static Sql Add(Sql left, Sql right) => left + right;

    internal Sql Append(string literal)
    {
        AppendLiteral(literal);
        return this;
    }

    internal Sql Append(Sql fragment)
    {
        _parts.Add(fragment);
        return this;
    }

    internal Sql AppendIdentifier(string name)
    {
        _parts.Add(new IdentifierPart(name));
        return this;
    }

    internal Sql AppendParameter(object? value, Type? clrType)
    {
        _parts.Add(new ParameterPart(value, clrType));
        return this;
    }


    internal Sql AppendList(IEnumerable values)
    {
        _parts.Add(new ListPart(values));
        return this;
    }

    /// <summary>Renders the SQL text and parameters for a dialect.</summary>
    public RenderedSql Render(SqlDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect, nameof(dialect));

        var text = new StringBuilder();
        var parameters = new List<SqlParameterValue>();
        Render(dialect, text, parameters);

        return new RenderedSql(text.ToString(), parameters);
    }

    private void AddValue(object? value, Type declaredType)
    {
        switch (value)
        {
            case Sql fragment:
                _parts.Add(fragment);
                break;
            case IEnumerable values when value is not string and not byte[]:
                _parts.Add(new ListPart(values));
                break;
            default:
                _parts.Add(new ParameterPart(value, declaredType));
                break;
        }
    }

    private void Render(SqlDialect dialect, StringBuilder text, List<SqlParameterValue> parameters)
    {
        foreach (object part in _parts)
        {
            switch (part)
            {
                case string literal:
                    text.Append(literal);
                    break;
                case Sql nested:
                    nested.Render(dialect, text, parameters);
                    break;
                case IdentifierPart identifier:
                    text.Append(dialect.QuoteIdentifier(identifier.Name));
                    break;
                case ParameterPart parameter:
                    AppendParameter(dialect, text, parameters, parameter.Value, parameter.ClrType);
                    break;
                case ListPart list:
                    bool any = false;
                    foreach (object item in list.Values)
                    {
                        text.Append(any ? ", " : "(");
                        AppendParameter(dialect, text, parameters, item, item?.GetType());
                        any = true;
                    }

                    if (any)
                    {
                        text.Append(')');
                    }
                    else
                    {
                        // Not "(NULL)": "x NOT IN (NULL)" is unknown, so it would match nothing too.
                        var placeholder = new StringBuilder();
                        AppendParameter(dialect, placeholder, parameters, null, ElementType(list.Values));
                        text.Append(dialect.EmptyList(placeholder.ToString()));
                    }

                    break;
                default:
                    break;
            }
        }
    }

    private static void AppendParameter(
        SqlDialect dialect,
        StringBuilder text,
        List<SqlParameterValue> parameters,
        object? value,
        Type? clrType)
    {
        string name = dialect.ParameterName(parameters.Count);
        text.Append(name);
        parameters.Add(new SqlParameterValue(name, value, clrType));
    }

    /// <summary>The item type of a collection, such as <see cref="int"/> for <c>List&lt;int&gt;</c>.</summary>
    private static Type? ElementType(IEnumerable values) =>
        values.GetType().GetInterfaces().Append(values.GetType())
            .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];

    /// <summary>Renders the fragment for debugging, using SQLite syntax.</summary>
    public override string ToString() => Render(SqlDialect.Sqlite).Text;
}
