using MicroORM.Materialization;
using MicroORM.Tests.Model;
using System.ComponentModel.DataAnnotations.Schema;
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
    public async Task Failed_streams_and_result_sets_are_logged_with_their_error(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await foreach (var _ in database.Db.StreamAsync<int>($"SELECT 'not a number'"))
            {
                // Enumerating is what reads the bad value.
            }
        });
        Assert.NotNull(Assert.Single(database.Log).Error);

        Assert.ThrowsAny<Exception>(() => database.Db.Stream<int>($"SELECT 'not a number'").ToList());
        Assert.NotNull(database.Log[^1].Error);

        await using (var results = await database.Db.QueryMultipleAsync($"SELECT 'not a number'"))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => results.ReadAsync<int>());
        }

        Assert.Equal(3, database.Log.Count);
        Assert.NotNull(database.Log[^1].Error);
    }

    [Fact]
    public async Task Upsert_of_an_existing_key_only_entity_does_not_insert_a_copy()
    {
        await using var database = await TestDatabase.CreateAsync("sqlite");
        await database.Db.ExecuteAsync($"CREATE TABLE micro_counters (id INTEGER PRIMARY KEY AUTOINCREMENT)");
        var counters = database.Db.Table<Counter>();
        var counter = new Counter();
        await counters.InsertAsync(counter);
        var id = counter.Id;

        await counters.UpsertAsync(counter);

        Assert.Equal(id, counter.Id);
        Assert.Equal(1, await counters.CountAsync());
    }

    [Fact]
    public async Task Structs_and_camel_case_constructors_materialize()
    {
        await using var database = await TestDatabase.CreateAsync("sqlite");

        var point = Assert.Single(await database.Db.QueryAsync<PointStruct>($"SELECT 1 AS x, 2 AS y"));
        Assert.Equal((1, 2), (point.X, point.Y));

        // Parameter "label" binds to property Label, mapped to column "tag_label".
        var labelled = Assert.Single(await database.Db.QueryAsync<Labelled>($"SELECT 7 AS id, 'a' AS tag_label"));
        Assert.Equal((7, "a"), (labelled.Id, labelled.Label));

        // An exact column name wins over an earlier one that only matches without underscores.
        var exact = Assert.Single(await database.Db.QueryAsync<NameDto>($"SELECT 1 AS id, 'wrong' AS up_per, 'right' AS upper"));
        Assert.Equal("right", exact.Upper);

        // Value tuples bind by position.
        var tuple = Assert.Single(await database.Db.QueryAsync<(long Id, string Name)>($"SELECT 7 AS a, 'bob' AS b"));
        Assert.Equal((7L, "bob"), tuple);

        // A struct nothing binds to would otherwise come back as default.
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Db.QueryAsync<PointStruct>($"SELECT 1 AS a"));
    }

    [Fact]
    public async Task Ignored_properties_are_not_filled_from_results()
    {
        await using var database = await TestDatabase.CreateAsync("sqlite", o => o.Entity<Secretive>(e => e.Ignore(s => s.Secret)));

        var secretive = Assert.Single(await database.Db.QueryAsync<Secretive>($"SELECT 1 AS id, 'x' AS secret"));
        Assert.Equal((1, null), (secretive.Id, secretive.Secret));
    }

    public sealed class Secretive
    {
        public int Id { get; set; }

        public string? Secret { get; set; }
    }

    [Table("micro_counters")]
    public sealed class Counter
    {
        public int Id { get; set; }
    }

    public struct PointStruct
    {
        public int X { get; set; }

        public int Y { get; set; }
    }

    public sealed class Labelled(int id, string label)
    {
        public int Id { get; } = id;

        [Column("tag_label")]
        public string Label { get; } = label;
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Enums_are_sent_as_numbers(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        Assert.Equal(5, await database.Db.ScalarAsync<int>($"SELECT {DayOfWeek.Friday}"));
    }
}
