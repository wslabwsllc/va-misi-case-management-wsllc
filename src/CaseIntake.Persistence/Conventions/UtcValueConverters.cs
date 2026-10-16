using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CaseIntake.Persistence.Conventions;

/// <summary>Normalizes every <see cref="DateTimeOffset"/> to UTC (offset zero) before it reaches the database.</summary>
public sealed class UtcDateTimeOffsetConverter()
    : ValueConverter<DateTimeOffset, DateTimeOffset>(v => v.ToUniversalTime(), v => v.ToUniversalTime());

/// <summary>Nullable counterpart of <see cref="UtcDateTimeOffsetConverter"/>.</summary>
public sealed class UtcNullableDateTimeOffsetConverter()
    : ValueConverter<DateTimeOffset?, DateTimeOffset?>(
        v => v.HasValue ? v.Value.ToUniversalTime() : v,
        v => v.HasValue ? v.Value.ToUniversalTime() : v);

/// <summary>
/// <see cref="DateTime"/> values must already be <see cref="DateTimeKind.Utc"/>; anything else is rejected rather
/// than silently reinterpreted. Values read back are tagged as UTC.
/// </summary>
public sealed class UtcDateTimeConverter()
    : ValueConverter<DateTime, DateTime>(v => RequireUtc(v), v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
{
    public static DateTime RequireUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : throw new InvalidOperationException(
            $"Only DateTimeKind.Utc values can be stored; received {value.Kind}. Use TimeProvider.GetUtcNow().");
}

public sealed class UtcNullableDateTimeConverter()
    : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? UtcDateTimeConverter.RequireUtc(v.Value) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
