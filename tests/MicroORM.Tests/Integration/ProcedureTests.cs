using MicroORM.Procedures;
using MicroORM.Tests.Model;

using static MicroORM.Tests.Integration.Seed;

namespace MicroORM.Tests.Integration;

[Collection(ProviderCollectionDefinition.Name)]
public class ProcedureTests
{
    public static TheoryData<string> WithProcedures => TestDatabase.With(["postgres", "sqlserver", "mysql"]);

    public static TheoryData<string> WithRowProcedures => TestDatabase.With(["sqlserver", "mysql"]);

    public static TheoryData<string> SqlServer => TestDatabase.With(["sqlserver"]);

    [ProviderTheory("postgres", "sqlserver", "mysql")]
    [MemberData(nameof(WithProcedures))]
    public async Task Output_parameters_are_read_back(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        var parameters = new ProcedureParameters().Add("a", 2).Add("b", 3).Output<int>("total");

        await database.Db.ExecuteProcedureAsync("micro_add", parameters);

        Assert.Equal(5, parameters.Get<int>("total"));
        Assert.Throws<InvalidOperationException>(() => parameters.Get<int>("a"));
    }

    [ProviderTheory("sqlserver", "mysql")]
    [MemberData(nameof(WithRowProcedures))]
    public async Task Procedures_return_rows_and_outputs(string provider)
    {
        await using var database = await PeopleAsync(provider);
        var parameters = new ProcedureParameters().Add("min_age", 30).Output<int>("total_count");

        var rows = await database.Db.QueryProcedureAsync<PersonSummary>("micro_people_older_than", parameters);

        Assert.Equal(["Ada", "50% Eve_", "Cleo"], rows.Select(r => r.Name));
        Assert.Equal(3, parameters.Get<int>("total_count"));
    }

    [ProviderTheory("sqlserver")]
    [MemberData(nameof(SqlServer))]
    public async Task Sql_server_return_values(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        var parameters = new ProcedureParameters().Add("a", 1).Add("b", 1).Output<int>("total").ReturnValue();

        database.Db.ExecuteProcedure("micro_add", parameters);

        Assert.Equal(7, parameters.Get<int>("ReturnValue"));
    }

    [Fact]
    public async Task Sqlite_has_no_procedures()
    {
        await using var database = await TestDatabase.CreateAsync("sqlite");
        await Assert.ThrowsAsync<NotSupportedException>(() => database.Db.ExecuteProcedureAsync("anything"));
    }
}
