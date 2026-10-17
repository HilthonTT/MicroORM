using MicroORM.Exceptions;
using MicroORM.Tests.Model;

namespace MicroORM.Tests.Integration;

[Collection(ProviderCollectionDefinition.Name)]
public class ConcurrencyTests
{
    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Stale_updates_and_deletes_are_rejected(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        var documents = database.Db.Table<Document>();
        var original = new Document { Title = "Draft", Version = 1 };
        await documents.InsertAsync(original);

        // Two readers load the same version.
        var alice = (await documents.FindAsync(original.Id))!;
        var bob = (await documents.FindAsync(original.Id))!;

        alice.Title = "Alice's edit";
        Assert.True(await documents.UpdateAsync(alice));
        Assert.Equal(2, alice.Version);

        bob.Title = "Bob's edit";
        var conflict = await Assert.ThrowsAsync<MicroConcurrencyException>(() => documents.UpdateAsync(bob));
        Assert.Same(bob, conflict.Entity);
        Assert.Equal(1, bob.Version);
        await Assert.ThrowsAsync<MicroConcurrencyException>(() => documents.DeleteAsync(bob));

        var stored = (await documents.FindAsync(original.Id))!;
        Assert.Equal("Alice's edit", stored.Title);
        Assert.Equal(2, stored.Version);

        // The current version may update and delete.
        stored.Title = "Final";
        await documents.UpdateAsync(stored);
        Assert.Equal(3, stored.Version);
        Assert.True(await documents.DeleteAsync(stored));
    }
}
