using MicroORM.TypeHandlers;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;

namespace MicroORM.Mapping;

internal sealed class MappingRegistry
{
    private readonly ConcurrentDictionary<Type, EntityMap> _maps = new();

    private readonly NamingConvention _naming;

    private readonly IReadOnlyDictionary<Type, EntityConfiguration> _configurations;

    private readonly TypeHandlerRegistry _handlers;

    public MappingRegistry(
        NamingConvention naming,
        IReadOnlyDictionary<Type, EntityConfiguration> configurations,
        TypeHandlerRegistry handlers)
    {
        _naming = naming;
        _configurations = configurations;
        _handlers = handlers;
    }

    public EntityMap GetMap(Type type) => _maps.GetOrAdd(type, Build);

    private EntityMap Build(Type type)
    {
        _configurations.TryGetValue(type, out var config);

        TableAttribute? tableAttribute = type.GetCustomAttribute<TableAttribute>();
        string table = config?.Table
            ?? (tableAttribute is null ? null : tableAttribute.Schema is null ? tableAttribute.Name : tableAttribute.Schema + "." + tableAttribute.Name)
            ?? _naming.TableName(type);

        List<PropertyInfo> properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && _handlers.IsScalar(p.PropertyType))
            .Where(p => p.GetCustomAttribute<NotMappedAttribute>() is null && config?.Ignored.Contains(p.Name) != true)
            .ToList();

        List<string> keyNames = FindKeys(type, properties, config);
        string? versionName = FindVersion(type, properties, config);

        List<ColumnMap> columns = properties.Select(p =>
        {
            var isKey = keyNames.Contains(p.Name);
            var name = config?.ColumnNames.GetValueOrDefault(p.Name)
                ?? p.GetCustomAttribute<ColumnAttribute>()?.Name
                ?? _naming.ColumnName(p);
            return new ColumnMap(p, name, isKey, p.Name == versionName, GenerationFor(p, isKey && keyNames.Count == 1, config));
        }).ToList();

        List<ColumnMap> keys = keyNames.Select(name => columns.First(c => c.Property.Name == name)).ToList();
        return new EntityMap(type, table, columns, keys, config?.QueryFilters.ToList() ?? []);
    }

    private static List<string> FindKeys(Type type, List<PropertyInfo> properties, EntityConfiguration? config)
    {
        if (config?.KeyProperties is { } configured)
        {
            return [.. configured];
        }

        // [Key] properties, ordered by [Column(Order = n)] and then by declaration. An unset Order is -1.
        List<string> annotated = properties
            .Select((p, index) => (Property: p, Index: index))
            .Where(x => x.Property.GetCustomAttribute<KeyAttribute>() is not null)
            .OrderBy(x => x.Property.GetCustomAttribute<ColumnAttribute>() is { Order: >= 0 } column ? column.Order : int.MaxValue)
            .ThenBy(x => x.Index)
            .Select(x => x.Property.Name)
            .ToList();
        if (annotated.Count > 0)
        {
            return annotated;
        }

        PropertyInfo? conventional = properties.FirstOrDefault(p => p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))
            ?? properties.FirstOrDefault(p => p.Name.Equals(type.Name + "Id", StringComparison.OrdinalIgnoreCase));
        return conventional is null ? [] : [conventional.Name];
    }

    private static string? FindVersion(Type type, List<PropertyInfo> properties, EntityConfiguration? config)
    {
        PropertyInfo? version = config?.VersionProperty is string configured
            ? properties.First(p => p.Name == configured)
            : properties.FirstOrDefault(p => p.GetCustomAttribute<ConcurrencyCheckAttribute>() is not null);
        if (version is null)
        {
            return null;
        }

        var versionType = Nullable.GetUnderlyingType(version.PropertyType) ?? version.PropertyType;
        return versionType == typeof(int) || versionType == typeof(long)
            ? version.Name
            : throw new NotSupportedException($"'{type.Name}.{version.Name}' is a concurrency version but is {version.PropertyType.Name}; MicroORM versions must be int or long.");
    }

    private static ValueGeneration GenerationFor(PropertyInfo property, bool isSingleKey, EntityConfiguration? config)
    {
        if (config?.Generation.TryGetValue(property.Name, out var configured) == true)
        {
            return configured;
        }

        if (property.GetCustomAttribute<DatabaseGeneratedAttribute>() is { } generated)
        {
            return generated.DatabaseGeneratedOption switch
            {
                DatabaseGeneratedOption.Identity => ValueGeneration.Identity,
                DatabaseGeneratedOption.Computed => ValueGeneration.Computed,
                _ => ValueGeneration.None,
            };
        }

        // SQL Server rowversion: the database writes it, MicroORM only reads it.
        if (property.GetCustomAttribute<TimestampAttribute>() is not null)
        {
            return ValueGeneration.Computed;
        }

        // Convention: a single integer key is database-generated; GUID, string and composite keys are not.
        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        return isSingleKey && (type == typeof(int) || type == typeof(long) || type == typeof(short))
            ? ValueGeneration.Identity
            : ValueGeneration.None;
    }
}
