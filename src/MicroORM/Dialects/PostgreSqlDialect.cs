namespace MicroORM.Dialects;

internal sealed class PostgreSqlDialect : SqlDialect
{
    public override string Name => "PostgreSQL";

    public override string ProcedureParameterName(string name) => name;

    // EXTRACT returns numeric, with fractional seconds; FLOOR keeps the cast from rounding them up.
    public override Sql.Sql DatePart(DatePart part, Sql.Sql value) =>
        Sql.Sql.Empty.Append("CAST(FLOOR(").Append(base.DatePart(part, value)).Append(") AS INTEGER)");

    protected override string QuotePart(string part) => "\"" + part.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
