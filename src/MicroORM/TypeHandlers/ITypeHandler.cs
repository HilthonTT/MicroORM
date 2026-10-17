using System.Data.Common;

namespace MicroORM.TypeHandlers;

public interface ITypeHandler
{
    Type Type { get; }

    object? Write(object value);

    object? Read(object value);

    void ConfigureParameter(DbParameter parameter);
}
