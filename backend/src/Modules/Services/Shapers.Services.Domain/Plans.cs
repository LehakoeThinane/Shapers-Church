namespace Shapers.Services.Domain;

public enum PlanItemKind
{
    /// <summary>A section heading (Worship, Word, Response). Takes no time.</summary>
    Header,

    /// <summary>Anything that happens: welcome, announcements, sermon, offering.</summary>
    Item,

    Song,
}

/// <summary>One line of the order of service.</summary>
public sealed record PlanItem(
    Guid Id,
    PlanItemKind Kind,
    string Title,
    int LengthSeconds,
    string? Description,
    Guid? SongId,
    Guid? ArrangementId,
    string? Key,
    string? Leader);

/// <summary>How many people a plan needs in a position, e.g. two vocalists.</summary>
public sealed record PositionNeed(Guid PositionId, int Count);

internal static class OrderOfService
{
    public const int MaxItems = 100;
    public const int MaxLengthSeconds = 4 * 60 * 60;

    /// <summary>Checks and tidies items: titles required, sensible lengths, headers take no time, new items get ids.</summary>
    public static List<PlanItem> Clean(IEnumerable<PlanItem> items)
    {
        var list = items.ToList();
        if (list.Count > MaxItems)
        {
            throw new DomainRuleException("services.too_many_items", $"Keep the order of service to {MaxItems} items.");
        }

        return list.Select(i =>
        {
            if (i.LengthSeconds < 0 || i.LengthSeconds > MaxLengthSeconds)
            {
                throw new DomainRuleException("services.item_length", "An item can't be longer than four hours.");
            }

            if (i.Kind == PlanItemKind.Song && i.SongId is null)
            {
                throw new DomainRuleException("services.song_required", "Choose a song for each song item.");
            }

            return i with
            {
                Id = i.Id == Guid.Empty ? Guid.CreateVersion7() : i.Id,
                Title = Text.Required(i.Title, 120, "Every item needs a title."),
                LengthSeconds = i.Kind == PlanItemKind.Header ? 0 : i.LengthSeconds,
                Description = Text.Optional(i.Description, 2000),
                Key = Text.Optional(i.Key, 10),
                Leader = Text.Optional(i.Leader, 80),
                SongId = i.Kind == PlanItemKind.Song ? i.SongId : null,
                ArrangementId = i.Kind == PlanItemKind.Song ? i.ArrangementId : null,
            };
        }).ToList();
    }

    public static List<PositionNeed> CleanNeeds(IEnumerable<PositionNeed> needs) =>
        needs.Where(n => n.Count > 0).GroupBy(n => n.PositionId).Select(g => new PositionNeed(g.Key, Math.Min(20, g.Sum(n => n.Count)))).ToList();
}

/// <summary>A kind of service with its usual order and team needs, e.g. "Sunday 09:00". New plans start from it.</summary>
public sealed class ServiceType : AggregateRoot<Guid>
{
    private ServiceType()
    {
    }

    public string Name { get; private set; } = null!;

    public string Scope { get; private set; } = null!;

    public TimeOnly StartTime { get; private set; }

    public List<PlanItem> Items { get; private set; } = [];

    public List<PositionNeed> Needs { get; private set; } = [];

    public bool IsArchived { get; private set; }

    public static ServiceType Create(string name, ScopePath scope, TimeOnly startTime, IEnumerable<PlanItem> items, IEnumerable<PositionNeed> needs)
    {
        var type = new ServiceType { Id = Guid.CreateVersion7(), Scope = scope.Value };
        type.Update(name, startTime, items, needs);
        return type;
    }

    public void Update(string name, TimeOnly startTime, IEnumerable<PlanItem> items, IEnumerable<PositionNeed> needs)
    {
        Name = Text.Required(name, 80, "Give the service a name, e.g. Sunday 09:00.");
        StartTime = startTime;
        Items = OrderOfService.Clean(items);
        Needs = OrderOfService.CleanNeeds(needs);
    }

    public void Archive() => IsArchived = true;
}

/// <summary>
/// One service on one day: its order of service, the people it needs, and (during the service) which item is live.
/// </summary>
public sealed class Plan : AggregateRoot<Guid>
{
    private Plan()
    {
    }

    public Guid? ServiceTypeId { get; private set; }

    public string Title { get; private set; } = null!;

    public DateOnly Date { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public string Scope { get; private set; } = null!;

    /// <summary>E.g. the sermon series, shown on the plan.</summary>
    public string? SeriesTitle { get; private set; }

    /// <summary>Notes for everyone serving (arrive times, dress, parking).</summary>
    public string? Notes { get; private set; }

    public Guid? LivestreamId { get; private set; }

    public List<PlanItem> Items { get; private set; } = [];

    public List<PositionNeed> Needs { get; private set; } = [];

    /// <summary>The item running now, while the service is live.</summary>
    public Guid? LiveItemId { get; private set; }

    public DateTimeOffset? LiveItemStartedAt { get; private set; }

    public DateTimeOffset? LiveEndedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Plan Create(string title, DateOnly date, TimeOnly startTime, ScopePath scope, DateTimeOffset now) =>
        Create(title, date, startTime, scope, null, [], [], now);

    /// <summary>A plan from a service type: its order (with fresh item ids) and needs.</summary>
    public static Plan FromType(ServiceType type, DateOnly date, string? title, DateTimeOffset now) =>
        Create(title ?? type.Name, date, type.StartTime, ScopePath.Parse(type.Scope), type.Id, type.Items.Select(i => i with { Id = Guid.Empty }), type.Needs, now);

    private static Plan Create(string title, DateOnly date, TimeOnly startTime, ScopePath scope, Guid? typeId, IEnumerable<PlanItem> items, IEnumerable<PositionNeed> needs, DateTimeOffset now)
    {
        var plan = new Plan { Id = Guid.CreateVersion7(), Scope = scope.Value, ServiceTypeId = typeId, CreatedAt = now };
        plan.UpdateDetails(title, date, startTime, null, null, null, now);
        plan.SetItems(items, now);
        plan.SetNeeds(needs, now);
        return plan;
    }

    public void UpdateDetails(string title, DateOnly date, TimeOnly startTime, string? seriesTitle, string? notes, Guid? livestreamId, DateTimeOffset now)
    {
        Title = Text.Required(title, 120, "Give the plan a title.");
        Date = date;
        StartTime = startTime;
        SeriesTitle = Text.Optional(seriesTitle, 120);
        Notes = Text.Optional(notes, 4000);
        LivestreamId = livestreamId;
        UpdatedAt = now;
    }

    public void SetItems(IEnumerable<PlanItem> items, DateTimeOffset now)
    {
        Items = OrderOfService.Clean(items);
        if (LiveItemId is { } live && Items.All(i => i.Id != live))
        {
            LiveItemId = null;
            LiveItemStartedAt = null;
        }

        UpdatedAt = now;
    }

    public void SetNeeds(IEnumerable<PositionNeed> needs, DateTimeOffset now)
    {
        Needs = OrderOfService.CleanNeeds(needs);
        UpdatedAt = now;
    }

    public int TotalSeconds => Items.Sum(i => i.LengthSeconds);

    /// <summary>When each item starts, from the plan's start time and the lengths before it.</summary>
    public IReadOnlyDictionary<Guid, TimeOnly> StartTimes()
    {
        var times = new Dictionary<Guid, TimeOnly>();
        var at = StartTime;
        foreach (var item in Items)
        {
            times[item.Id] = at;
            at = at.AddMinutes(item.LengthSeconds / 60d);
        }

        return times;
    }

    // ---------- Live ----------

    public bool IsLive => LiveItemId is not null;

    /// <summary>Starts the run sheet on the first item that takes time, or moves to the given item.</summary>
    public void GoLive(Guid? itemId, DateTimeOffset now)
    {
        var target = itemId is { } id
            ? Items.FirstOrDefault(i => i.Id == id) ?? throw new DomainRuleException("services.item_not_found", "That item isn't in this plan.")
            : Items.FirstOrDefault(i => i.Kind != PlanItemKind.Header) ?? throw new DomainRuleException("services.empty_plan", "Add items to the plan first.");
        LiveItemId = target.Id;
        LiveItemStartedAt = now;
        LiveEndedAt = null;
    }

    /// <summary>Next (or previous) item, skipping headers. Moving past the last item ends the run sheet.</summary>
    public void Step(int direction, DateTimeOffset now)
    {
        if (LiveItemId is null)
        {
            GoLive(null, now);
            return;
        }

        var timed = Items.Where(i => i.Kind != PlanItemKind.Header).ToList();
        var index = timed.FindIndex(i => i.Id == LiveItemId) + Math.Sign(direction);
        if (index >= timed.Count)
        {
            EndLive(now);
            return;
        }

        LiveItemId = timed[Math.Max(0, index)].Id;
        LiveItemStartedAt = now;
    }

    public void EndLive(DateTimeOffset now)
    {
        LiveItemId = null;
        LiveItemStartedAt = null;
        LiveEndedAt = now;
    }
}
