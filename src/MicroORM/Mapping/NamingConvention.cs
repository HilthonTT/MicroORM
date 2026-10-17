using System.Reflection;

namespace MicroORM.Mapping;

public abstract class NamingConvention
{
    public static readonly NamingConvention AsIs = new AsIsNamingConvention();

    public static readonly NamingConvention SnakeCase = new SnakeCaseNamingConvention();

    public static readonly NamingConvention LowerCase = new LowerCaseNamingConvention();

    public virtual string TableName(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType, nameof(entityType));
        return Convert(entityType.Name);
    }

    public virtual string ColumnName(PropertyInfo member)
    {
        ArgumentNullException.ThrowIfNull(member, nameof(member));
        return Convert(member.Name);
    }

    public abstract string Convert(string name);
}
