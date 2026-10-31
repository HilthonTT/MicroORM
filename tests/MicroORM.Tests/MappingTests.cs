using MicroORM.Configuration;
using MicroORM.Dialects;
using MicroORM.Mapping;
using MicroORM.Materialization;
using MicroORM.Sessions;
using MicroORM.Sql;
using MicroORM.Tests.Integration;
using MicroORM.Tests.Model;
using MicroORM.TypeHandlers;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MicroORM.Tests;

public class MappingTests
{
    private static EntityMap Map<T>(MicroOptions? options = null) =>
        new MicroDb(SqlDialect.Sqlite, () => throw new InvalidOperationException(), options ?? TestDatabase.CreateOptions()).MapOf<T>();

    [Theory]
    [InlineData("UserId", "user_id")]
    [InlineData("HTTPServer", "http_server")]
    [InlineData("Address2", "address2")]
    [InlineData("already_snake", "already_snake")]
    [InlineData("ID", "id")]
    [InlineData("CreatedAtUtc", "created_at_utc")]
    public void Snake_case(string input, string expected) => Assert.Equal(expected, NamingConvention.SnakeCase.Convert(input));

    [Fact]
    public void Configuring_options_after_creating_the_database_changes_nothing()
    {
        var options = TestDatabase.CreateOptions();
        var db = new MicroDb(SqlDialect.Sqlite, () => throw new InvalidOperationException(), options);
        options.Entity<Document>(e => e.ToTable("elsewhere"));

        Assert.Equal("micro_documents", db.MapOf<Document>().Table);
    }

    [Fact]
    public void Dates_with_an_offset_read_as_utc_whatever_the_provider()
    {
        var fromText = ValueConverter.To<DateTime>("2024-01-01 10:00:00+02:00");
        var fromOffset = ValueConverter.To<DateTime>(new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.FromHours(2)));

        Assert.Equal(new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc), fromText);
        Assert.Equal(DateTimeKind.Utc, fromText.Kind);
        Assert.Equal(fromOffset, fromText);
        Assert.Equal(DateTimeKind.Unspecified, ValueConverter.To<DateTime>("2024-01-01 10:00:00").Kind);
    }

    [Fact]
    public void Conventions_find_key_and_skip_non_scalars()
    {
        var map = Map<Person>();

        Assert.Equal("micro_people", map.Table);
        var key = Assert.Single(map.Keys);
        Assert.Equal("id", key.Name);
        Assert.Equal(ValueGeneration.Identity, key.Generation);
        Assert.Same(key, map.Identity);
        Assert.Null(map.Version);
        Assert.DoesNotContain(map.Columns, c => c.Property.Name is nameof(Person.Tags) or nameof(Person.DisplayName));
    }

    [Fact]
    public void Guid_keys_are_not_generated()
    {
        var key = Assert.Single(Map<Tag>().Keys);
        Assert.Equal(nameof(Tag.Code), key.Property.Name);
        Assert.Equal(ValueGeneration.None, key.Generation);
        Assert.True(key.IsInsertable);
    }

    [Fact]
    public void Attributes_override_conventions()
    {
        var map = Map<Annotated>();

        Assert.Equal("audit.entries", map.Table);
        Assert.Equal("entry_no", Assert.Single(map.Keys).Name);
        Assert.Equal(ValueGeneration.None, map.Keys[0].Generation);
        Assert.False(map.FindByProperty(nameof(Annotated.Total))!.IsInsertable);
        Assert.Equal(ValueGeneration.Computed, map.FindByProperty(nameof(Annotated.RowVersion))!.Generation);
    }

    [Fact]
    public void Fluent_configuration_overrides_attributes()
    {
        var options = new MicroOptions { Naming = NamingConvention.SnakeCase }
            .Entity<Annotated>(e => e.ToTable("entries_v2").Column(a => a.Note, "remark").Ignore(a => a.Total));
        var map = Map<Annotated>(options);

        Assert.Equal("entries_v2", map.Table);
        Assert.Equal("remark", map.FindByProperty(nameof(Annotated.Note))!.Name);
        Assert.Null(map.FindByProperty(nameof(Annotated.Total)));
    }

    private sealed class PartlyOrderedKeys
    {
        [Key, Column("tenant")]
        public int Tenant { get; set; }

        [Key, Column(Order = 0)]
        public int Region { get; set; }
    }

    [Fact]
    public void Keys_without_a_column_order_follow_ordered_keys()
    {
        // [Column("tenant")] leaves Order at -1, which means "not set", not "first".
        Assert.Equal(["region", "tenant"], Map<PartlyOrderedKeys>().Keys.Select(k => k.Name));
    }

    [Fact]
    public void Null_converts_to_null_for_object()
    {
        Assert.Null(ValueConverter.To<object>(DBNull.Value));
        Assert.Null(ValueConverter.To<object?>(null));
    }

    [Fact]
    public void Handlers_for_the_declared_type_convert_parameters()
    {
        var engine = new MicroEngine(SqlDialect.Sqlite, new MicroOptions().AddJsonColumn<IReadOnlyList<string>>());
        var parameter = new SqlParameterValue("@p0", new List<string> { "a", "b" }, typeof(IReadOnlyList<string>));

        var prepared = engine.Prepare("SELECT @p0", System.Data.CommandType.Text, [parameter], null);

        Assert.Equal("[\"a\",\"b\"]", prepared.Parameters[0].Value);
        Assert.NotNull(prepared.Handlers[0]);
    }

    [Fact]
    public void Composite_keys_follow_column_order()
    {
        var map = Map<OrderLine>();

        Assert.Equal(["order_id", "line_no"], map.Keys.Select(k => k.Name));
        Assert.All(map.Keys, k => Assert.Equal(ValueGeneration.None, k.Generation));
        Assert.Null(map.Identity);
        Assert.Equal([1, 2], map.KeyValues((1, 2), "Find"));
        Assert.Equal([3, 4], map.KeyValues(new object[] { 3, 4 }, "Find"));
        Assert.Throws<ArgumentException>(() => map.KeyValues(1, "Find"));
    }

    [Fact]
    public void Fluent_composite_keys_and_versions()
    {
        var options = new MicroOptions()
            .Entity<Person>(e => e.HasKey(p => new { p.Name, p.CreatedAt }).IsVersion(p => p.Age));
        var map = Map<Person>(options);

        Assert.Equal(["Name", "CreatedAt"], map.Keys.Select(k => k.Property.Name));
        Assert.Equal(nameof(Person.Age), map.Version?.Property.Name);
        Assert.False(map.Version!.IsUpdatable);
        Assert.Null(map.Identity);
    }

    [Fact]
    public void Concurrency_check_marks_the_version()
    {
        var version = Map<Document>().Version;
        Assert.NotNull(version);
        Assert.Equal(nameof(Document.Version), version.Property.Name);
        Assert.True(version.IsInsertable);
        Assert.False(version.IsUpdatable);
    }

    [Fact]
    public void Handled_types_are_mapped_columns()
    {
        var map = Map<Customer>();
        Assert.Equal(["id", "email", "name", "address", "tags", "tier"], map.Columns.Select(c => c.Name));

        var unhandled = Map<Customer>(new MicroOptions());
        Assert.DoesNotContain(unhandled.Columns, c => c.Property.Name is nameof(Customer.Email) or nameof(Customer.Address));
    }

    [Fact]
    public void Keyless_entities_explain_how_to_add_a_key()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Map<Keyless>().RequireKeys("Update"));
        Assert.Contains("[Key]", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(5L, typeof(int), 5)]
    [InlineData(1L, typeof(bool), true)]
    [InlineData("2", typeof(Mood), Mood.Grumpy)]
    [InlineData("Happy", typeof(Mood), Mood.Happy)]
    [InlineData(1L, typeof(Mood?), Mood.Happy)]
    public void Value_conversion(object input, Type target, object expected) => Assert.Equal(expected, ValueConverter.Convert(input, target));

    [Fact]
    public void Value_conversion_handles_text_encoded_types()
    {
        var guid = Guid.NewGuid();
        Assert.Equal(guid, ValueConverter.To<Guid>(guid.ToString()));
        Assert.Equal(guid, ValueConverter.To<Guid?>(guid.ToByteArray()));
        Assert.Equal(12.5m, ValueConverter.To<decimal>("12.50"));
        Assert.Equal(new DateOnly(2026, 10, 16), ValueConverter.To<DateOnly>("2026-10-16"));
        Assert.Equal(new DateTime(2026, 10, 16, 8, 30, 0), ValueConverter.To<DateTime>("2026-10-16 08:30:00"));
        Assert.Null(ValueConverter.To<int?>(DBNull.Value));
        Assert.Equal(0, ValueConverter.To<int>(DBNull.Value));
    }

    [Fact]
    public void Widening_integer_conversions_take_the_fast_path_and_check_overflow()
    {
        Assert.Equal(5, ValueConverter.To<int>(5L));
        Assert.Equal(5, ValueConverter.To<int?>(5L));
        Assert.True(ValueConverter.To<bool>(1L));
        Assert.False(ValueConverter.To<bool?>(0L));
        Assert.Equal((short)7, ValueConverter.To<short>(7L));
        Assert.Throws<OverflowException>(() => ValueConverter.To<int>(long.MaxValue));
    }

    [Fact]
    public void Json_handler_round_trips()
    {
        var handler = new JsonTypeHandler<Address>();
        var json = (string)handler.Write(new Address { Street = "1 Loom Lane", City = "Ghent" })!;

        Assert.Contains("Ghent", json, StringComparison.Ordinal);
        Assert.Equal("Ghent", handler.Read(json).City);
        Assert.Equal("Ghent", handler.Read(System.Text.Encoding.UTF8.GetBytes(json)).City);
    }
}
