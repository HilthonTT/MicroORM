using MicroORM.Tests.Model;

using static MicroORM.Tests.Integration.Seed;

namespace MicroORM.Tests.Integration;

[Collection(ProviderCollectionDefinition.Name)]
public class GroupingTests
{
    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Group_by_a_column_selects_aggregates(string provider)
    {
        await using var database = await PeopleAsync(provider);
        await OrdersAsync(database);
        var orders = database.Db.Table<Order>();

        var groups = (await orders.GroupBy(o => o.PersonId)
            .Select(g => new { PersonId = g.Key, Count = g.Count(), Total = g.Sum(o => o.Amount), Largest = g.Max(o => o.Amount), Average = g.Average(o => o.Amount), Small = g.Count(o => o.Amount < 20) })
            .ToListAsync()).OrderBy(g => g.Total).ToList();

        Assert.Equal([5, 40, 65, 99], groups.Select(g => g.Total));
        Assert.Equal([1, 1, 3, 1], groups.Select(g => g.Count));
        Assert.Equal([5, 40, 30, 99], groups.Select(g => g.Largest));
        Assert.Equal([1, 0, 1, 0], groups.Select(g => g.Small));
        Assert.Equal(65 / 3.0, groups[2].Average, 6);
        Assert.Null(groups[3].PersonId);

        Assert.Equal(4, await orders.GroupBy(o => o.PersonId).CountAsync());
        Assert.Equal(1, await orders.GroupBy(o => o.PersonId).Having(g => g.Count() > 1).CountAsync());
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Groups_are_filtered_sorted_and_paged(string provider)
    {
        await using var database = await PeopleAsync(provider);
        await OrdersAsync(database);
        var ada = (await database.Db.Table<Person>().FirstOrDefaultAsync(p => p.Name == "Ada"))!;

        var biggest = await database.Db.Table<Order>().Where(o => o.PersonId != null)
            .GroupBy(o => o.PersonId)
            .Having(g => g.Sum(o => o.Amount) > 10)
            .OrderByDescending(g => g.Sum(o => o.Amount))
            .Take(1)
            .Select(g => g.Key)
            .SingleAsync();
        Assert.Equal(ada.Id, biggest);

        var people = database.Db.Table<Person>();
        Assert.Equal(Mood.Happy, await people.GroupBy(p => p.Mood).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefaultAsync());
        Assert.Empty(await people.GroupBy(p => p.Mood).Take(0).Select(g => g.Key).ToListAsync());
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Composite_and_computed_keys(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var people = database.Db.Table<Person>();

        var byMood = await people.GroupBy(p => new { p.Mood, p.Active }).OrderBy(g => g.Key)
            .Select(g => new { g.Key.Mood, g.Key.Active, Count = g.Count() })
            .ToListAsync();
        Assert.Equal(
            [(Mood.Calm, false, 1), (Mood.Calm, true, 1), (Mood.Happy, true, 2), (Mood.Grumpy, true, 1)],
            byMood.Select(g => (g.Mood, g.Active, g.Count)));

        // Ages 17, 29, 36, 41 and 52.
        var byParity = (await people.GroupBy(p => p.Age % 2).Select(g => new { Parity = g.Key, Count = g.Count(), Total = g.Sum(p => p.Age) }).ToListAsync())
            .OrderBy(g => g.Parity);
        Assert.Equal([(0, 2, 88), (1, 3, 87)], byParity.Select(g => (g.Parity, g.Count, g.Total)));
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task A_paged_source_is_grouped_after_paging(string provider)
    {
        await using var database = await PeopleAsync(provider);

        // The three youngest: Brendan (inactive) 17, Dmitri 29, Ada 36.
        var groups = (await database.Db.Table<Person>().OrderBy(p => p.Age).Take(3)
            .GroupBy(p => p.Active)
            .Select(g => new { Active = g.Key, Count = g.Count() })
            .ToListAsync()).OrderBy(g => g.Active);
        Assert.Equal([(false, 1), (true, 2)], groups.Select(g => (g.Active, g.Count)));
    }
}
