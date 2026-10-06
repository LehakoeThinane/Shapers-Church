using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shapers.People.Contracts;
using Shapers.Platform.Authorization;
using Shapers.Platform.Email;
using Shapers.Platform.Messaging;
using Shapers.Services.Contracts;
using Shapers.Services.Domain;

namespace Shapers.Services.Application;

/// <summary>Anyone signed in: where they're serving, their answers, the dates they're away, and what to rehearse.</summary>
public sealed class MyServingService(IServicesDb db, PlanReader reader, IAuthorizer authorizer, ICurrentUser currentUser, TimeProvider clock)
{
    private static readonly Error SignIn = Error.Unauthorized("services.sign_in", "Sign in to see where you're serving.");
    private static readonly Error NotFound = Error.NotFound("services.assignment_not_found", "Not found.");

    public async Task<Result<IReadOnlyList<MyAssignmentDto>>> ScheduleAsync(CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        var today = ServingTime.Today(clock);
        var mine = await db.Assignments.AsNoTracking().Where(a => a.PersonId == me && a.Date >= today).OrderBy(a => a.Date).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<MyAssignmentDto>>.Ok(await ToMineAsync(mine, cancellationToken));
    }

    public async Task<Result<MyAssignmentDto>> AnswerAsync(Guid assignmentId, ServingAnswerRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        var assignment = await db.Assignments.SingleOrDefaultAsync(a => a.Id == assignmentId && a.PersonId == me, cancellationToken);
        return assignment is null ? NotFound : await AnswerAsync(assignment, request, cancellationToken);
    }

    /// <summary>Shared with the email link: records the answer, telling the schedulers when it's a no.</summary>
    public async Task<Result<MyAssignmentDto>> AnswerAsync(Assignment assignment, ServingAnswerRequest request, CancellationToken cancellationToken)
    {
        if (assignment.Date < ServingTime.Today(clock))
        {
            return new Error("services.plan_past", "This service has already happened.");
        }

        if (request.Accept)
        {
            if (await db.Blockouts.AnyAsync(b => b.PersonId == assignment.PersonId && b.From <= assignment.Date && b.To >= assignment.Date, cancellationToken))
            {
                return Error.Conflict("services.away", "You've marked yourself away that day. Remove that first if you can serve.");
            }

            assignment.Accept(clock.GetUtcNow());
        }
        else
        {
            var (plan, team, position) = await NamesAsync(assignment, cancellationToken);
            assignment.Decline(request.Reason, plan, team, position, clock.GetUtcNow());
        }

        await db.SaveChangesAsync(cancellationToken);
        return (await ToMineAsync([assignment], cancellationToken))[0];
    }

    public async Task<Result<IReadOnlyList<BlockoutDto>>> BlockoutsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        var today = ServingTime.Today(clock);
        return await db.Blockouts.AsNoTracking().Where(b => b.PersonId == me && b.To >= today).OrderBy(b => b.From)
            .Select(b => new BlockoutDto(b.Id, b.From, b.To, b.Reason)).ToListAsync(cancellationToken);
    }

    public async Task<Result<BlockoutDto>> AddBlockoutAsync(SaveBlockoutRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        var blockout = Blockout.Create(me, request.From, request.To, request.Reason, ServingTime.Today(clock));
        db.Blockouts.Add(blockout);
        await db.SaveChangesAsync(cancellationToken);
        return new BlockoutDto(blockout.Id, blockout.From, blockout.To, blockout.Reason);
    }

    public async Task<Result> DeleteBlockoutAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        var deleted = await db.Blockouts.Where(b => b.Id == id && b.PersonId == me).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? Error.NotFound("services.blockout_not_found", "Not found.") : Result.Success();
    }

    /// <summary>
    /// A plan as the people serving on it see it: the order of service, the songs with their chord charts, recordings
    /// and lyrics, and who else is serving. Open to anyone on it who hasn't said no, and to staff.
    /// </summary>
    public async Task<Result<RehearsePlanDto>> RehearseAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await db.Plans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null)
        {
            return Error.NotFound("services.plan_not_found", "Plan not found.");
        }

        var onIt = currentUser.PersonId is { } me && await db.Assignments.AnyAsync(a => a.PlanId == planId && a.PersonId == me && a.Status != AssignmentStatus.Declined, cancellationToken);
        if (!onIt && !await ServingTime.CanAnyAsync(authorizer, plan.Scope, cancellationToken, ServicesPermissions.PlansEdit, ServicesPermissions.Schedule, ServicesPermissions.SongsEdit))
        {
            return Error.NotFound("services.plan_not_found", "Plan not found.");
        }

        var items = await reader.ItemsAsync(plan, cancellationToken);
        var songIds = plan.Items.Where(i => i.SongId is not null).Select(i => i.SongId!.Value).Distinct().ToList();
        var songs = await db.Songs.AsNoTracking().Where(s => songIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        var rehearse = plan.Items.Where(i => i.SongId is { } id && songs.ContainsKey(id)).Select(i =>
            {
                var song = songs[i.SongId!.Value];
                var arrangement = song.Arrangements.FirstOrDefault(a => a.Id == i.ArrangementId) ?? song.Arrangements.FirstOrDefault();
                return new RehearseSongDto(i.Id, song.Id, song.Title, song.Author, i.Key ?? arrangement?.Key, arrangement?.Bpm, arrangement?.ChartUrl, arrangement?.AudioUrl, song.ReferenceUrl, song.Lyrics, arrangement?.Notes);
            })
            .ToList();
        var team = (await reader.AssignmentsAsync([planId], cancellationToken)).Where(a => a.Status != AssignmentStatus.Declined).ToList();
        return new RehearsePlanDto(plan.Id, plan.Title, plan.Date, plan.StartTime, plan.Notes, items, rehearse, team);
    }

    public async Task<MyAssignmentDto?> ScheduleForAsync(Assignment assignment, CancellationToken cancellationToken) =>
        (await ToMineAsync([assignment], cancellationToken)).FirstOrDefault();

    private async Task<(string Plan, string Team, string Position)> NamesAsync(Assignment a, CancellationToken cancellationToken)
    {
        var plan = await db.Plans.AsNoTracking().Where(p => p.Id == a.PlanId).Select(p => p.Title).SingleOrDefaultAsync(cancellationToken) ?? "Service";
        var team = await db.Teams.AsNoTracking().Where(t => t.Id == a.TeamId).Select(t => t.Name).SingleOrDefaultAsync(cancellationToken) ?? "Team";
        var position = await db.Positions.AsNoTracking().Where(p => p.Id == a.PositionId).Select(p => p.Name).SingleOrDefaultAsync(cancellationToken) ?? "Position";
        return (plan, team, position);
    }

    private async Task<List<MyAssignmentDto>> ToMineAsync(IReadOnlyList<Assignment> assignments, CancellationToken cancellationToken)
    {
        var planIds = assignments.Select(a => a.PlanId).Distinct().ToList();
        var plans = await db.Plans.AsNoTracking().Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var teamIds = assignments.Select(a => a.TeamId).Distinct().ToList();
        var teams = await db.Teams.AsNoTracking().Where(t => teamIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        var positionIds = assignments.Select(a => a.PositionId).Distinct().ToList();
        var positions = await db.Positions.AsNoTracking().Where(p => positionIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        return assignments.Where(a => plans.ContainsKey(a.PlanId)).Select(a => new MyAssignmentDto(
                a.Id,
                a.PlanId,
                plans[a.PlanId].Title,
                a.Date,
                plans[a.PlanId].StartTime,
                teams.GetValueOrDefault(a.TeamId) ?? "Team",
                positions.GetValueOrDefault(a.PositionId) ?? "Position",
                a.Status))
            .ToList();
    }
}

/// <summary>Answering from the email: the link carries a signed token instead of a sign-in.</summary>
public sealed class AnswerByLinkService(IServicesDb db, IAnswerLinks links, MyServingService serving)
{
    public async Task<Result<(MyAssignmentDto Assignment, Assignment Entity)>> FindAsync(string? token, CancellationToken cancellationToken)
    {
        var id = token is null ? null : links.Read(token);
        var assignment = id is { } assignmentId ? await db.Assignments.SingleOrDefaultAsync(a => a.Id == assignmentId, cancellationToken) : null;
        if (assignment is null)
        {
            return Error.NotFound("services.link_invalid", "This link has expired or is no longer valid. Open the Shapers app to answer.");
        }

        var dto = (await serving.ScheduleForAsync(assignment, cancellationToken))!;
        return (dto, assignment);
    }

    public async Task<Result<MyAssignmentDto>> AnswerAsync(string? token, bool accept, string? reason, CancellationToken cancellationToken)
    {
        var found = await FindAsync(token, cancellationToken);
        return found.IsFailure ? found.Error! : await serving.AnswerAsync(found.Value.Entity, new ServingAnswerRequest(accept, reason), cancellationToken);
    }
}

/// <summary>Sends the "can you serve?" and reminder emails with answer links. Pushes are sent by Communications.</summary>
public sealed class ServingEmails(IServicesDb db, IPeopleDirectory people, IEmailSender email, IAnswerLinks links, IOptions<ServicesOptions> options)
    : IIntegrationEventHandler<ServingRequestedIntegrationEvent>, IIntegrationEventHandler<ServingReminderIntegrationEvent>
{
    private static readonly CultureInfo SouthAfrica = CultureInfo.GetCultureInfo("en-ZA");

    public async Task HandleAsync(ServingRequestedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var assignment = await db.Assignments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == e.AssignmentId, cancellationToken);
        if (assignment is null || assignment.Status != AssignmentStatus.Pending)
        {
            return;
        }

        await SendAsync(e.PersonId, $"Can you serve on {Day(e.Date)}?",
            [$"You've been asked to serve as {e.Position} ({e.Team}) at {e.PlanTitle} on {Day(e.Date)}.", "Please let the team know whether you can make it."],
            "Answer", Link(e.AssignmentId), cancellationToken);
    }

    public async Task HandleAsync(ServingReminderIntegrationEvent e, CancellationToken cancellationToken) =>
        await SendAsync(e.PersonId, $"Reminder: you're serving on {Day(e.Date)}",
            [$"Thank you for serving as {e.Position} ({e.Team}) at {e.PlanTitle} on {Day(e.Date)}.", "If something has come up, let the team know as soon as you can."],
            "See the details or change your answer", Link(e.AssignmentId), cancellationToken);

    private async Task SendAsync(Guid personId, string subject, IReadOnlyList<string> paragraphs, string button, string url, CancellationToken cancellationToken)
    {
        var person = await people.GetAsync(personId, cancellationToken);
        if (person?.Email is not { } address)
        {
            return;
        }

        var greeting = $"Hi {person.DisplayName.Split(' ')[0]},";
        var lines = paragraphs.Prepend(greeting).ToList();
        await email.SendAsync(new EmailMessage(address, subject, string.Join("\n\n", lines.Append($"{button}: {url}")), EmailLayout.Html(subject, lines, button, url)), cancellationToken);
    }

    private string Link(Guid assignmentId) =>
        $"{(options.Value.PublicApiUrl ?? "http://localhost:5080").TrimEnd('/')}/api/services/answer?token={Uri.EscapeDataString(links.Token(assignmentId))}";

    private static string Day(DateOnly date) => date.ToString("dddd d MMMM", SouthAfrica);
}

/// <summary>Daily: reminders for people who said yes to a service in the next few days; away dates long past are deleted.</summary>
public sealed class ServingReminderJob(IServicesDb db, IOptions<ServicesOptions> options, TimeProvider clock)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var today = ServingTime.Today(clock);
        var until = today.AddDays(options.Value.ReminderDaysBefore);
        var due = await db.Assignments.Where(a => a.Status == AssignmentStatus.Accepted && a.RemindedAt == null && a.Date >= today && a.Date <= until).ToListAsync(cancellationToken);
        var planIds = due.Select(a => a.PlanId).Distinct().ToList();
        var plans = await db.Plans.AsNoTracking().Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken);
        var teams = await db.Teams.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        var positions = await db.Positions.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        var now = clock.GetUtcNow();
        var reminded = due.Count(a => a.Remind(plans.GetValueOrDefault(a.PlanId) ?? "Service", teams.GetValueOrDefault(a.TeamId) ?? "Team", positions.GetValueOrDefault(a.PositionId) ?? "Position", now));
        await db.SaveChangesAsync(cancellationToken);

        var stale = today.AddDays(-30);
        await db.Blockouts.Where(b => b.To < stale).ExecuteDeleteAsync(cancellationToken);
        return reminded;
    }
}

/// <summary>A person's teams, serving history and away dates, for export and erasure.</summary>
public sealed class ServicesPersonalData(IServicesDb db) : Shapers.Platform.Privacy.IPersonalDataSource
{
    public string Name => "Serving";

    public async Task<object?> ExportAsync(Guid personId, CancellationToken cancellationToken)
    {
        var teams = await db.Members.AsNoTracking().Where(m => m.PersonId == personId)
            .Join(db.Teams.AsNoTracking(), m => m.TeamId, t => t.Id, (m, t) => new { Team = t.Name, m.IsLeader, m.AddedAt })
            .ToListAsync(cancellationToken);
        var serving = await db.Assignments.AsNoTracking().Where(a => a.PersonId == personId).OrderBy(a => a.Date)
            .Select(a => new { a.Date, Status = a.Status.ToString(), a.DeclineReason }).ToListAsync(cancellationToken);
        var away = await db.Blockouts.AsNoTracking().Where(b => b.PersonId == personId).Select(b => new { b.From, b.To, b.Reason }).ToListAsync(cancellationToken);
        return teams.Count == 0 && serving.Count == 0 && away.Count == 0 ? null : new { Teams = teams, Serving = serving, Away = away };
    }

    public async Task<int> EraseAsync(Guid personId, CancellationToken cancellationToken) =>
        await db.Members.Where(m => m.PersonId == personId).ExecuteDeleteAsync(cancellationToken)
        + await db.Assignments.Where(a => a.PersonId == personId).ExecuteDeleteAsync(cancellationToken)
        + await db.Blockouts.Where(b => b.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
}

/// <summary>Two church records were merged: team places, serving and away dates move to the surviving record.</summary>
public sealed class ReplaceMergedServingPerson(IServicesDb db) : IIntegrationEventHandler<PeopleMergedIntegrationEvent>
{
    public async Task HandleAsync(PeopleMergedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var survivorTeams = await db.Members.Where(m => m.PersonId == e.SurvivorId).Select(m => m.TeamId).ToListAsync(cancellationToken);
        await db.Members.Where(m => m.PersonId == e.MergedId && survivorTeams.Contains(m.TeamId)).ExecuteDeleteAsync(cancellationToken);
        await db.Members.Where(m => m.PersonId == e.MergedId).ExecuteUpdateAsync(s => s.SetProperty(m => m.PersonId, e.SurvivorId), cancellationToken);
        await db.Assignments.Where(a => a.PersonId == e.MergedId).ExecuteUpdateAsync(s => s.SetProperty(a => a.PersonId, e.SurvivorId), cancellationToken);
        await db.Blockouts.Where(b => b.PersonId == e.MergedId).ExecuteUpdateAsync(s => s.SetProperty(b => b.PersonId, e.SurvivorId), cancellationToken);
    }
}
