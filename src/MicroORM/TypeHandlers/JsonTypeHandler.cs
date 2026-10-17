using System.Text.Json;

namespace MicroORM.TypeHandlers;

public sealed class JsonTypeHandler<T> : TypeHandler<T>
{
    private readonly JsonSerializerOptions? _options;

    public JsonTypeHandler(JsonSerializerOptions? options = null) => _options = options;

    public override T Read(object value) => value switch
    {
        string text => JsonSerializer.Deserialize<T>(text, _options)!,
        byte[] utf8 => JsonSerializer.Deserialize<T>(utf8, _options)!,
        _ => JsonSerializer.Deserialize<T>(value.ToString()!, _options)!,
    };

    public override object? Write(T value) => JsonSerializer.Serialize(value, _options);
}
