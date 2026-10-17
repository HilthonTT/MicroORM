namespace MicroORM.Materialization;

internal sealed class RowSchema
{
    public RowSchema(string[] names)
    {
        Names = names;
        Ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < names.Length; i++)
        {
            Ordinals.TryAdd(names[i], i);
        }
    }

    public string[] Names { get; set; }

    public Dictionary<string, int> Ordinals { get; set; }
}
