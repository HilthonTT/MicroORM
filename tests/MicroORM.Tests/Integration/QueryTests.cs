using MicroORM.Querying;
using MicroORM.Tests.Model;

using static MicroORM.Tests.Integration.Seed;

namespace MicroORM.Tests.Integration;

[Collection(ProviderCollectionDefinition.Name)]
public class QueryTests
{
    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Predicates_translate_with_the_same_results_everywhere(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var people = database.Db.Table<Person>();

        static async Task<string[]> Names(Query<Person> query) => [.. (await query.OrderBy(p => p.Name).ToListAsync()).Select(p => p.Name)];

        var adultAge = 18;
        var names = new[] { "Ada", "Cleo", "Nobody" };
        Assert.Equal(["50% Eve_", "Ada", "Cleo", "Dmitri"], await Names(people.Where(p => p.Age >= adultAge)));
        Assert.Equal(["Brendan"], await Names(people.Where(p => !p.Active)));
        Assert.Equal(["Ada", "Dmitri"], await Names(people.Where(p => p.Mood == Mood.Happy)));
        Assert.Equal(["Ada", "Cleo"], await Names(people.Where(p => names.Contains(p.Name))));
        Assert.Equal(["Ada", "Cleo"], await Names(people.Where(p => p.Email != null)));
        Assert.Equal(["50% Eve_"], await Names(people.Where(p => p.Name.StartsWith("50%"))));
        Assert.Equal(["50% Eve_"], await Names(people.Where(p => p.Name.EndsWith('_'))));
        Assert.Equal(["Brendan", "Dmitri"], await Names(people.Where(p => p.Name.Contains("en") || p.Name.Contains("mit"))));
        Assert.Equal(["Cleo"], await Names(people.Where(p => p.Age + 10 > 60)));
        Assert.Equal(["Brendan", "Dmitri"], await Names(people.Where(p => p.Name.Length > 5 && !p.Name.Contains("%"))));
        Assert.Equal(["Ada"], await Names(people.Where(p => p.Name.ToUpper() == "ADA")));
        Assert.Empty(await Names(people.Where(p => Array.Empty<int>().Contains(p.Age))));
        Assert.Equal(["Cleo"], await Names(people.Where($"age > {50}")));
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Functions_translate_with_the_same_results_everywhere(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var people = database.Db.Table<Person>();

        static async Task<string[]> Names(Query<Person> query) => [.. (await query.OrderBy(p => p.Name).ToListAsync()).Select(p => p.Name)];

        Assert.Equal(["Ada", "Dmitri"], await Names(people.Where(p => p.Name.Substring(1, 1) == "d" || p.Name.Substring(2) == "itri")));
        Assert.Equal(["50% Eve_"], await Names(people.Where(p => p.Name.Replace("% ", "-") == "50-Eve_")));
        Assert.Equal(["50% Eve_", "Brendan", "Dmitri"], await Names(people.Where(p => string.IsNullOrWhiteSpace(p.Email))));
        Assert.Equal(["Ada"], await Names(people.Where(p => SqlFunctions.Like(p.Email, "a%@example.___"))));
        Assert.Equal(["Ada", "Dmitri"], await Names(people.Where(p => Math.Abs(p.Age - 32) <= 4)));
        Assert.Equal(["Ada"], await Names(people.Where(p => Math.Floor(p.Age / 10.0) == 3 && Math.Ceiling(p.Age / 10.0) == 4)));

        // Seed.Now is 2026-10-16 08:30:15.
        Assert.Equal(5, await people.Where(p =>
            p.CreatedAt.Year == 2026 && p.CreatedAt.Month == 10 && p.CreatedAt.Day == 16 &&
            p.CreatedAt.Hour == 8 && p.CreatedAt.Minute == 30 && p.CreatedAt.Second == 15).CountAsync());
        Assert.Equal([2026], (await people.Select(p => p.CreatedAt.Year).ToListAsync()).Distinct());
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Filters_and_limits_behave_like_linq(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var people = database.Db.Table<Person>();

        static async Task<string[]> Names(Query<Person> query) => [.. (await query.OrderBy(p => p.Name).ToListAsync()).Select(p => p.Name)];

        Assert.Equal(["Ada"], await Names(people.Where(p => p.Name == "Ada").Where($"age > {50} OR age < {100}")));
        var enabled = true;
        Assert.Equal(["Ada"], await Names(people.Where(p => p.Name == "Ada" && enabled != false)));
        string?[] noEmail = [null];
        Assert.Equal(["50% Eve_", "Brendan", "Dmitri"], await Names(people.Where(p => noEmail.Contains(p.Email))));

        var none = people.OrderBy(p => p.Name).Take(0);
        Assert.Empty(await none.ToListAsync());
        Assert.Null(await none.FirstOrDefaultAsync());
        Assert.False(await none.AnyAsync());
        Assert.Equal(0, await none.CountAsync());
        Assert.Equal("50% Eve_", (await people.OrderBy(p => p.Name).Take(1).SingleAsync()).Name);
        Assert.Equal("50% Eve_", await people.OrderBy(p => p.Name).Take(1).Select(p => p.Name).SingleAsync());
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Ordering_paging_counting(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var people = database.Db.Table<Person>();

        var page = await people.OrderByDescending(p => p.Age).ThenBy(p => p.Name).Skip(1).Take(2).ToListAsync();
        Assert.Equal(["50% Eve_", "Ada"], page.Select(p => p.Name));

        var skipOnly = await people.OrderBy(p => p.Age).Skip(4).ToListAsync();
        Assert.Equal("Cleo", Assert.Single(skipOnly).Name);

        Assert.Equal(5, await people.CountAsync());
        Assert.Equal(4, await people.Where(p => p.Active).CountAsync());
        Assert.Equal(2, await people.OrderBy(p => p.Id).Skip(3).Take(10).CountAsync());
        Assert.True(await people.Where(p => p.Age > 50).AnyAsync());
        Assert.False(await people.Where(p => p.Age > 100).AnyAsync());

        var single = await people.Where(p => p.Name == "Ada").SingleAsync();
        Assert.Equal(36, single.Age);
        await Assert.ThrowsAsync<InvalidOperationException>(() => people.Where(p => p.Active).SingleAsync());
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Set_based_update_and_delete(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var people = database.Db.Table<Person>();

        var updated = await people.Where(p => p.Mood == Mood.Happy)
            .ExecuteUpdateAsync(s => s.SetFrom(p => p.Age, p => p.Age + 1).Set(p => p.Email, null).Set(p => p.Mood, Mood.Calm));
        Assert.Equal(2, updated);

        var ada = (await people.FirstOrDefaultAsync(p => p.Name == "Ada"))!;
        Assert.Equal(37, ada.Age);
        Assert.Null(ada.Email);
        Assert.Equal(Mood.Calm, ada.Mood);

        Assert.Equal(1, await people.Where(p => p.Age < 18).ExecuteDeleteAsync());
        Assert.Equal(4, await people.CountAsync());
    }
}
