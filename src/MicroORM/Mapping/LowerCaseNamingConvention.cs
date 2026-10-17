namespace MicroORM.Mapping;

internal sealed class LowerCaseNamingConvention : NamingConvention
{
    public override string Convert(string name) => name.ToLowerInvariant();
}