namespace MicroORM.Mapping;

public abstract class EntityConfiguration
{
    internal string? Table { get; set; }

    internal IReadOnlyList<string>? KeyProperties { get; set; }

    internal string? VersionProperty { get; set; }

    internal Dictionary<string, string> ColumnNames { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, ValueGeneration> Generation { get; } = new(StringComparer.Ordinal);

    internal HashSet<string> Ignored { get; } = new(StringComparer.Ordinal);
}
