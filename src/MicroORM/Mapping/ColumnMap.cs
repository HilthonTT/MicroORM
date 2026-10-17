using MicroORM.Dialects;
using MicroORM.TypeHandlers;
using System.Linq.Expressions;
using System.Reflection;

namespace MicroORM.Mapping;

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

    public PropertyInfo Property { get; }

    public string Name { get; }

    public bool IsKey { get; set; }

    public bool IsVersion { get; set; }

    public ValueGeneration Generation { get; set; }

    public bool IsInsertable => Generation == ValueGeneration.None;

    public bool IsUpdatable => Generation == ValueGeneration.None && !IsKey && !IsVersion;

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
