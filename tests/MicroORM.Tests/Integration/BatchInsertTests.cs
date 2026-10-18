using MicroORM.Tests.Model;
using System.Data.Common;

using static MicroORM.Tests.Integration.Seed;

namespace MicroORM.Tests.Integration;

[Collection(ProviderCollectionDefinition.Name)]
public class BatchInsertTests
{
    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Insert_many_batches_rows_and_reads_back_every_generated_key(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        var table = database.Db.Table<Person>();
        var people = Enumerable.Range(0, 300).Select(i => NewPerson($"P{i:000}", i % 90)).ToList();

        Assert.Equal(300, await table.InsertManyAsync(people));

        Assert.Equal(300, people.Select(p => p.Id).Distinct().Count());
        var loaded = (await table.ToListAsync()).ToDictionary(p => p.Id);
        Assert.All(people, p => Assert.Equal(p.Name, loaded[p.Id].Name));

        // Seven inserted columns per row; one command per batch.
        var perBatch = Math.Clamp(database.Db.Dialect.MaxParameters / 7, 1, 1000);
        Assert.Equal((300 + perBatch - 1) / perBatch, database.Log.Count(l => l.Sql.StartsWith("INSERT", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Insert_many_without_generated_keys_is_one_multi_row_insert(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        var lines = database.Db.Table<OrderLine>();

        Assert.Equal(3, await lines.InsertManyAsync(
        [
            new OrderLine { OrderId = 1, LineNo = 1, Product = "Pen", Quantity = 2 },
            new OrderLine { OrderId = 1, LineNo = 2, Product = "Ink", Quantity = 1 },
            new OrderLine { OrderId = 2, LineNo = 1, Product = "Pad", Quantity = 5 },
        ]));

        var insert = Assert.Single(database.Log);
        Assert.Contains("), (", insert.Sql, StringComparison.Ordinal);
        Assert.Equal(8, await lines.SumAsync(l => l.Quantity));
        Assert.Equal(0, await lines.InsertManyAsync([]));
        Assert.Single(database.Log, l => l.Sql.StartsWith("INSERT", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Insert_many_is_all_or_nothing(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        var table = database.Db.Table<Person>();
        var invalid = NewPerson("Nameless", 1);
        invalid.Name = null!;

        await Assert.ThrowsAnyAsync<DbException>(() => table.InsertManyAsync([NewPerson("Ada", 36), invalid, NewPerson("Cleo", 52)]));
        Assert.Equal(0, await table.CountAsync());
        Assert.Throws<ArgumentException>(() => table.InsertMany([NewPerson("Ada", 36), null!]));
    }
}
