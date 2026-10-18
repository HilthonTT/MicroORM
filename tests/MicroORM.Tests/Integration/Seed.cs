using MicroORM.Configuration;
using MicroORM.Tests.Model;

namespace MicroORM.Tests.Integration;

/// <summary>Shared test data.</summary>
internal static class Seed
{
    public static readonly DateTime Now = new(2026, 10, 16, 8, 30, 15, DateTimeKind.Utc);

    public static Person NewPerson(string name, int age, bool active = true, Mood mood = Mood.Calm, string? email = null) => new()
    {
        Name = name,
        Age = age,
        Active = active,
        Mood = mood,
        Email = email,
        CreatedAt = Now,
    };

    /// <summary>
    /// Five people. By age: Brendan 17, Dmitri 29, Ada 36, "50% Eve_" 41, Cleo 52 (sum 175, average 35).
    /// </summary>
    public static async Task<TestDatabase> PeopleAsync(string provider, Action<MicroOptions>? configure = null)
    {
        var database = await TestDatabase.CreateAsync(provider, configure);
        await database.Db.Table<Person>().InsertManyAsync(
        [
            NewPerson("Ada", 36, mood: Mood.Happy, email: "ada@example.com"),
            NewPerson("Brendan", 17, active: false),
            NewPerson("Cleo", 52, mood: Mood.Grumpy, email: "cleo@example.com"),
            NewPerson("Dmitri", 29, mood: Mood.Happy),
            NewPerson("50% Eve_", 41),
        ]);
        database.Log.Clear();
        return database;
    }
}
