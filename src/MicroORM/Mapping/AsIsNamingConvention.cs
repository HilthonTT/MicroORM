namespace MicroORM.Mapping;

internal sealed class AsIsNamingConvention : NamingConvention
{
    public override string Convert(string name) => name;
}