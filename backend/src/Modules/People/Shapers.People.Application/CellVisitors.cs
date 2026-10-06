using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shapers.Groups.Contracts;
using Shapers.People.Contracts;
using Shapers.People.Domain;
using Shapers.Platform.Messaging;

namespace Shapers.People.Application;

/// <summary>
/// Visitors at a home cell who agreed to be contacted become connect cards, so the follow-up team welcomes them
/// like anyone else. Each gets a new record (flagged for duplicate review), never attached to an existing one.
/// </summary>
public sealed partial class FileCellVisitors(IPeopleDb db, IGuestRecords guests, TimeProvider clock, ILogger<FileCellVisitors> logger)
    : IIntegrationEventHandler<CellVisitorsRecordedIntegrationEvent>
{
    public async Task HandleAsync(CellVisitorsRecordedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var message = $"Visited the {e.CellName} home cell on {e.MeetingDate:d MMMM yyyy}.";
        foreach (var visitor in e.Visitors)
        {
            // The leader confirmed the visitor agreed to be contacted; that's recorded as their consent.
            var person = await guests.CreateAsync(
                new GuestDetails(visitor.FirstName, visitor.LastName ?? "(cell visitor)", visitor.Mobile, visitor.Email, EmailVerified: false, ConsentToKeepDetails: true,
                    GuestOrigin.CellGroup, FromWebsite: false, PolicyVersion: null),
                cancellationToken);
            if (person.IsFailure)
            {
                LogSkipped(logger, e.ReportId, person.Error!.Code);
                continue;
            }

            var scope = await db.Persons.Where(p => p.Id == person.Value).Select(p => p.Scope).SingleAsync(cancellationToken);
            db.ConnectCards.Add(ConnectCard.Submit(person.Value, ScopePath.Parse(scope), [ConnectReason.FirstTime], message, "cell", e.ReportId, clock.GetUtcNow()));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A visitor on cell report {ReportId} couldn't be filed: {Reason}")]
    private static partial void LogSkipped(ILogger logger, Guid reportId, string reason);
}
