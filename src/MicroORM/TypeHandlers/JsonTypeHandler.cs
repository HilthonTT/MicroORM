using System.Text.Json;

namespace MicroORM.TypeHandlers;

/// <summary>
/// Stores a value as JSON text using <see cref="JsonSerializer"/>. Suitable for <c>TEXT</c>,
/// <c>NVARCHAR</c>, MySQL <c>JSON</c> and PostgreSQL <c>json</c>/<c>jsonb</c> columns (for the
/// latter, override <see cref="TypeHandler{T}.ConfigureParameter"/> to set the Npgsql type).
/// </summary>
/// <typeparam name="T">The type serialized into the column.</typeparam>
public class JsonTypeHandler<T> : TypeHandler<T>
{
    private readonly JsonSerializerOptions? _options;

    /// <summary>Creates a handler using optional serializer options.</summary>
    public JsonTypeHandler(JsonSerializerOptions? options = null) => _options = options;

    /// <inheritdoc />
    public override T Read(object value) => value switch
    {
        string text => JsonSerializer.Deserialize<T>(text, _options)!,
        byte[] utf8 => JsonSerializer.Deserialize<T>(utf8, _options)!,
        _ => JsonSerializer.Deserialize<T>(value.ToString()!, _options)!,
    };

    /// <inheritdoc />
    public override object? Write(T value) => JsonSerializer.Serialize(value, _options);
}
