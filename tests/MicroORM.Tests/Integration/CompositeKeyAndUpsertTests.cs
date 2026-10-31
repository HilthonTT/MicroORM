using MicroORM.Exceptions;
using MicroORM.Tests.Model;

using static MicroORM.Tests.Integration.Seed;

namespace MicroORM.Tests.Integration;

[Collection(ProviderCollectionDefinition.Name)]
public class CompositeKeyAndUpsertTests
{
    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Upserting_a_versioned_entity_checks_and_increments_the_version(string provider)
    {
        // Quantity stands in for a version on a table whose key the entity supplies.
        await using var database = await TestDatabase.CreateAsync(provider, o => o.Entity<OrderLine>(e => e.IsVersion(l => l.Quantity)));
        var lines = database.Db.Table<OrderLine>();
        var line = new OrderLine { OrderId = 1, LineNo = 1, Product = "Pen", Quantity = 1 };
        await lines.UpsertAsync(line);
        Assert.Equal(1, line.Quantity);

        var stale = new OrderLine { OrderId = 1, LineNo = 1, Product = "Stale", Quantity = 0 };
        await Assert.ThrowsAsync<MicroConcurrencyException>(() => lines.UpsertAsync(stale));

        line.Product = "Ink";
        await lines.UpsertAsync(line);
        Assert.Equal(2, line.Quantity);
        var stored = (await lines.FindAsync((1, 1)))!;
        Assert.Equal(("Ink", 2), (stored.Product, stored.Quantity));
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Composite_keys_find_update_and_delete(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        var lines = database.Db.Table<OrderLine>();
        await lines.InsertManyAsync(
        [
            new OrderLine { OrderId = 1, LineNo = 1, Product = "Warp", Quantity = 2 },
            new OrderLine { OrderId = 1, LineNo = 2, Product = "MicroORM", Quantity = 5 },
            new OrderLine { OrderId = 2, LineNo = 1, Product = "Loom", Quantity = 1 },
        ]);

        var line = (await lines.FindAsync((1, 2)))!;
        Assert.Equal("MicroORM", line.Product);
        Assert.Null(await lines.FindAsync((2, 2)));

        line.Quantity = 6;
        Assert.True(await lines.UpdateAsync(line));
        Assert.Equal(6, (await lines.FindAsync(new object[] { 1, 2 }))!.Quantity);

        Assert.True(await lines.DeleteByKeyAsync((2, 1)));
        Assert.True(await lines.DeleteAsync(line));
        Assert.Equal(1, await lines.CountAsync());

        await Assert.ThrowsAsync<ArgumentException>(() => lines.FindAsync(1));
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Upsert_on_application_keys_inserts_then_updates(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        var tags = database.Db.Table<Tag>();
        var lines = database.Db.Table<OrderLine>();
        var tag = new Tag { Code = Guid.NewGuid(), Label = "new" };

        await tags.UpsertAsync(tag);
        tag.Label = "changed";
        await tags.UpsertAsync(tag);
        Assert.Equal("changed", Assert.Single(await tags.ToListAsync()).Label);

        await lines.UpsertAsync(new OrderLine { OrderId = 7, LineNo = 1, Product = "Warp", Quantity = 1 });
        await lines.UpsertAsync(new OrderLine { OrderId = 7, LineNo = 1, Product = "Warp", Quantity = 9 });
        await lines.UpsertAsync(new OrderLine { OrderId = 7, LineNo = 2, Product = "MicroORM", Quantity = 3 });
        Assert.Equal([9, 3], (await lines.OrderBy(l => l.LineNo).ToListAsync()).Select(l => l.Quantity));
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Upsert_on_a_unique_column_returns_the_identity_both_ways(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        var customers = database.Db.Table<Customer>();
        var first = new Customer { Email = new Email("ada@example.com"), Name = "Ada", Tier = CustomerTier.Standard };

        await customers.UpsertAsync(first, on: c => c.Email);
        Assert.True(first.Id > 0);

        var again = new Customer { Email = new Email("ada@example.com"), Name = "Ada Lovelace", Tier = CustomerTier.Gold };
        await customers.UpsertAsync(again, on: c => c.Email);

        Assert.Equal(first.Id, again.Id);
        var stored = Assert.Single(await customers.ToListAsync());
        Assert.Equal("Ada Lovelace", stored.Name);
        Assert.Equal(CustomerTier.Gold, stored.Tier);
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Upsert_by_identity_key_saves(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        var people = database.Db.Table<Person>();
        var person = NewPerson("Ada", 36);

        await people.UpsertAsync(person);
        var id = person.Id;
        Assert.True(id > 0);

        person.Age = 37;
        await people.UpsertAsync(person);
        Assert.Equal(id, person.Id);
        Assert.Equal(37, (await people.FindAsync(id))!.Age);
        Assert.Equal(1, await people.CountAsync());
    }
}
