using Microsoft.EntityFrameworkCore;
using Shapers.People.Contracts;
using Shapers.People.Domain;

namespace Shapers.People.Application;

/// <summary>Other modules' follow-ups land on the connect cards list, next to first-timers and decisions.</summary>
public sealed class FollowUps(IPeopleDb db, TimeProvider clock) : IFollowUps
{
    public async Task AddAsync(FollowUpRequest request, CancellationToken cancellationToken = default)
    {
        var person = await db.Persons.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.PersonId && p.Status != PersonStatus.Merged, cancellationToken);
        if (person is null || await db.ConnectCards.AnyAsync(c => c.PersonId == person.Id && c.SourceId == request.SourceId, cancellationToken))
        {
            return;
        }

        var note = request.Note.Length > 2000 ? request.Note[..2000] : request.Note;
        db.ConnectCards.Add(ConnectCard.Submit(person.Id, ScopePath.Parse(person.Scope), [ConnectReason.EventRegistration], note, request.Source, request.SourceId, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }
}
