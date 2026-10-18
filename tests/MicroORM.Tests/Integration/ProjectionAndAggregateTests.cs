using MicroORM.Tests.Model;

using static MicroORM.Tests.Integration.Seed;

namespace MicroORM.Tests.Integration;

[Collection(ProviderCollectionDefinition.Name)]
public class ProjectionAndAggregateTests
{
    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Select_projects_anonymous_types_records_dtos_and_scalars(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var people = database.Db.Table<Person>().Where(p => p.Age > 30).OrderBy(p => p.Age);

        var anonymous = await people.Select(p => new { p.Name, NextAge = p.Age + 1, Senior = p.Age >= 50 }).ToListAsync();
        Assert.Equal(["Ada", "50% Eve_", "Cleo"], anonymous.Select(a => a.Name));
        Assert.Equal([37, 42, 53], anonymous.Select(a => a.NextAge));
        Assert.Equal([false, false, true], anonymous.Select(a => a.Senior));

        var records = await people.Select(p => new PersonSummary(p.Id, p.Name, p.Age)).ToListAsync();
        Assert.Equal([36, 41, 52], records.Select(r => r.Age));

        var dtos = await people.Select(p => new NameDto { Id = p.Id, Upper = p.Name.ToUpper() }).ToListAsync();
        Assert.Equal("ADA", dtos[0].Upper);
        Assert.Equal(records[0].Id, dtos[0].Id);

        Assert.Equal(["Ada", "50% Eve_", "Cleo"], await people.Select(p => p.Name).ToListAsync());
        Assert.Equal("Ada", await people.Select(p => p.Name).FirstOrDefaultAsync());
        Assert.Equal(52, await database.Db.Table<Person>().Where(p => p.Name == "Cleo").Select(p => p.Age).SingleAsync());
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Aggregates(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var people = database.Db.Table<Person>();

        Assert.Equal(175, await people.SumAsync(p => p.Age));
        Assert.Equal(17, await people.MinAsync(p => p.Age));
        Assert.Equal(52, await people.MaxAsync(p => p.Age));
        Assert.Equal(35.0, await people.AverageAsync(p => p.Age));
        Assert.Equal("Dmitri", await people.MaxAsync(p => p.Name));
        Assert.Equal(154, await people.Where(p => p.Active).SumAsync(p => p.Age - 1));

        // Aggregates over a page use a subquery.
        Assert.Equal(46, await people.OrderBy(p => p.Age).Take(2).SumAsync(p => p.Age));

        // Empty sets: SUM is zero, MIN/MAX/AVG are null for nullable selectors.
        var nobody = people.Where(p => p.Age > 100);
        Assert.Equal(0, await nobody.SumAsync(p => p.Age));
        Assert.Null(await nobody.MaxAsync(p => (int?)p.Age));
        Assert.Null(await nobody.AverageAsync(p => p.Age));
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Distinct_projections(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var people = database.Db.Table<Person>();

        Assert.Equal([Mood.Calm, Mood.Happy, Mood.Grumpy], (await people.Select(p => p.Mood).Distinct().ToListAsync()).Order());
        Assert.Equal(3, await people.Select(p => p.Mood).Distinct().CountAsync());
        Assert.Equal(5, await people.Select(p => p.Mood).CountAsync());

        // An ordering before Distinct is dropped rather than rejected by the database.
        Assert.Equal(3, (await people.OrderBy(p => p.Name).Select(p => p.Mood).Distinct().ToListAsync()).Count);

        // Paging applies first, as in LINQ: the three youngest have two moods.
        Assert.Equal(2, await people.OrderBy(p => p.Age).Take(3).Select(p => p.Mood).Distinct().CountAsync());

        // Single applies to the distinct values: under 40, people are active and inactive.
        await Assert.ThrowsAsync<InvalidOperationException>(() => people.Where(p => p.Age < 40).Select(p => p.Active).Distinct().SingleAsync());
        Assert.True(await people.Where(p => p.Mood == Mood.Happy).Select(p => p.Active).Distinct().SingleAsync());
        Assert.Equal("Ada", people.Where(p => p.Name == "Ada").Select(p => p.Name).Distinct().FirstOrDefault());
    }
}
