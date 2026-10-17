using MicroORM.Materialization;

namespace MicroORM.TypeHandlers;

internal sealed class DelegateTypeHandler<T, TProvider>(Func<T, TProvider> toProvider, Func<TProvider, T> fromProvider) 
    : TypeHandler<T>
{
    public override object? Write(T value) => toProvider(value);

    public override T Read(object value) => fromProvider(ValueConverter.To<TProvider>(value));
}
