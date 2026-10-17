using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;

namespace MicroORM.Materialization;

[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "A row is the natural name for a result row.")]
public sealed class Row : DynamicObject, IReadOnlyDictionary<string, object?>
{
    private readonly RowSchema _schema;
    private readonly object?[] _values;

    internal Row(RowSchema schema, object?[] values)
    {
        _schema = schema;
        _values = values;
    }

    /// <summary>Gets a column value, with <see cref="DBNull"/> already turned into <see langword="null"/>.</summary>
    /// <exception cref="KeyNotFoundException">The row has no such column.</exception>
    public object? this[string key] => TryGetValue(key, out var value)
        ? value
        : throw new KeyNotFoundException($"The row has no column '{key}'. Columns: {string.Join(", ", _schema.Names)}.");

    /// <summary>Gets a column value by position.</summary>
    public object? this[int ordinal] => _values[ordinal];

    /// <summary>Gets a column value converted to <typeparamref name="T"/>.</summary>
    public T Get<T>(string column) => ValueConverter.To<T>(this[column]);

    /// <inheritdoc />
    public IEnumerable<string> Keys => _schema.Names;

    /// <inheritdoc />
    public IEnumerable<object?> Values => _values;

    /// <inheritdoc />
    public int Count => _values.Length;

    /// <inheritdoc />
    public bool ContainsKey(string key) => _schema.Ordinals.ContainsKey(key);

    /// <inheritdoc />
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out object? value)
    {
        if (_schema.Ordinals.TryGetValue(key, out var ordinal))
        {
            value = _values[ordinal];
            return true;
        }

        value = null;
        return false;
    }

    /// <inheritdoc />
    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        ArgumentNullException.ThrowIfNull(binder);
        return TryGetValue(binder.Name, out result);
    }

    /// <inheritdoc />
    public override IEnumerable<string> GetDynamicMemberNames() => _schema.Names;

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        for (var i = 0; i < _values.Length; i++)
        {
            yield return new KeyValuePair<string, object?>(_schema.Names[i], _values[i]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public override string ToString() => "{ " + string.Join(", ", this.Select(kv => $"{kv.Key} = {kv.Value ?? "NULL"}")) + " }";
}