namespace Shapers.Content.Domain;

public enum ContentStatus
{
    Draft,

    /// <summary>Published automatically at PublishAt.</summary>
    Scheduled,

    Published,

    /// <summary>Taken down but kept, e.g. an old notice.</summary>
    Archived,
}

public enum PostKind
{
    /// <summary>Articles and teaching: the blog on the website.</summary>
    Blog,

    /// <summary>Short, time-bound church news: shown on the app's Home and the website's front page.</summary>
    News,
}

public sealed record ContentPublished(Guid Id, string Kind, string Slug) : IDomainEvent;

/// <summary>Draft, schedule, publish and archive rules shared by pages and posts.</summary>
public abstract class PublishableContent : AggregateRoot<Guid>
{
    public const int MaxTitle = 150;
    public const int MaxSummary = 300;
    public const int MaxBody = 50_000;

    public string Title { get; protected set; } = null!;

    public string Slug { get; protected set; } = null!;

    /// <summary>One or two sentences for lists, link previews and search engines.</summary>
    public string? Summary { get; protected set; }

    /// <summary>Markdown.</summary>
    public string Body { get; protected set; } = null!;

    public string Scope { get; protected set; } = null!;

    public ContentStatus Status { get; protected set; }

    public DateTimeOffset? PublishAt { get; protected set; }

    public DateTimeOffset? PublishedAt { get; protected set; }

    public DateTimeOffset CreatedAt { get; protected set; }

    public DateTimeOffset UpdatedAt { get; protected set; }

    /// <summary>The address on the old WordPress site, so it can redirect here.</summary>
    public string? LegacyPath { get; protected set; }

    public bool IsLive(DateTimeOffset now) => Status == ContentStatus.Published || (Status == ContentStatus.Scheduled && PublishAt <= now);

    protected void SetText(string title, string? summary, string body, DateTimeOffset now)
    {
        Title = Required(title, MaxTitle, "Give it a title.");
        Summary = Optional(summary, MaxSummary);
        Body = Required(body, MaxBody, "Write something first.");
        UpdatedAt = now;
    }

    public void UseSlug(string slug)
    {
        if (!SlugRules.IsValid(slug))
        {
            throw new DomainRuleException("content.slug_invalid", "Use lower-case letters, numbers and dashes, e.g. about-us.");
        }

        Slug = slug;
    }

    public void SetLegacyPath(string? path) => LegacyPath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    public void Publish(DateTimeOffset now)
    {
        if (Status == ContentStatus.Published)
        {
            return;
        }

        Status = ContentStatus.Published;
        PublishedAt ??= now;
        PublishAt = null;
        UpdatedAt = now;
        Raise(new ContentPublished(Id, GetType().Name, Slug));
    }

    public void Schedule(DateTimeOffset at, DateTimeOffset now)
    {
        if (at <= now)
        {
            throw new DomainRuleException("content.schedule_past", "Choose a time in the future, or publish now.");
        }

        Status = ContentStatus.Scheduled;
        PublishAt = at;
        UpdatedAt = now;
    }

    /// <summary>Called by the minute job: a scheduled item whose time has come becomes published.</summary>
    public bool PublishIfDue(DateTimeOffset now)
    {
        if (Status != ContentStatus.Scheduled || PublishAt > now)
        {
            return false;
        }

        Publish(now);
        return true;
    }

    public void Unpublish(DateTimeOffset now)
    {
        Status = ContentStatus.Draft;
        PublishAt = null;
        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        Status = ContentStatus.Archived;
        PublishAt = null;
        UpdatedAt = now;
    }

    protected static string Required(string value, int max, string message)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainRuleException("content.required", message);
        }

        return trimmed.Length <= max ? trimmed : throw new DomainRuleException("content.too_long", $"Keep it under {max:N0} characters.");
    }

    protected static string? Optional(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (trimmed?.Length > max)
        {
            throw new DomainRuleException("content.too_long", $"Keep it under {max:N0} characters.");
        }

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

/// <summary>A standing page: About us, Shapers Growth Track, Contact, Giving.</summary>
public sealed class Page : PublishableContent
{
    private Page()
    {
    }

    /// <summary>Listed in the website's menu, in this order. Null keeps it out of the menu.</summary>
    public int? MenuOrder { get; private set; }

    public static Page Create(string title, string? summary, string body, ScopePath scope, DateTimeOffset now)
    {
        var page = new Page { Id = Guid.CreateVersion7(), Scope = scope.Value, CreatedAt = now, Status = ContentStatus.Draft };
        page.SetText(title, summary, body, now);
        page.UseSlug(SlugRules.From(title));
        return page;
    }

    public void Edit(string title, string? summary, string body, int? menuOrder, DateTimeOffset now)
    {
        SetText(title, summary, body, now);
        MenuOrder = menuOrder;
    }
}

/// <summary>A blog article or a news item.</summary>
public sealed class Post : PublishableContent
{
    private Post()
    {
    }

    public PostKind Kind { get; private set; }

    public string? Author { get; private set; }

    public string? CoverImageUrl { get; private set; }

    /// <summary>News only: after this date it drops off Home and the front page (it stays on the site).</summary>
    public DateOnly? ShowUntil { get; private set; }

    public static Post Create(PostKind kind, string title, string? summary, string body, ScopePath scope, DateTimeOffset now)
    {
        var post = new Post { Id = Guid.CreateVersion7(), Kind = kind, Scope = scope.Value, CreatedAt = now, Status = ContentStatus.Draft };
        post.SetText(title, summary, body, now);
        post.UseSlug(SlugRules.From(title));
        return post;
    }

    public void Edit(PostKind kind, string title, string? summary, string body, string? author, string? coverImageUrl, DateOnly? showUntil, DateTimeOffset now)
    {
        SetText(title, summary, body, now);
        if (!string.IsNullOrWhiteSpace(coverImageUrl)
            && (!Uri.TryCreate(coverImageUrl.Trim(), UriKind.Absolute, out var parsed) || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)))
        {
            throw new DomainRuleException("content.image_invalid", "Use a full image link starting with https://.");
        }

        Kind = kind;
        Author = Optional(author, 100);
        CoverImageUrl = string.IsNullOrWhiteSpace(coverImageUrl) ? null : coverImageUrl.Trim();
        ShowUntil = kind == PostKind.News ? showUntil : null;
    }

    /// <summary>Backdates an imported article to when it was first published on the old site.</summary>
    public void ImportedAs(DateTimeOffset originallyPublished, DateTimeOffset now)
    {
        Publish(now);
        PublishedAt = originallyPublished;
        ClearDomainEvents();
    }
}

public static class SlugRules
{
    public static string From(string title) => Shapers.SharedKernel.Slug.From(title, "untitled");

    public static bool IsValid(string slug) => Shapers.SharedKernel.Slug.IsValid(slug);
}
