using MicroORM.Procedures;
using MicroORM.Sql;
using System.Data;
using System.Data.Common;

namespace MicroORM.Dialects;

internal sealed class PostgreSqlDialect : SqlDialect
{
    public override string Name => "PostgreSQL";

    public override string ProcedureParameterName(string name) => name;

    // The protocol allows 65535; this leaves room for any the provider adds.
    public override int MaxParameters => 32000;

    // EXTRACT returns numeric, with fractional seconds; FLOOR keeps the cast from rounding them up.
    public override Sql.Sql DatePart(DatePart part, Sql.Sql value) =>
        Sql.Sql.Empty.Append("CAST(FLOOR(").Append(base.DatePart(part, value)).Append(") AS INTEGER)");

    public override Sql.Sql Trim(Sql.Sql value, bool leading, bool trailing) => leading && trailing
        ? Sql.Sql.Empty.Append("BTRIM(").Append(value).Append(", ").AppendParameter(Whitespace, typeof(string)).Append(")")
        : base.Trim(value, leading, trailing);

    // An untyped NULL, as in an empty list's subquery, can't be resolved against a column.
    public override void ConfigureParameter(DbParameter parameter, SqlParameterValue value)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Value is null && value.ClrType is { } type && DbTypes.For(type) is DbType dbType)
        {
            parameter.DbType = dbType;
        }
    }

    protected override string QuotePart(string part) => "\"" + part.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
