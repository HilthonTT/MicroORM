using System.Reflection;

namespace MicroORM.Mapping;

/// <summary>
/// Turns CLR type and property names into table and column names when no explicit name is given.
/// </summary>
public abstract class NamingConvention
{
    /// <summary>Uses names exactly as declared: <c>UserId</c> stays <c>UserId</c>.</summary>
    public static readonly NamingConvention AsIs = new AsIsNamingConvention();

    /// <summary>Uses snake_case: <c>UserId</c> becomes <c>user_id</c>. Recommended for PostgreSQL.</summary>
    public static readonly NamingConvention SnakeCase = new SnakeCaseNamingConvention();

    /// <summary>Uses lowercase: <c>UserId</c> becomes <c>userid</c>.</summary>
    public static readonly NamingConvention LowerCase = new LowerCaseNamingConvention();

    /// <summary>Gets the table name for an entity type.</summary>
    public virtual string TableName(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType, nameof(entityType));
        return Convert(entityType.Name);
    }

    /// <summary>Gets the column name for a property.</summary>
    public virtual string ColumnName(PropertyInfo member)
    {
        ArgumentNullException.ThrowIfNull(member, nameof(member));
        return Convert(member.Name);
    }

    /// <summary>Converts a CLR identifier to a database identifier.</summary>
    public abstract string Convert(string name);
}
