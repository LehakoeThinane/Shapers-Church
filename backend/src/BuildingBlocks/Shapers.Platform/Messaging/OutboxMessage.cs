using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shapers.SharedKernel;

namespace Shapers.Platform.Messaging;

/// <summary>
/// An integration event waiting to be delivered. Written in the same transaction as the data change
/// that caused it, so an event is published if and only if the change was committed.
/// </summary>
public sealed class OutboxMessage
{
    public const int MaxAttempts = 10;

    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage From(IIntegrationEvent integrationEvent) => new()
    {
        Id = integrationEvent.EventId,
        Type = IntegrationEventTypeRegistry.NameOf(integrationEvent.GetType()),
        Payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), MessagingJson.Options),
        OccurredAt = integrationEvent.OccurredAt,
    };

    public void MarkProcessed(DateTimeOffset at) => ProcessedAt = at;

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
    }
}

internal static class MessagingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public static class OutboxModelBuilderExtensions
{
    public static ModelBuilder ApplyOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.Type).HasMaxLength(300);
            b.Property(m => m.Payload).HasColumnType("jsonb");
            b.Property(m => m.LastError).HasMaxLength(2000);
            b.HasIndex(m => m.OccurredAt).HasFilter("processed_at IS NULL");
        });
        return modelBuilder;
    }
}
