namespace MicroORM.Materialization;

internal static class ScalarTypes
{
    private static readonly HashSet<Type> Known = 
    [
        typeof(string), typeof(char), typeof(bool),
        typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal),
        typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan),
        typeof(Guid), typeof(byte[]),
    ];

    public static bool IsScalar(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsEnum || Known.Contains(underlying);
    }
}
