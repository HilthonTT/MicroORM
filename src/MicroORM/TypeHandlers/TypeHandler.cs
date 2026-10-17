using System.Data.Common;

namespace MicroORM.TypeHandlers;

/// <summary>
/// Converts a custom CLR type to and from a value the database provider understands. Register
/// handlers with <see cref="Configuration.MicroOptions.AddTypeHandler{T}(TypeHandler{T})"/>; a property of a
/// handled type is then mapped as an ordinary column.
/// </summary>
/// <example>
/// <code>
/// public sealed class EmailHandler : TypeHandler&lt;Email&gt;
/// {
///     public override object? Write(Email value) =&gt; value.Address;
///     public override Email Read(object value) =&gt; new Email((string)value);
/// }
/// </code>
/// </example>
/// <typeparam name="T">The handled type.</typeparam>
public abstract class TypeHandler<T> : ITypeHandler
{
    Type ITypeHandler.Type => typeof(T);

    /// <summary>Sets provider-specific parameter details, such as a JSON parameter type.</summary>
    public virtual void ConfigureParameter(DbParameter parameter)
    {
    }

    /// <summary>Converts a database value back. Never called with <see langword="null"/> or <see cref="DBNull"/>.</summary>
    public abstract T Read(object value);

    /// <summary>Converts a value to what is sent to the database. Never called with <see langword="null"/>.</summary>
    public abstract object? Write(T value);

    object? ITypeHandler.Write(object value) => Write((T)value);

    object? ITypeHandler.Read(object value) => Read(value);
}
