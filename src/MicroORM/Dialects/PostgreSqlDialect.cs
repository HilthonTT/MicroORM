namespace MicroORM.Dialects;

internal sealed class PostgreSqlDialect : SqlDialect
{
    public override string Name => "PostgreSQL";

    public override string ProcedureParameterName(string name) => name;

    protected override string QuotePart(string part) => "\"" + part.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
