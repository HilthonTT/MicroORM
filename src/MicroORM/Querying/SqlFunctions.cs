namespace MicroORM.Querying;

/// <summary>
/// SQL functions for typed queries. They only translate to SQL; calling them in .NET throws.
/// </summary>
public static class SqlFunctions
{
    /// <summary>
    /// Matches <paramref name="value"/> against a <c>LIKE</c> pattern, with <c>%</c> and <c>_</c> as
    /// wildcards. Unlike <see cref="string.StartsWith(string)"/> and friends, the pattern is not escaped.
    /// </summary>
    /// <example><c>Where(u =&gt; SqlFunctions.Like(u.Email, "%@example.___"))</c></example>
    public static bool Like(string? value, string? pattern) =>
        throw new InvalidOperationException($"{nameof(SqlFunctions)}.{nameof(Like)} can only be used in a typed query.");
}
