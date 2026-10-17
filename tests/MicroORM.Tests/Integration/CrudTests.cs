using MicroORM.Materialization;
using MicroORM.Tests.Model;

using static MicroORM.Tests.Integration.Seed;

namespace MicroORM.Tests.Integration;

[Collection(ProviderCollectionDefinition.Name)]
public class CrudTests
{
    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Insert_assigns_the_generated_key_and_round_trips_every_column(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        var people = database.Db.Table<Person>();
        var external = Guid.NewGuid();
        var person = NewPerson("Ada", 36, mood: Mood.Grumpy, email: "ada@example.com");
        person.ExternalId = external;

        await people.InsertAsync(person);
        var second = NewPerson("Bob", 40);
        await people.InsertAsync(second);

        Assert.True(person.Id > 0);
        Assert.Equal(person.Id + 1, second.Id);

        var loaded = await people.FindAsync(person.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Ada", loaded.Name);
        Assert.Equal("ada@example.com", loaded.Email);
        Assert.Equal(36, loaded.Age);
        Assert.True(loaded.Active);
        Assert.Equal(Mood.Grumpy, loaded.Mood);
        Assert.Equal(Now, loaded.CreatedAt);
        Assert.Equal(external, loaded.ExternalId);
        Assert.Null((await people.FindAsync(second.Id))!.ExternalId);
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Update_and_delete_by_key(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var people = database.Db.Table<Person>();
        var cleo = (await people.FirstOrDefaultAsync(p => p.Name == "Cleo"))!;

        cleo.Email = null;
        cleo.Age = 53;
        Assert.True(await people.UpdateAsync(cleo));

        var reloaded = (await people.FindAsync(cleo.Id))!;
        Assert.Null(reloaded.Email);
        Assert.Equal(53, reloaded.Age);

        Assert.True(await people.DeleteAsync(cleo));
        Assert.False(await people.DeleteAsync(cleo));
        Assert.Null(await people.FindAsync(cleo.Id));
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Application_generated_keys(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        var tags = database.Db.Table<Tag>();
        var tag = new Tag { Code = Guid.NewGuid(), Label = "urgent" };

        await tags.InsertAsync(tag);
        tag.Label = "critical";
        Assert.True(await tags.UpdateAsync(tag));

        var loaded = await tags.FindAsync(tag.Code);
        Assert.Equal("critical", loaded?.Label);
        Assert.True(await tags.DeleteByKeyAsync(tag.Code));
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Raw_sql_materializes_records_rows_and_scalars(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var db = database.Db;
        var minimum = 30;

        var summaries = await db.QueryAsync<PersonSummary>($"SELECT id, name, age FROM micro_people WHERE age > {minimum} ORDER BY age");
        Assert.Equal(["Ada", "50% Eve_", "Cleo"], summaries.Select(s => s.Name));

        var rows = await db.QueryAsync<Row>($"SELECT name, email FROM micro_people WHERE email IS NOT NULL ORDER BY name");
        Assert.Equal("ada@example.com", rows[0]["EMAIL"]);
        dynamic first = rows[0];
        Assert.Equal("Ada", (string)first.name);

        var ages = await db.QueryAsync<int>($"SELECT age FROM micro_people ORDER BY age");
        Assert.Equal([17, 29, 36, 41, 52], ages);
        Assert.Equal(175, await db.ScalarAsync<long>($"SELECT SUM(age) FROM micro_people"));
        Assert.Null(await db.QueryFirstOrDefaultAsync<PersonSummary>($"SELECT id, name, age FROM micro_people WHERE age > {999}"));

        var streamed = new List<string>();
        await foreach (var person in db.Table<Person>().Where(p => p.Age < 30).OrderBy(p => p.Age).StreamAsync())
        {
            streamed.Add(person.Name);
        }

        Assert.Equal(["Brendan", "Dmitri"], streamed);
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Transactions_commit_or_roll_back(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var db = database.Db;

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.TransactionAsync(async tx =>
        {
            await tx.Table<Person>().InsertAsync(NewPerson("Ghost", 99));
            Assert.Equal(6, await tx.Table<Person>().CountAsync());
            throw new InvalidOperationException("boom");
        }));
        Assert.Equal(5, await db.Table<Person>().CountAsync());

        var id = await db.TransactionAsync(async tx =>
        {
            var person = NewPerson("Keeper", 60);
            await tx.Table<Person>().InsertAsync(person);
            return person.Id;
        });
        Assert.NotNull(await db.Table<Person>().FindAsync(id));

        await using (var tx = await db.BeginTransactionAsync())
        {
            await tx.ExecuteAsync($"DELETE FROM micro_people");
            await tx.RollbackAsync();
        }

        Assert.Equal(6, await db.Table<Person>().CountAsync());
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Commands_are_logged_with_parameters(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);

        await database.Db.Table<Person>().Where(p => p.Age > 3).CountAsync();

        var entry = Assert.Single(database.Log);
        Assert.Contains("COUNT(*)", entry.Sql, StringComparison.Ordinal);
        Assert.Equal(3, Assert.Single(entry.Parameters).Value);
        Assert.Null(entry.Error);

        // Readers abandoned early (FirstOrDefault stops after one row) are logged too.
        await database.Db.Table<Person>().FirstOrDefaultAsync();
        Assert.Equal(2, database.Log.Count);

        await Assert.ThrowsAnyAsync<Exception>(() => database.Db.ExecuteAsync($"SELECT * FROM no_such_table"));
        Assert.NotNull(database.Log[^1].Error);
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Enums_are_sent_as_numbers(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        Assert.Equal(5, await database.Db.ScalarAsync<int>($"SELECT {DayOfWeek.Friday}"));
    }
}
