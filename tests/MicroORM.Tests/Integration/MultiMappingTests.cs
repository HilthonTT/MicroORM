using MicroORM.Materialization;
using MicroORM.Tests.Model;

using static MicroORM.Tests.Integration.Seed;

namespace MicroORM.Tests.Integration;

[Collection(ProviderCollectionDefinition.Name)]
public class MultiMappingTests
{
    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Joins_split_into_objects_and_unmatched_left_joins_are_null(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var db = database.Db;
        var ada = (await db.Table<Person>().FirstOrDefaultAsync(p => p.Name == "Ada"))!;
        await db.Table<Order>().InsertManyAsync(
        [
            new Order { PersonId = ada.Id, Amount = 10 },
            new Order { PersonId = ada.Id, Amount = 25 },
            new Order { PersonId = null, Amount = 99 },
        ]);

        var rows = await db.QueryAsync<Order, Person, (Order Order, Person? Person)>($"""
            SELECT o.id, o.person_id, o.amount, p.id, p.name, p.age
            FROM micro_orders o LEFT JOIN micro_people p ON p.id = o.person_id
            ORDER BY o.amount
            """,
            (order, person) => (order, person));

        Assert.Equal([10, 25, 99], rows.Select(r => r.Order.Amount));
        Assert.Equal("Ada", rows[0].Person?.Name);
        Assert.Equal(36, rows[1].Person?.Age);
        Assert.Null(rows[2].Person);
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Three_way_split_with_named_split_columns(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var db = database.Db;

        var rows = await db.QueryAsync<PersonSummary, Row, int, string>($"""
            SELECT id, name, age, email AS contact, 1 AS marker, age * 2 AS doubled
            FROM micro_people WHERE name = {"Cleo"}
            """,
            (person, contact, doubled) => $"{person.Name}|{contact["contact"]}|{doubled}",
            splitOn: "contact,doubled");

        Assert.Equal("Cleo|cleo@example.com|104", Assert.Single(rows));
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Missing_split_columns_explain_the_problem(string provider)
    {
        await using var database = await PeopleAsync(provider);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Db.QueryAsync<Person, Person, Person>($"SELECT id, name FROM micro_people", (a, _) => a, splitOn: "nope"));
        Assert.Contains("'nope'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Multiple_result_sets_are_read_in_order(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var minimum = 30;

        await using (var results = await database.Db.QueryMultipleAsync($"""
            SELECT COUNT(*) FROM micro_people;
            SELECT id, name, age FROM micro_people WHERE age > {minimum} ORDER BY age;
            SELECT name FROM micro_people WHERE id = {-1}
            """))
        {
            Assert.Equal(5, await results.ReadSingleAsync<long>());
            Assert.Equal(["Ada", "50% Eve_", "Cleo"], (await results.ReadAsync<PersonSummary>()).Select(p => p.Name));
            Assert.Null(await results.ReadFirstOrDefaultAsync<string>());
            await Assert.ThrowsAsync<InvalidOperationException>(() => results.ReadAsync<int>());
        }

        // The connection was returned: the database is still usable and the command was logged once.
        Assert.Equal(5, await database.Db.Table<Person>().CountAsync());
        Assert.Contains(database.Log, entry => entry.Sql.Contains("SELECT COUNT(*) FROM micro_people;", StringComparison.Ordinal));
    }
}
