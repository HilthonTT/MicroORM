namespace MicroORM.Tests.Integration;

/// <summary>
/// Integration test classes share real databases and table names, so they run serially in one
/// collection.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProviderCollectionDefinition
{
    public const string Name = "Providers";
}
