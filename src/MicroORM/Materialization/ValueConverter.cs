using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace MicroORM.Materialization;

/// <summary>
/// Converts raw provider values to CLR types. Providers disagree on representations (SQLite returns
/// <see cref="long"/> for every integer and text for GUIDs, dates and decimals), so materialization
/// goes through one forgiving conversion point.
/// </summary>
public static class ValueConverter
{
    /// <summary>Converts a database value to <typeparamref name="T"/>; <see cref="DBNull"/> becomes default.</summary>
    public static T To<T>(object? value)
    {
        // Before the type test, so To<object>(DBNull.Value) is null rather than DBNull.
        if (value is null or DBNull)
        {
            return default!;
        }

        if (value is T typed)
        {
            return typed;
        }

        // SQLite and MySQL return 64-bit integers for every integer column. Converting through
        // Convert.ChangeType would box the result again; these paths don't allocate.
        if (value is long wide)
        {
            if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
            {
                return Reinterpret<int, T>(checked((int)wide));
            }

            if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
            {
                return Reinterpret<bool, T>(wide != 0);
            }

            if (typeof(T) == typeof(short) || typeof(T) == typeof(short?))
            {
                return Reinterpret<short, T>(checked((short)wide));
            }
        }

        return (T)Convert(value, typeof(T))!;
    }

    private static TTarget Reinterpret<TSource, TTarget>(TSource value)
       where TSource : struct
    {
        if (typeof(TTarget) == typeof(TSource))
        {
            return Unsafe.As<TSource, TTarget>(ref value);
        }

        TSource? nullable = value;
        return Unsafe.As<TSource?, TTarget>(ref nullable);
    }

    /// <summary>Converts a database value to <paramref name="target"/>; <see cref="DBNull"/> becomes null or default.</summary>
    public static object? Convert(object? value, Type target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (value is null or DBNull)
        {
            return target.IsValueType && Nullable.GetUnderlyingType(target) is null ? Activator.CreateInstance(target) : null;
        }

        var type = Nullable.GetUnderlyingType(target) ?? target;
        if (type.IsInstanceOfType(value))
        {
            return value;
        }

        try
        {
            return ConvertCore(value, type);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            throw new InvalidCastException($"Cannot convert database value '{value}' ({value.GetType().Name}) to {target.Name}.", ex);
        }
    }

    private static object ConvertCore(object value, Type type)
    {
        var inv = CultureInfo.InvariantCulture;

        if (type.IsEnum)
        {
            return value is string name
                ? Enum.Parse(type, name, ignoreCase: true)
                : Enum.ToObject(type, System.Convert.ChangeType(value, Enum.GetUnderlyingType(type), inv));
        }

        if (type == typeof(string))
        {
            return System.Convert.ToString(value, inv)!;
        }

        if (type == typeof(Guid))
        {
            return value switch
            {
                string s => Guid.Parse(s),
                byte[] { Length: 16 } bytes => new Guid(bytes),
                _ => throw new InvalidCastException(),
            };
        }

        if (type == typeof(bool))
        {
            return value switch
            {
                string s => s == "1" || (s != "0" && bool.Parse(s)),
                _ => System.Convert.ToInt64(value, inv) != 0,
            };
        }

        if (type == typeof(DateTime))
        {
            return value switch
            {
                // Text with an offset is an instant: read as UTC, as a DateTimeOffset is, not in the machine's zone.
                string s => DateTime.Parse(s, inv, DateTimeStyles.RoundtripKind) is { Kind: DateTimeKind.Local }
                    ? DateTimeOffset.Parse(s, inv).UtcDateTime
                    : DateTime.Parse(s, inv, DateTimeStyles.RoundtripKind),
                DateTimeOffset dto => dto.UtcDateTime,
                DateOnly d => d.ToDateTime(TimeOnly.MinValue),
                _ => System.Convert.ToDateTime(value, inv),
            };
        }

        if (type == typeof(DateTimeOffset))
        {
            return value switch
            {
                string s => DateTimeOffset.Parse(s, inv, DateTimeStyles.AssumeUniversal),
                DateTime dt => new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt),
                _ => throw new InvalidCastException(),
            };
        }

        if (type == typeof(DateOnly))
        {
            return value switch
            {
                string s => DateOnly.Parse(s, inv),
                DateTime dt => DateOnly.FromDateTime(dt),
                _ => throw new InvalidCastException(),
            };
        }

        if (type == typeof(TimeOnly))
        {
            return value switch
            {
                string s => TimeOnly.Parse(s, inv),
                TimeSpan ts => TimeOnly.FromTimeSpan(ts),
                DateTime dt => TimeOnly.FromDateTime(dt),
                _ => throw new InvalidCastException(),
            };
        }

        if (type == typeof(TimeSpan))
        {
            return value switch
            {
                string s => TimeSpan.Parse(s, inv),
                TimeOnly t => t.ToTimeSpan(),
                _ => TimeSpan.FromTicks(System.Convert.ToInt64(value, inv)),
            };
        }

        if (type == typeof(char) && value is string { Length: 1 } single)
        {
            return single[0];
        }

        if (type == typeof(byte[]) && value is string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        return System.Convert.ChangeType(value, type, inv);
    }
}
