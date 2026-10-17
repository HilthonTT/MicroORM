namespace MicroORM.Mapping;

/// <summary>How the database produces a column's value.</summary>
public enum ValueGeneration
{
    /// <summary>The application always supplies the value.</summary>
    None,

    /// <summary>The database generates the value on insert (identity, auto-increment, serial).</summary>
    Identity,

    /// <summary>The database computes the value; MicroORM never writes it.</summary>
    Computed,
}
