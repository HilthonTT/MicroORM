using MicroORM.Tests.Model;

namespace MicroORM.Tests.Integration;

[Collection(ProviderCollectionDefinition.Name)]
public class TypeHandlerTests
{
    private static Customer NewCustomer(string email, string name) => new()
    {
        Email = new Email(email),
        Name = name,
        Address = new Address { Street = "1 Loom Lane", City = "Ghent" },
        Tags = ["vip", "early"],
        Tier = CustomerTier.Gold,
    };

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Value_objects_json_and_text_enums_round_trip(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        var customers = database.Db.Table<Customer>();
        var customer = NewCustomer("ada@example.com", "Ada");

        await customers.InsertAsync(customer);
        var loaded = (await customers.FindAsync(customer.Id))!;

        Assert.Equal(new Email("ada@example.com"), loaded.Email);
        Assert.Equal("Ghent", loaded.Address?.City);
        Assert.Equal(["vip", "early"], loaded.Tags);
        Assert.Equal(CustomerTier.Gold, loaded.Tier);

        // The handler's provider value is what reaches the database.
        Assert.Equal("Gold", await database.Db.ScalarAsync<string>($"SELECT tier FROM micro_customers"));
        Assert.Equal("ada@example.com", await database.Db.ScalarAsync<string>($"SELECT email FROM micro_customers"));
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Handled_values_work_in_predicates_updates_and_nulls(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        var customers = database.Db.Table<Customer>();
        await customers.InsertAsync(NewCustomer("ada@example.com", "Ada"));
        var bob = NewCustomer("bob@example.com", "Bob");
        bob.Address = null;
        bob.Tier = CustomerTier.Standard;
        await customers.InsertAsync(bob);

        var target = new Email("bob@example.com");
        var found = (await customers.FirstOrDefaultAsync(c => c.Email == target))!;
        Assert.Equal("Bob", found.Name);
        Assert.Null(found.Address);

        Assert.Equal(["Ada"], (await customers.Where(c => c.Tier == CustomerTier.Gold).ToListAsync()).Select(c => c.Name));

        await customers.Where(c => c.Email == target).ExecuteUpdateAsync(s => s.Set(c => c.Tier, CustomerTier.Gold).Set(c => c.Tags, ["upgraded"]));
        var upgraded = (await customers.FindAsync(found.Id))!;
        Assert.Equal(CustomerTier.Gold, upgraded.Tier);
        Assert.Equal(["upgraded"], upgraded.Tags);

        var emails = await customers.OrderBy(c => c.Name).Select(c => c.Email).ToListAsync();
        Assert.Equal([new Email("ada@example.com"), new Email("bob@example.com")], emails);
    }
}
