using System.Collections;
using System.Runtime.CompilerServices;

namespace MicroORM.Mapping;

/// <summary>How an entity type maps to a table.</summary>
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

    /// <summary>Gets the mapped CLR type.</summary>
    public Type EntityType { get; }

    /// <summary>Gets the table name, optionally schema-qualified (<c>schema.table</c>).</summary>
    public string Table { get; }

    /// <summary>Gets the mapped columns in declaration order.</summary>
    public IReadOnlyList<ColumnMap> Columns { get; }

    /// <summary>Gets the primary key columns in key order; empty when the entity has no key.</summary>
    public IReadOnlyList<ColumnMap> Keys { get; }

    /// <summary>Gets the optimistic concurrency version column, if any.</summary>
    public ColumnMap? Version { get; }

    /// <summary>Gets the database-generated identity column, if any.</summary>
    public ColumnMap? Identity { get; }

    /// <summary>Gets the column mapped from a property, or <see langword="null"/> when it isn't mapped.</summary>
    public ColumnMap? FindByProperty(string propertyName) => _byProperty.GetValueOrDefault(propertyName);

    internal IReadOnlyList<ColumnMap> RequireKeys(string operation) => Keys.Count > 0 ? Keys : throw new InvalidOperationException(
        $"{operation} needs a primary key, but '{EntityType.Name}' has none. Mark properties with [Key], name one 'Id' or '{EntityType.Name}Id', or configure one with MicroOptions.Entity<{EntityType.Name}>(e => e.HasKey(...)).");

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
