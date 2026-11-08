using MicroORM.Querying;
using MicroORM.Tests.Model;

using static MicroORM.Tests.Integration.Seed;

namespace MicroORM.Tests.Integration;

/// <summary>The typed query API's newer operators and translations, against each database.</summary>
[Collection(ProviderCollectionDefinition.Name)]
public class QueryApiTests
{
    private static async Task<string[]> NamesAsync(Query<Person> query) => [.. (await query.OrderBy(p => p.Name).ToListAsync()).Select(p => p.Name)];

    // ---- Translation ---------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Conditionals_casts_and_math(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var people = database.Db.Table<Person>();

        Assert.Equal(["50% Eve_", "Ada", "Cleo"], await NamesAsync(people.Where(p => (p.Active ? p.Age : 0) > 30)));
        Assert.Equal(["Ada", "Brendan"], await NamesAsync(people.Where(p => p.Age > 30 ? p.Mood == Mood.Happy : !p.Active)));
        Assert.Equal(
            ["none", "ada@example.com", "none", "cleo@example.com", "none"],
            (await people.OrderBy(p => p.Name).Select(p => p.Email == null ? "none" : p.Email).ToListAsync()).ToArray());
        var flags = await people.OrderBy(p => p.Age).Select(p => new { p.Name, Flag = p.Age > 30 ? p.Active : p.Email != null }).ToListAsync();
        Assert.Equal([false, false, true, true, true], flags.Select(f => f.Flag));

        // A cast truncates toward zero, as in C# (29 / 10.0 would round to 3).
        Assert.Equal(["Dmitri"], await NamesAsync(people.Where(p => (int)(p.Age / 10.0) == 2)));
        Assert.Equal(new[] { 1L, 2L, 3L, 3L, 4L }, (await people.OrderBy(p => p.Age).Select(p => (long)(p.Age / 10.5)).ToListAsync()).ToArray());

        Assert.Equal(["Cleo"], await NamesAsync(people.Where(p => Math.Pow(p.Age, 2) > 2000)));
        Assert.Equal(["Ada"], await NamesAsync(people.Where(p => Math.Sqrt(p.Age) == 6)));
        Assert.Equal(new[] { 30, 30, 36, 41, 52 }, (await people.OrderBy(p => p.Age).Select(p => Math.Max(p.Age, 30)).ToListAsync()).ToArray());
        Assert.Equal(new[] { 17, 29, 30, 30, 30 }, (await people.OrderBy(p => p.Age).Select(p => Math.Min(30, p.Age)).ToListAsync()).ToArray());
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Strings_search_pad_compare_and_format(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var people = database.Db.Table<Person>();
        var ada = people.Where(p => p.Name == "Ada");

        Assert.Equal(["Brendan", "Cleo"], await NamesAsync(people.Where(p => p.Name.IndexOf("e") == 2)));
        Assert.Equal(5, await people.Where(p => p.Name.IndexOf("") == 0 && p.Name.IndexOf("zz") == -1).CountAsync());
        Assert.Equal(1, await ada.Select(p => p.Name.IndexOf('d')).SingleAsync());

        Assert.Equal("**Ada", await ada.Select(p => p.Name.PadLeft(5, '*')).SingleAsync());
        Assert.Equal("Ada..", await ada.Select(p => p.Name.PadRight(5, '.')).SingleAsync());
        Assert.Equal("  Ada", await ada.Select(p => p.Name.PadLeft(5)).SingleAsync());
        Assert.Equal("Ada", await ada.Select(p => p.Name.PadLeft(2)).SingleAsync());

        Assert.Equal(["50% Eve_", "Ada", "Brendan"], await NamesAsync(people.Where(p => string.Compare(p.Name, "C") < 0)));
        Assert.Equal(["Cleo", "Dmitri"], await NamesAsync(people.Where(p => p.Name.CompareTo("Cleo") >= 0)));

        // A null email sorts first, as in C#.
        Assert.Equal(["50% Eve_", "Ada", "Brendan", "Dmitri"], await NamesAsync(people.Where(p => string.Compare(p.Email, "b") < 0)));

        Assert.Equal("Ada (36)", await ada.Select(p => $"{p.Name} ({p.Age})").SingleAsync());
        Assert.Equal("Brendan:", await people.Where(p => p.Name == "Brendan").Select(p => string.Concat(p.Name, ":", p.Email)).SingleAsync());
        Assert.Equal("Ada{36}", await ada.Select(p => string.Format("{0}{{{1}}}", p.Name, p.Age)).SingleAsync());
        Assert.Equal(["Ada"], await NamesAsync(people.Where(p => p.Age.ToString() == "36")));
    }

    [Theory]
    [MemberData(nameof(TestDatabase.Providers), MemberType = typeof(TestDatabase))]
    public async Task Date_arithmetic(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var people = database.Db.Table<Person>();

        // Seed.Now is 2026-10-16 08:30:15 UTC.
        Assert.Equal(5, await people.Where(p => p.CreatedAt.AddDays(1) == new DateTime(2026, 10, 17, 8, 30, 15, DateTimeKind.Utc)).CountAsync());
        if (provider is "sqlserver" or "mysql")
        {
            // DATEADD and INTERVAL take whole amounts only.
            Assert.Throws<NotSupportedException>(() => people.Where(p => p.CreatedAt.AddDays(-7.5) > Now));
        }
        else
        {
            Assert.Equal(5, await people.Where(p => p.CreatedAt.AddDays(-7.5) == new DateTime(2026, 10, 8, 20, 30, 15, DateTimeKind.Utc)).CountAsync());
        }
        Assert.Equal(5, await people.Where(p => p.CreatedAt.AddHours(-8).Hour == 0 && p.CreatedAt.AddMinutes(30).Hour == 9 && p.CreatedAt.AddSeconds(45).Minute == 31).CountAsync());
        Assert.Equal(5, await people.Where(p => p.CreatedAt.AddYears(1).Year == 2027 && p.CreatedAt.AddMonths(-10).Month == 12).CountAsync());
        Assert.Equal(new DateTime(2026, 10, 16), await people.Select(p => p.CreatedAt.Date).FirstOrDefaultAsync());
        Assert.Equal(new DateTime(2026, 10, 16, 8, 30, 52), await people.Where(p => p.Name == "Ada").Select(p => p.CreatedAt.AddSeconds(p.Age + 1)).SingleAsync());

        // Month ends clamp, as in C#: Jan 31 plus a month is Feb 28.
        await people.Where(p => p.Name == "Ada").ExecuteUpdateAsync(s => s.Set(p => p.CreatedAt, new DateTime(2026, 1, 31, 10, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2026, 2, 28, 10, 0, 0), await people.Where(p => p.Name == "Ada").Select(p => p.CreatedAt.AddMonths(1)).SingleAsync());

        var later = new DateTime(2026, 10, 18, 8, 30, 15, DateTimeKind.Utc);
        Assert.Equal(4, await people.Where(p => (later - p.CreatedAt).TotalDays == 2).CountAsync());
        Assert.Equal(48.0, await people.Where(p => p.Name == "Cleo").Select(p => (later - p.CreatedAt).TotalHours).SingleAsync(), 6);
        Assert.Equal(4, await people.Where(p => (p.CreatedAt - later).TotalMinutes == -2880 && (later - p.CreatedAt).TotalSeconds == 172800).CountAsync());
        Assert.Equal(4, await people.Where(p => p.CreatedAt + TimeSpan.FromHours(16) > new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc)).CountAsync());
        Assert.Equal(0, await people.Where(p => p.CreatedAt - TimeSpan.FromMinutes(31) > new DateTime(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc)).CountAsync());
    }
}
