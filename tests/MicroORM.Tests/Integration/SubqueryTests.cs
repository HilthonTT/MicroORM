using MicroORM.Tests.Model;

using static MicroORM.Tests.Integration.Seed;

namespace MicroORM.Tests.Integration;

[Collection(ProviderCollectionDefinition.Name)]
public class SubqueryTests
{
    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Any_finds_rows_with_correlated_rows(string provider)
    {
        await using var database = await PeopleAsync(provider);
        await OrdersAsync(database);
        var people = database.Db.Table<Person>();
        var orders = database.Db.Table<Order>();

        Assert.Equal(["Ada", "Cleo", "Dmitri"], await NamesAsync(people.Where(p => orders.Any(o => o.PersonId == p.Id))));
        Assert.Equal(["50% Eve_", "Brendan"], await NamesAsync(people.Where(p => !orders.Any(o => o.PersonId == p.Id))));

        // The source's own filters and paging stay in the subquery.
        var large = orders.Where(o => o.Amount >= 30);
        Assert.Equal(["Ada", "Cleo"], await NamesAsync(people.Where(p => large.Any(o => o.PersonId == p.Id))));
        var cheapest = orders.OrderBy(o => o.Amount).Take(2);
        Assert.Equal(["Ada", "Dmitri"], await NamesAsync(people.Where(p => cheapest.Any(o => o.PersonId == p.Id))));

        // The same table, nested: people younger than someone who ordered (Cleo, 52, is the oldest).
        Assert.Equal(
            ["50% Eve_", "Ada", "Brendan", "Dmitri"],
            await NamesAsync(people.Where(p => people.Any(q => q.Age > p.Age && orders.Any(o => o.PersonId == q.Id)))));
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Contains_matches_nulls_and_negates_safely(string provider)
    {
        await using var database = await PeopleAsync(provider);
        await OrdersAsync(database);
        var people = database.Db.Table<Person>();
        var buyers = database.Db.Table<Order>().Select(o => o.PersonId);

        Assert.Equal(["Ada", "Cleo", "Dmitri"], await NamesAsync(people.Where(p => buyers.Contains(p.Id))));

        // One order has no person; with NOT IN, that NULL would hide everyone.
        Assert.Equal(["50% Eve_", "Brendan"], await NamesAsync(people.Where(p => !buyers.Contains(p.Id))));

        Assert.True(await buyers.ContainsAsync(null));
        Assert.False(buyers.Contains(-1));
        Assert.True(await buyers.Distinct().ContainsAsync((await people.FirstOrDefaultAsync(p => p.Name == "Cleo"))!.Id));

        // int?[] binds to MemoryExtensions.Contains(span, value, comparer) under C# 14.
        var orders = database.Db.Table<Order>();
        int?[] nobody = [null];
        Assert.Equal(1, await orders.Where(o => nobody.Contains(o.PersonId)).CountAsync());
        Assert.Equal(5, await orders.Where(o => !nobody.Contains(o.PersonId)).CountAsync());

        // A paged Distinct reads the page from a derived table; the correlated subquery still finds its row.
        var youngest = await people.OrderBy(p => p.Age).Take(3)
            .Select(p => new { Ordered = orders.Any(o => o.PersonId == p.Id) }).Distinct().ToListAsync();
        Assert.Equal([false, true], youngest.Select(r => r.Ordered).Order());
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Subqueries_in_projections_and_set_based_writes(string provider)
    {
        await using var database = await PeopleAsync(provider);
        await OrdersAsync(database);
        var people = database.Db.Table<Person>();
        var orders = database.Db.Table<Order>();

        var flags = await people.OrderBy(p => p.Age).Select(p => new { p.Name, HasOrders = orders.Any(o => o.PersonId == p.Id) }).ToListAsync();
        Assert.Equal([false, true, true, false, true], flags.Select(f => f.HasOrders));

        Assert.True(people.Any(p => p.Name == "Ada"));
        Assert.True(await people.AnyAsync(p => p.Age > 50));

        // Deletes the order that has no person.
        Assert.Equal(1, await orders.Where(o => !people.Any(p => p.Id == o.PersonId)).ExecuteDeleteAsync());
        Assert.Equal(5, await orders.CountAsync());
    }

    private static async Task<string[]> NamesAsync(Querying.Query<Person> query) =>
        [.. (await query.Select(p => p.Name).ToListAsync()).Order(StringComparer.Ordinal)];
}
