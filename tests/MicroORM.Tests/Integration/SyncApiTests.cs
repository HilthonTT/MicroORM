using MicroORM.Materialization;
using MicroORM.Tests.Model;

using static MicroORM.Tests.Integration.Seed;

namespace MicroORM.Tests.Integration;

/// <summary>The synchronous API mirrors the asynchronous one.</summary>
[Collection(ProviderCollectionDefinition.Name)]
public class SyncApiTests
{
    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Crud_and_queries(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        var db = database.Db;
        var people = db.Table<Person>();

        Assert.Equal(3, people.InsertMany([NewPerson("Ada", 36), NewPerson("Bob", 20), NewPerson("Cleo", 52)]));
        var dmitri = NewPerson("Dmitri", 29);
        people.Insert(dmitri);
        Assert.True(dmitri.Id > 0);

        dmitri.Age = 30;
        Assert.True(people.Update(dmitri));
        Assert.Equal(30, people.Find(dmitri.Id)!.Age);

        Assert.Equal(4, people.Count());
        Assert.True(people.Where(p => p.Age > 50).Any());
        Assert.Equal("Cleo", people.OrderByDescending(p => p.Age).FirstOrDefault()!.Name);
        Assert.Equal("Bob", people.Where(p => p.Age < 25).Single().Name);
        Assert.Equal(["Ada", "Bob", "Cleo", "Dmitri"], people.OrderBy(p => p.Name).Select(p => p.Name).ToList());
        Assert.Equal(138, people.Sum(p => p.Age));
        Assert.Equal(52, people.Max(p => p.Age));
        Assert.Equal(34.5, people.Average(p => p.Age));
        Assert.Equal(["Bob", "Dmitri"], people.Where(p => p.Age < 35).OrderBy(p => p.Age).Stream().Select(p => p.Name).ToList());

        Assert.Equal(1, people.Where(p => p.Name == "Bob").ExecuteUpdate(s => s.SetFrom(p => p.Age, p => p.Age + 1)));
        Assert.Equal(21, db.Scalar<int>($"SELECT age FROM micro_people WHERE name = {"Bob"}"));
        Assert.Equal(4, db.Query<PersonSummary>($"SELECT id, name, age FROM micro_people").Count);
        Assert.Equal("Ada", db.QueryFirstOrDefault<string>($"SELECT name FROM micro_people ORDER BY name"));
        Assert.Equal(1, db.Execute($"DELETE FROM micro_people WHERE name = {"Bob"}"));

        Assert.True(people.Delete(dmitri));
        Assert.Equal(1, people.Where(p => p.Age > 50).ExecuteDelete());
        Assert.Equal(1, people.Count());
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Transactions_multi_mapping_result_sets_and_upsert(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var db = database.Db;

        Assert.Throws<InvalidOperationException>(() => db.Transaction(tx =>
        {
            tx.Table<Person>().Insert(NewPerson("Ghost", 99));
            throw new InvalidOperationException("boom");
        }));
        Assert.Equal(5, db.Table<Person>().Count());

        var id = db.Transaction(tx =>
        {
            var person = NewPerson("Keeper", 60);
            tx.Table<Person>().Insert(person);
            return person.Id;
        });
        Assert.NotNull(db.Table<Person>().Find(id));

        using (var tx = db.BeginTransaction())
        {
            tx.Execute($"DELETE FROM micro_people");
            tx.Rollback();
        }

        Assert.Equal(6, db.Table<Person>().Count());

        var pairs = db.Query<PersonSummary, Row, string>(
            $"SELECT id, name, age, email FROM micro_people WHERE email IS NOT NULL ORDER BY name",
            (person, row) => $"{person.Name}:{row["email"]}",
            splitOn: "email");
        Assert.Equal(["Ada:ada@example.com", "Cleo:cleo@example.com"], pairs);

        using (var results = db.QueryMultiple($"SELECT COUNT(*) FROM micro_people; SELECT name FROM micro_people WHERE age = {17}"))
        {
            Assert.Equal(6, results.ReadSingle<long>());
            Assert.Equal("Brendan", results.ReadFirstOrDefault<string>());
        }

        var line = new OrderLine { OrderId = 1, LineNo = 1, Product = "Warp", Quantity = 1 };
        db.Table<OrderLine>().Upsert(line);
        line.Quantity = 4;
        db.Table<OrderLine>().Upsert(line);
        Assert.Equal(4, db.Table<OrderLine>().Find((1, 1))!.Quantity);
    }
}
