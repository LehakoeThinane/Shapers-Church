using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Shapers.Platform.Persistence;

/// <summary>
/// PostgreSQL stores instants as UTC and Npgsql refuses offsets other than zero. Times arriving with a
/// local offset (e.g. +02:00 from South African clients) are normalised to UTC on save; the instant is unchanged.
/// </summary>
public sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(v => v.ToUniversalTime(), v => v);

public static class UtcConventions
{
    public static void UseUtcTimestamps(this ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
}
