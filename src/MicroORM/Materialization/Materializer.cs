using MicroORM.Mapping;
using MicroORM.TypeHandlers;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;

namespace MicroORM.Materialization;

internal sealed class Materializer
{
    private static readonly MethodInfo GetValueMethod =
        typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetValue), [typeof(int)])!;

    private static readonly MethodInfo ToMethod =
        typeof(ValueConverter).GetMethod(nameof(ValueConverter.To))!;

    private static readonly MethodInfo ReadWithMethod =
        typeof(TypeHandlerRegistry).GetMethod(nameof(TypeHandlerRegistry.ReadWith))!;

    private readonly ConcurrentDictionary<(Type Type, int Start, string Layout), Delegate> _cache = new();

    private readonly MappingRegistry _mappings;

    private readonly TypeHandlerRegistry _handlers;

    public Materializer(MappingRegistry mappings, TypeHandlerRegistry handlers)
    {
        _mappings = mappings;
        _handlers = handlers;
    }

    /// <summary>Gets a reader for all columns.</summary>
    public Func<DbDataReader, T> For<T>(DbDataReader reader) => For<T>(reader, 0, reader.FieldCount);

    /// <summary>Gets a reader for the columns <c>[start, start + count)</c>.</summary>
    public Func<DbDataReader, T> For<T>(DbDataReader reader, int start, int count)
    {
        var names = new string[count];
        for (var i = 0; i < count; i++)
        {
            names[i] = reader.GetName(start + i);
        }

        var layout = string.Join('\u001f', names);
        return (Func<DbDataReader, T>)_cache.GetOrAdd((typeof(T), start, layout), _ => Build<T>(names, start));
    }

    /// <summary>
    /// Gets a reader that splits each row into <paramref name="types"/>.Length objects at the
    /// <paramref name="splitOn"/> columns and combines them with <paramref name="map"/>.
    /// </summary>
    public Func<DbDataReader, TResult> ForSplit<TResult>(DbDataReader reader, Type[] types, string splitOn, Func<object?[], TResult> map)
    {
        int[] bounds = SplitPoints(reader, types.Length, splitOn);
        var readers = new Func<DbDataReader, object?>[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            int start = bounds[i];
            int count = (i + 1 < types.Length ? bounds[i + 1] : reader.FieldCount) - start;
            object? typed = typeof(Materializer).GetMethod(nameof(Boxed), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(types[i])
                .Invoke(this, [reader, start, count]);
            var read = (Func<DbDataReader, object?>)typed!;

            // A NULL first column means an unmatched outer join: the object is null, not empty.
            readers[i] = i == 0 ? read : r => r.IsDBNull(start) ? null : read(r);
        }

        return r =>
        {
            var parts = new object?[readers.Length];
            for (int i = 0; i < readers.Length; i++)
            {
                parts[i] = readers[i](r);
            }

            return map(parts);
        };
    }

    private Func<DbDataReader, object?> Boxed<T>(DbDataReader reader, int start, int count)
    {
        var typed = For<T>(reader, start, count);
        return r => typed(r);
    }

    private static int[] SplitPoints(DbDataReader reader, int parts, string splitOn)
    {
        var names = splitOn.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (names.Length == 0)
        {
            throw new ArgumentException("splitOn must name at least one column.", nameof(splitOn));
        }

        var bounds = new int[parts];
        for (int i = 1; i < parts; i++)
        {
            var name = names[Math.Min(i - 1, names.Length - 1)];
            var found = -1;
            for (var column = bounds[i - 1] + 1; column < reader.FieldCount; column++)
            {
                if (string.Equals(reader.GetName(column), name, StringComparison.OrdinalIgnoreCase))
                {
                    found = column;
                    break;
                }
            }

            bounds[i] = found >= 0 ? found : throw new InvalidOperationException(
                $"Multi-mapping could not find split column '{name}' after column {bounds[i - 1]}. Result columns: {string.Join(", ", Enumerable.Range(0, reader.FieldCount).Select(reader.GetName))}. Pass splitOn with the first column of each object after the first.");
        }

        return bounds;
    }

    private Func<DbDataReader, T> Build<T>(string[] names, int start)
    {
        var type = typeof(T);
        if (type == typeof(object) || type == typeof(Row))
        {
            var schema = new RowSchema(names);
            return reader => (T)(object)ReadRow(reader, schema, start);
        }

        if (_handlers.IsScalar(type))
        {
            return reader => _handlers.Convert<T>(reader.GetValue(start));
        }

        return BuildEntityReader<T>(names, start);
    }

    private Func<DbDataReader, T> BuildEntityReader<T>(string[] names, int start)
    {
        var type = typeof(T);
        var map = _mappings.GetMap(type);

        // Every name a column may go by, pointing at its ordinal in the reader.
        var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < names.Length; i++)
        {
            ordinals.TryAdd(names[i], start + i);
            ordinals.TryAdd(Normalize(names[i]), start + i);
        }

        int? OrdinalOf(string memberName)
        {
            var column = map.FindByProperty(memberName)?.Name;
            if (column is not null && ordinals.TryGetValue(column, out var byColumn))
            {
                return byColumn;
            }

            return ordinals.TryGetValue(memberName, out var byName) ? byName
                : ordinals.TryGetValue(Normalize(memberName), out var byNormalized) ? byNormalized
                : null;
        }

        var reader = Expression.Parameter(typeof(DbDataReader), "reader");
        Expression Read(int ordinal, Type target)
        {
            var raw = Expression.Call(reader, GetValueMethod, Expression.Constant(ordinal));
            return _handlers.TryGet(target, out var handler)
                ? Expression.Call(ReadWithMethod.MakeGenericMethod(target), Expression.Constant(handler, typeof(ITypeHandler)), raw)
                : Expression.Call(ToMethod.MakeGenericMethod(target), raw);
        }

        var (constructor, arguments) = ChooseConstructor(type, OrdinalOf);
        var parameters = constructor.GetParameters();
        var newExpression = Expression.New(constructor, arguments.Select((ordinal, i) => Read(ordinal, parameters[i].ParameterType)));

        var bound = new HashSet<string>(parameters.Select(p => p.Name!), StringComparer.OrdinalIgnoreCase);
        var bindings = new List<MemberBinding>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (bound.Contains(property.Name) || property.SetMethod is not { IsPublic: true } || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (OrdinalOf(property.Name) is int ordinal)
            {
                bindings.Add(Expression.Bind(property, Read(ordinal, property.PropertyType)));
            }
        }

        Expression body = bindings.Count > 0 ? Expression.MemberInit(newExpression, bindings) : newExpression;
        return Expression.Lambda<Func<DbDataReader, T>>(body, reader).Compile();
    }

    private static (ConstructorInfo Constructor, int[] Ordinals) ChooseConstructor(Type type, Func<string, int?> ordinalOf)
    {
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (constructors.FirstOrDefault(c => c.GetParameters().Length == 0) is { } parameterless)
        {
            return (parameterless, []);
        }

        foreach (var constructor in constructors.OrderByDescending(c => c.GetParameters().Length))
        {
            int?[] ordinals = constructor.GetParameters().Select(p => ordinalOf(p.Name!)).ToArray();
            if (ordinals.All(o => o is not null))
            {
                return (constructor, ordinals.Select(o => o!.Value).ToArray());
            }
        }

        throw new InvalidOperationException(
           $"Cannot materialize '{type.Name}': it has no parameterless constructor and no constructor whose parameters all match result columns.");
    }

    private static Row ReadRow(DbDataReader reader, RowSchema schema, int start)
    {
        var values = new object?[schema.Names.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var value = reader.GetValue(start + i);
            values[i] = value is DBNull ? null : value;
        }

        return new Row(schema, values);
    }

    private static string Normalize(string name) => name.Replace("_", string.Empty, StringComparison.Ordinal);
}
