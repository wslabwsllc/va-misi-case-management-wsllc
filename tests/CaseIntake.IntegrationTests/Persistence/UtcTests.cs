using CaseIntake.Persistence;
using CaseIntake.Persistence.Conventions;
using CaseIntake.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace CaseIntake.IntegrationTests.Persistence;

public class UtcTests : ClassFixture<UtcTests, PostgresDatabaseFixture>
{
    private static PostgresDatabaseFixture database => Shared;

    [Test]
    public void DateTimeOffset_converter_normalizes_any_offset_to_utc()
    {
        var local = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.FromHours(5));

        var stored = (DateTimeOffset)new UtcDateTimeOffsetConverter().ConvertToProvider(local)!;

        Assert.Equal(TimeSpan.Zero, stored.Offset);
        Assert.Equal(local.UtcDateTime, stored.UtcDateTime);
        var nullable = (DateTimeOffset?)new UtcNullableDateTimeOffsetConverter().ConvertToProvider((DateTimeOffset?)local);
        Assert.Equal(TimeSpan.Zero, nullable!.Value.Offset);
        Assert.Null(new UtcNullableDateTimeOffsetConverter().ConvertToProvider(null));
    }

    [TestCase(DateTimeKind.Local)]
    [TestCase(DateTimeKind.Unspecified)]
    public void DateTime_converter_rejects_non_utc_values(DateTimeKind kind)
    {
        var value = new DateTime(2026, 10, 16, 12, 0, 0, kind);

        Assert.Throws<InvalidOperationException>(() => new UtcDateTimeConverter().ConvertToProvider(value));
        Assert.Throws<InvalidOperationException>(() => new UtcNullableDateTimeConverter().ConvertToProvider((DateTime?)value));
    }

    [Test]
    public void DateTime_converter_accepts_utc_and_tags_reads_as_utc()
    {
        var utc = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(utc, new UtcDateTimeConverter().ConvertToProvider(utc));
        Assert.Equal(DateTimeKind.Utc, ((DateTime)new UtcDateTimeConverter().ConvertFromProvider(new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Unspecified))!).Kind);
    }

    [Test]
    public void Every_persisted_DateTime_and_DateTimeOffset_property_is_timestamptz_with_a_utc_converter()
    {
        using var db = database.CreateContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var temporal = model.GetEntityTypes().SelectMany(e => e.GetProperties())
            .Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType) == typeof(DateTime)
                        || (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType) == typeof(DateTimeOffset)).ToList();

        Assert.Contains(temporal, p => p.Name == "LockoutEnd");   // Identity fields are included, not skipped
        foreach (var property in temporal)
        {
            var name = $"{property.DeclaringType.DisplayName()}.{property.Name}";
            Assert.True(property.GetColumnType() == "timestamp with time zone", $"{name} column type is {property.GetColumnType()}");
            Assert.True(property.GetValueConverter() is not null, $"{name} has no UTC value converter");
        }
    }

    [Test]
    public async Task Non_utc_offsets_are_stored_as_the_same_instant_in_a_timestamptz_column()
    {
        var instant = new DateTimeOffset(2026, 10, 16, 12, 30, 0, TimeSpan.FromHours(-7));
        await using (var db = database.CreateContext())
        {
            db.Users.Add(new AppUser { Id = "u-utc", UserName = "utc", NormalizedUserName = "UTC", LockoutEnd = instant });
            await db.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT \"LockoutEnd\", pg_typeof(\"LockoutEnd\")::text, current_setting('TimeZone') FROM \"AspNetUsers\" WHERE \"Id\" = 'u-utc'", connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var raw = reader.GetFieldValue<DateTimeOffset>(0);
        Assert.Equal(instant.UtcDateTime, raw.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, raw.Offset);
        Assert.Equal("timestamp with time zone", reader.GetString(1));
        Assert.Equal("UTC", reader.GetString(2));   // session time zone, so textual output is UTC
    }
}
