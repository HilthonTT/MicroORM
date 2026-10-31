using System.Linq.Expressions;

namespace MicroORM.Mapping;

/// <summary>Non-generic storage for fluent mapping overrides.</summary>
public abstract class EntityConfiguration
{
    internal string? Table { get; set; }

    internal IReadOnlyList<string>? KeyProperties { get; set; }

    internal string? VersionProperty { get; set; }

    internal Dictionary<string, string> ColumnNames { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, ValueGeneration> Generation { get; } = new(StringComparer.Ordinal);

    internal HashSet<string> Ignored { get; } = new(StringComparer.Ordinal);

    internal List<LambdaExpression> QueryFilters { get; } = [];

    /// <summary>A copy that later changes to this configuration don't affect.</summary>
    internal EntityConfiguration Snapshot()
    {
        var copy = new Copy { Table = Table, KeyProperties = KeyProperties?.ToArray(), VersionProperty = VersionProperty };
        foreach (var (property, column) in ColumnNames)
        {
            copy.ColumnNames[property] = column;
        }

        foreach (var (property, generation) in Generation)
        {
            copy.Generation[property] = generation;
        }

        copy.Ignored.UnionWith(Ignored);
        copy.QueryFilters.AddRange(QueryFilters);
        return copy;
    }

    private sealed class Copy : EntityConfiguration;
}
