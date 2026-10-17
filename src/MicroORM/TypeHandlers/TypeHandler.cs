using System.Data.Common;

namespace MicroORM.TypeHandlers;

public abstract class TypeHandler<T> : ITypeHandler
{
    Type ITypeHandler.Type => typeof(T);

    public void ConfigureParameter(DbParameter parameter)
    {
    }

    public abstract T Read(object value);

    public abstract object? Write(T value);

    object? ITypeHandler.Write(object value) => Write((T)value);

    object? ITypeHandler.Read(object value) => Read(value);
}
