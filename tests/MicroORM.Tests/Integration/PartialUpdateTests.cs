using MicroORM.Exceptions;
using MicroORM.Tests.Model;

using static MicroORM.Tests.Integration.Seed;

namespace MicroORM.Tests.Integration;

[Collection(ProviderCollectionDefinition.Name)]
public class PartialUpdateTests
{
    public static TheoryData<string> Returning => TestDatabase.With(["sqlite", "postgres", "sqlserver"]);

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Partial_updates_write_only_the_chosen_columns(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var people = database.Db.Table<Person>();
        var ada = (await people.FirstOrDefaultAsync(p => p.Name == "Ada"))!;
        ada.Name = "Changed";
        ada.Email = "new@example.com";
        ada.Age = 37;

        Assert.True(await people.UpdateAsync(ada, p => p.Age));
        var update = Assert.Single(database.Log, l => l.Sql.StartsWith("UPDATE", StringComparison.Ordinal));
        Assert.Single(update.Parameters, p => Equals(p.Value, 37));
        Assert.Equal(2, update.Parameters.Count);
        var loaded = (await people.FindAsync(ada.Id))!;
        Assert.Equal((37, "Ada", "ada@example.com"), (loaded.Age, loaded.Name, loaded.Email));

        Assert.True(people.Update(ada, p => new { p.Name, p.Email }));
        loaded = (await people.FindAsync(ada.Id))!;
        Assert.Equal(("Changed", "new@example.com"), (loaded.Name, loaded.Email));

        await Assert.ThrowsAsync<ArgumentException>(() => people.UpdateAsync(ada, p => p.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => people.UpdateAsync(ada, p => p.DisplayName));
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Partial_updates_check_and_increment_the_version(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        var documents = database.Db.Table<Document>();
        var document = new Document { Title = "Draft" };
        await documents.InsertAsync(document);
        var stale = new Document { Id = document.Id, Title = document.Title, Version = document.Version };

        document.Title = "Final";
        Assert.True(await documents.UpdateAsync(document, d => d.Title));
        Assert.Equal(1, document.Version);
        Assert.Equal((1, "Final"), ((await documents.FindAsync(document.Id))!.Version, (await documents.FindAsync(document.Id))!.Title));

        stale.Title = "Lost";
        await Assert.ThrowsAsync<MicroConcurrencyException>(() => documents.UpdateAsync(stale, d => d.Title));
        await Assert.ThrowsAsync<ArgumentException>(() => documents.UpdateAsync(document, d => d.Version));
    }

    [ProviderTheory("sqlite", "postgres", "sqlserver")]
    [MemberData(nameof(Returning))]
    public async Task Execute_update_returning_reads_the_updated_rows(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var people = database.Db.Table<Person>();

        var older = await people.Where(p => p.Age > 40).ExecuteUpdateReturningAsync(s => s.SetFrom(p => p.Age, p => p.Age + 1));
        Assert.Equal([("50% Eve_", 42), ("Cleo", 53)], older.Select(p => (p.Name, p.Age)).Order());
        Assert.All(older, p => Assert.True(p.Id > 0));

        var ada = Assert.Single(people.Where(p => p.Name == "Ada").ExecuteUpdateReturning(s => s.Set(p => p.Email, "ada@new.example")));
        Assert.Equal(("Ada", 36, "ada@new.example"), (ada.Name, ada.Age, ada.Email));
    }
}
