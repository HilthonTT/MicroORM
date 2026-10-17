using MicroORM.TypeHandlers;
using Npgsql;
using NpgsqlTypes;
using System.Data.Common;

namespace MicroORM.Tests.Model;

/// <summary>A JSON handler that sends PostgreSQL parameters as jsonb, showing provider-specific configuration.</summary>
public sealed class JsonbHandler<T> : JsonTypeHandler<T>
{
    public override void ConfigureParameter(DbParameter parameter)
    {
        if (parameter is NpgsqlParameter npgsql)
        {
            npgsql.NpgsqlDbType = NpgsqlDbType.Jsonb;
        }
    }
}
