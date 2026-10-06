namespace Shapers.Services.Contracts;

public static class ServicesPermissions
{
    /// <summary>Build service plans and templates, and run the live run sheet.</summary>
    public const string PlansEdit = "services.plans.edit";

    /// <summary>Manage teams in scope and schedule people to serve.</summary>
    public const string Schedule = "services.schedule";

    /// <summary>Keep the song library: songs, arrangements, keys, charts and lyrics.</summary>
    public const string SongsEdit = "services.songs.edit";

    /// <summary>Add, rename and remove the categories teams are grouped in (Ministries, Disciplines, ...).</summary>
    public const string CategoriesManage = "services.categories.manage";
}

/// <summary>Someone was asked to serve. Notifications push it to their phone; the email with answer links is sent by Services.</summary>
public sealed record ServingRequestedIntegrationEvent(Guid AssignmentId, Guid PersonId, string PlanTitle, DateOnly Date, string Team, string Position) : IntegrationEvent;

/// <summary>A few days before a service: a reminder for someone who said yes.</summary>
public sealed record ServingReminderIntegrationEvent(Guid AssignmentId, Guid PersonId, string PlanTitle, DateOnly Date, string Team, string Position) : IntegrationEvent;

/// <summary>Someone can't serve: the team's schedulers are told so they can find someone else.</summary>
public sealed record ServingDeclinedIntegrationEvent(Guid AssignmentId, Guid PlanId, string PlanTitle, DateOnly Date, string Team, string Position, string Scope) : IntegrationEvent;
