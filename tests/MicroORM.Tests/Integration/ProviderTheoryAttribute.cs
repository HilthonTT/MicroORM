namespace MicroORM.Tests.Integration;

/// <summary>
/// A theory over a subset of providers that is skipped, rather than failing for lack of data,
/// when none of them is configured.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ProviderTheoryAttribute : TheoryAttribute
{
    public ProviderTheoryAttribute(params string[] providers)
    {
        if (!providers.Any(TestDatabase.IsConfigured))
        {
            Skip = $"Needs one of: {string.Join(", ", providers)}. Set the MICROORM_TEST_* connection strings (see CONTRIBUTING.md).";
        }
    }
}
