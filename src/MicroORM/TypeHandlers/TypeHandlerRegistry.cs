using MicroORM.Materialization;

namespace MicroORM.TypeHandlers;

internal sealed class TypeHandlerRegistry
{
    private readonly IReadOnlyDictionary<Type, ITypeHandler> _handlers;

    public TypeHandlerRegistry(IReadOnlyDictionary<Type, ITypeHandler> handlers) => _handlers = handlers;

    public bool IsEmpty => _handlers.Count == 0;

    public bool TryGet(Type? type, out ITypeHandler handler)
    {
        handler = null!;
        if (type is null || _handlers.Count == 0)
        {
            return false;
        }

        return _handlers.TryGetValue(type, out handler!)
            || (Nullable.GetUnderlyingType(type) is { } underlying && _handlers.TryGetValue(underlying, out handler!));
    }

    public bool IsScalar(Type type) => ScalarTypes.IsScalar(type) || TryGet(type, out _);

    public T Convert<T>(object? value) => TryGet(typeof(T), out var handler)
        ? ReadWith<T>(handler, value)
        : ValueConverter.To<T>(value);

    public object? Convert(object? value, Type target) => TryGet(target, out var handler)
        ? (value is null or DBNull ? ValueConverter.Convert(null, target) : handler.Read(value))
        : ValueConverter.Convert(value, target);

    public static T ReadWith<T>(ITypeHandler handler, object? value) =>
        value is null or DBNull ? default! : (T)handler.Read(value)!;
}
