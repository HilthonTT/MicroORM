using MicroORM.Dialects;
using MicroORM.TypeHandlers;
using System.Linq.Expressions;
using System.Reflection;

namespace MicroORM.Mapping;

/// <summary>How a property maps to a column.</summary>
public sealed class ColumnMap
{
    private readonly Func<object, object?> _getter;

    internal ColumnMap(PropertyInfo property, string name, bool isKey, bool isVersion, ValueGeneration generation)
    {
        Property = property;
        Name = name;
        IsKey = isKey;
        IsVersion = isVersion;
        Generation = generation;
        _getter = CompileGetter(property);
    }

    /// <summary>Gets the mapped property.</summary>
    public PropertyInfo Property { get; }

    /// <summary>Gets the column name.</summary>
    public string Name { get; }

    /// <summary>Gets whether this column is part of the primary key.</summary>
    public bool IsKey { get; }

    /// <summary>Gets whether this column is the optimistic concurrency version.</summary>
    public bool IsVersion { get; }

    /// <summary>Gets how the database generates this column's value.</summary>
    public ValueGeneration Generation { get; }

    /// <summary>Gets whether the column is written by <c>INSERT</c>.</summary>
    public bool IsInsertable => Generation == ValueGeneration.None;

    /// <summary>Gets whether the column is written from the entity by <c>UPDATE</c> (the version is incremented instead).</summary>
    public bool IsUpdatable => Generation == ValueGeneration.None && !IsKey && !IsVersion;

    /// <summary>Reads the property value from an entity.</summary>
    public object? GetValue(object entity) => _getter(entity);

    internal void SetValue(object entity, object? value, TypeHandlerRegistry handlers) =>
        Property.SetValue(entity, handlers.Convert(value, Property.PropertyType));

    internal ColumnValue ToColumnValue(object entity) => new(Name, GetValue(entity), Property.PropertyType);

    private static Func<object, object?> CompileGetter(PropertyInfo property)
    {
        var instance = Expression.Parameter(typeof(object), "entity");
        var body = Expression.Convert(
            Expression.Property(Expression.Convert(instance, property.DeclaringType!), property),
            typeof(object));
        return Expression.Lambda<Func<object, object?>>(body, instance).Compile();
    }
}
