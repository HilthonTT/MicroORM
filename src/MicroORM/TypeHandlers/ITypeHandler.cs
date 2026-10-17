using System.Data.Common;

namespace MicroORM.TypeHandlers;

/// <summary>The non-generic view of a <see cref="TypeHandler{T}"/>.</summary>
public interface ITypeHandler
{
    /// <summary>Gets the handled type.</summary>
    Type Type { get; }

    /// <summary>Converts a value of the handled type to what is sent to the database.</summary>
    object? Write(object value);

    /// <summary>Converts a provider value back to the handled type.</summary>
    object? Read(object value);

    /// <summary>Sets provider-specific parameter details.</summary>
    void ConfigureParameter(DbParameter parameter);
}
