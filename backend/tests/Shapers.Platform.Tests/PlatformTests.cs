using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shapers.Platform.Authorization;
using Shapers.Platform.Messaging;
using Shapers.Platform.Text;

namespace Shapers.Platform.Tests;

public sealed class ScopePathTests
{
    [Fact]
    public void Builds_paths_from_slugs()
    {
        var church = ScopePath.Organisation("Shapers");
        var campus = church.Child(ScopeType.Campus, "Rivonia");
        var ministry = campus.Child(ScopeType.Ministry, "Growth Track");

        Assert.Equal("shapers.campus_rivonia.ministry_growth_track", ministry.Value);
        Assert.Equal(ScopeType.Ministry, ministry.Type);
        Assert.Equal(ScopeType.Campus, campus.Type);
        Assert.Equal(ScopeType.Global, church.Type);
        Assert.Equal(campus, ministry.Parent);
        Assert.Equal(church, ministry.Root);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Shapers")]
    [InlineData("shapers..campus_x")]
    [InlineData("shapers.campus-x")]
    [InlineData("shapers.campus_x.")]
    [InlineData("shapers.campus_x'; drop table")]
    public void Rejects_malformed_paths(string value)
    {
        Assert.False(ScopePath.TryParse(value, out _));
    }

    [Fact]
    public void Covers_only_self_and_descendants_on_label_boundaries()
    {
        var rivonia = ScopePath.Parse("shapers.campus_rivonia");

        Assert.True(rivonia.Covers(rivonia));
        Assert.True(rivonia.Covers(ScopePath.Parse("shapers.campus_rivonia.ministry_kids")));
        Assert.False(rivonia.Covers(ScopePath.Parse("shapers")));
        Assert.False(rivonia.Covers(ScopePath.Parse("shapers.campus_rivonia_north")));
    }

    [Fact]
    public void Collapse_removes_scopes_inside_others()
    {
        var collapsed = ScopeSet.Collapse(
        [
            ScopePath.Parse("shapers.campus_a.ministry_x"),
            ScopePath.Parse("shapers.campus_a"),
            ScopePath.Parse("shapers.campus_b"),
            ScopePath.Parse("shapers.campus_a"),
        ]);

        Assert.Equal(["shapers.campus_a", "shapers.campus_b"], collapsed.Select(s => s.Value).Order());
    }

    [Fact]
    public void Scope_filter_matches_self_and_descendants_only()
    {
        var rows = new[] { "shapers", "shapers.campus_a", "shapers.campus_a.ministry_x", "shapers.campus_ab", "shapers.campus_b" }
            .Select(s => new Row(s))
            .AsQueryable();

        var visible = rows.WithinScopes(r => r.Scope, [ScopePath.Parse("shapers.campus_a")]).Select(r => r.Scope).ToList();

        Assert.Equal(["shapers.campus_a", "shapers.campus_a.ministry_x"], visible);
        Assert.Empty(rows.WithinScopes(r => r.Scope, []));
    }

    private sealed record Row(string Scope);
}

public sealed class ContactNormaliserTests
{
    [Theory]
    [InlineData("082 123 4567", "+27821234567")]
    [InlineData("+27 82 123 4567", "+27821234567")]
    [InlineData("0027821234567", "+27821234567")]
    [InlineData("+44 7911 123456", "+447911123456")]
    public void Normalises_phone_numbers_to_e164(string input, string expected)
    {
        Assert.True(ContactNormaliser.TryNormalisePhone(input, out var e164));
        Assert.Equal(expected, e164);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("not a number")]
    public void Rejects_invalid_phone_numbers(string input)
    {
        Assert.False(ContactNormaliser.TryNormalisePhone(input, out _));
    }

    [Theory]
    [InlineData("  Thandi@Example.COM ", true, "thandi@example.com")]
    [InlineData("no-at-sign", false, "")]
    [InlineData("a@b", false, "")]
    [InlineData("two@@example.com", false, "")]
    public void Normalises_emails(string input, bool valid, string expected)
    {
        Assert.Equal(valid, ContactNormaliser.TryNormaliseEmail(input, out var email));
        if (valid)
        {
            Assert.Equal(expected, email);
        }
    }

    [Fact]
    public void Masks_phone_numbers_for_display()
    {
        Assert.Equal("+2782 *** 4567", ContactNormaliser.MaskPhone("+27821234567"));
    }

    [Fact]
    public void Escapes_like_wildcards()
    {
        Assert.Equal(@"100\%\_\\", LikePattern.Escape(@"100%_\"));
    }
}

public sealed class IntegrationEventDispatcherTests
{
    public sealed record SomethingHappened(string Detail) : IntegrationEvent;

    private sealed class Recorder
    {
        public List<string> Calls { get; } = [];
    }

    private sealed class FirstHandler(Recorder recorder) : IIntegrationEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken)
        {
            recorder.Calls.Add($"first:{integrationEvent.Detail}");
            return Task.CompletedTask;
        }
    }

    private sealed class FlakyHandler(Recorder recorder) : IIntegrationEventHandler<SomethingHappened>
    {
        public static bool Fail { get; set; } = true;

        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken)
        {
            recorder.Calls.Add("flaky");
            return Fail ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        }
    }

    private sealed class MemoryInbox : IInbox
    {
        public HashSet<(Guid, string)> Processed { get; } = [];

        public Task<bool> HasProcessedAsync(Guid eventId, string handler, CancellationToken cancellationToken) =>
            Task.FromResult(Processed.Contains((eventId, handler)));

        public Task MarkProcessedAsync(Guid eventId, string handler, CancellationToken cancellationToken)
        {
            Processed.Add((eventId, handler));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Redelivery_only_reruns_handlers_that_have_not_succeeded()
    {
        var recorder = new Recorder();
        var inbox = new MemoryInbox();
        var services = new ServiceCollection()
            .AddSingleton(recorder)
            .AddSingleton<IInbox>(inbox)
            .AddScoped<IIntegrationEventHandler<SomethingHappened>, FirstHandler>()
            .AddScoped<IIntegrationEventHandler<SomethingHappened>, FlakyHandler>()
            .BuildServiceProvider();
        var dispatcher = new IntegrationEventDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            new IntegrationEventTypeRegistry([new IntegrationEventAssembly(typeof(SomethingHappened).Assembly)]),
            NullLogger<IntegrationEventDispatcher>.Instance);
        var message = OutboxMessage.From(new SomethingHappened("x"));

        FlakyHandler.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(message.Type, message.Payload, CancellationToken.None));
        FlakyHandler.Fail = false;
        await dispatcher.DispatchAsync(message.Type, message.Payload, CancellationToken.None);
        await dispatcher.DispatchAsync(message.Type, message.Payload, CancellationToken.None);

        Assert.Equal(["first:x", "flaky", "flaky"], recorder.Calls);
        Assert.Equal(2, inbox.Processed.Count);
    }

    [Fact]
    public void Outbox_message_round_trips_the_event()
    {
        var original = new SomethingHappened("detail");
        var message = OutboxMessage.From(original);

        Assert.Equal(original.EventId, message.Id);
        Assert.Contains("\"detail\":\"detail\"", message.Payload, StringComparison.Ordinal);
        Assert.Null(message.ProcessedAt);
    }
}
