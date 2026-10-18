using MicroORM.Tests.Model;

using static MicroORM.Tests.Integration.Seed;

namespace MicroORM.Tests.Integration;

[Collection(ProviderCollectionDefinition.Name)]
public class QueryFilterTests
{
    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Query_filters_hide_rows_from_reads_and_set_based_writes(string provider)
    {
        // Brendan is the only inactive person.
        await using var database = await PeopleAsync(provider, o => o.Entity<Person>(e => e.HasQueryFilter(p => p.Active)));
        var people = database.Db.Table<Person>();
        var brendan = (await people.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Name == "Brendan"))!;

        Assert.Equal(4, await people.CountAsync());
        Assert.Equal(5, await people.IgnoreQueryFilters().CountAsync());
        Assert.Null(await people.FindAsync(brendan.Id));
        Assert.False(await people.Where(p => p.Name == "Brendan").AnyAsync());
        Assert.Equal(29, await people.MinAsync(p => p.Age));
        Assert.Equal(17, await people.IgnoreQueryFilters().MinAsync(p => p.Age));

        Assert.Equal(4, await people.ExecuteUpdateAsync(s => s.SetFrom(p => p.Age, p => p.Age + 1)));
        Assert.Equal(17, (await people.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == brendan.Id))!.Age);
        Assert.Equal(4, await people.ExecuteDeleteAsync());
        Assert.Equal(["Brendan"], await people.IgnoreQueryFilters().Select(p => p.Name).ToListAsync());

        // Single-entity writes act on the key they're given.
        brendan.Age = 18;
        Assert.True(await people.UpdateAsync(brendan));
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Captured_filter_values_are_read_for_every_statement(string provider)
    {
        var scope = new AgeScope { Minimum = 30 };
        await using var database = await PeopleAsync(provider, o => o.Entity<Person>(e => e.HasQueryFilter(p => p.Age >= scope.Minimum)));
        var people = database.Db.Table<Person>();

        Assert.Equal(3, await people.CountAsync());
        scope.Minimum = 40;
        Assert.Equal(2, await people.CountAsync());
        Assert.Equal(["50% Eve_", "Cleo"], await people.OrderBy(p => p.Age).Select(p => p.Name).ToListAsync());
    }

    private sealed class AgeScope
    {
        public int Minimum { get; set; }
    }
}
