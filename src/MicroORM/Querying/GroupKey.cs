namespace MicroORM.Querying;

/// <summary>A grouping key: one value, or the named members of a composite key.</summary>
internal sealed class GroupKey
{
    public GroupKey(IReadOnlyList<(string? Name, Sql.Sql Value)> parts) => Parts = parts;

    /// <summary>Gets the SQL of each part; names are <see langword="null"/> for a single-value key.</summary>
    public IReadOnlyList<(string? Name, Sql.Sql Value)> Parts { get; }

    public bool IsComposite => Parts[0].Name is not null;
}
