using System.Data;

namespace MicroORM.Procedures;

internal static class DbTypes
{
    private static readonly Dictionary<Type, DbType> Map = new()
    {
        [typeof(string)] = DbType.String,
        [typeof(bool)] = DbType.Boolean,
        [typeof(byte)] = DbType.Byte,
        [typeof(short)] = DbType.Int16,
        [typeof(int)] = DbType.Int32,
        [typeof(long)] = DbType.Int64,
        [typeof(float)] = DbType.Single,
        [typeof(double)] = DbType.Double,
        [typeof(decimal)] = DbType.Decimal,
        [typeof(DateTime)] = DbType.DateTime,
        [typeof(DateTimeOffset)] = DbType.DateTimeOffset,
        [typeof(Guid)] = DbType.Guid,
        [typeof(byte[])] = DbType.Binary,
    };

    public static DbType? For(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return Map.TryGetValue(underlying, out var dbType) ? dbType : null;
    }
}
