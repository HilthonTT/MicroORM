using System.Collections;
using System.Runtime.CompilerServices;

namespace MicroORM.Mapping;

public sealed class EntityMap
{
    private readonly Dictionary<string, ColumnMap> _byProperty;

    internal EntityMap(Type entityType, string table, IReadOnlyList<ColumnMap> columns, IReadOnlyList<ColumnMap> keys)
    {
        EntityType = entityType;
        Table = table;
        Columns = columns;
        Keys = keys;
        Version = columns.FirstOrDefault(c => c.IsVersion);
        Identity = columns.FirstOrDefault(c => c.Generation == ValueGeneration.Identity);
        _byProperty = columns.ToDictionary(c => c.Property.Name, StringComparer.Ordinal);
    }

    public Type EntityType { get; }

    public string Table { get; }

    public IReadOnlyList<ColumnMap> Columns { get; }

    public IReadOnlyList<ColumnMap> Keys { get; }

    public ColumnMap? Version { get; }

    public ColumnMap? Identity { get; }

    public ColumnMap? FindByProperty(string propertyName) => _byProperty.GetValueOrDefault(propertyName);

    internal IReadOnlyList<ColumnMap> RequireKeys(string operation) => Keys.Count > 0 ? Keys : throw new InvalidOperationException(
        $"{operation} needs a primary key, but '{EntityType.Name}' has none. Mark properties with [Key], name one 'Id' or '{EntityType.Name}Id', or configure one with WeftOptions.Entity<{EntityType.Name}>(e => e.HasKey(...)).");

    internal object?[] KeyValues(object key, string operation)
    {
        var keys = RequireKeys(operation);
        if (keys.Count == 1)
        {
            return [key];
        }

        object?[] values = key switch
        {
            ITuple tuple => Enumerable.Range(0, tuple.Length).Select(i => tuple[i]).ToArray(),
            object[] array => array,
            IEnumerable sequence and not string => sequence.Cast<object?>().ToArray(),
            _ => [key],
        };

        return values.Length == keys.Count ? values : throw new ArgumentException(
            $"'{EntityType.Name}' has a composite key ({string.Join(", ", keys.Select(k => k.Property.Name))}); pass {keys.Count} values as a tuple, such as ({string.Join(", ", keys.Select(k => k.Property.Name.ToLowerInvariant()))}).",
            nameof(key));
    }
}
