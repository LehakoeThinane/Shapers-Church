using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Platform.Text;
using Shapers.Services.Contracts;
using Shapers.Services.Domain;

namespace Shapers.Services.Application;

/// <summary>
/// The song library. Anyone planning or scheduling can read it; services.songs.edit keeps it. How often and how
/// recently a song was used comes from the plans, which also gives the CCLI usage report.
/// </summary>
public sealed class SongService(IServicesDb db, IChurchDirectory church, IAuthorizer authorizer, IAuditLog audit, TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("services.song_not_found", "Song not found.");

    public async Task<Result<IReadOnlyList<SongSummaryDto>>> ListAsync(string? q, bool includeArchived, CancellationToken cancellationToken)
    {
        if (!await CanReadAsync(cancellationToken))
        {
            return Error.Forbidden("services.forbidden", "You don't have access to the song library.");
        }

        var query = db.Songs.AsNoTracking().Where(s => includeArchived || !s.IsArchived);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = LikePattern.Contains(q.Trim());
            query = query.Where(s => EF.Functions.ILike(s.Title, pattern) || (s.Author != null && EF.Functions.ILike(s.Author, pattern)) || s.CcliNumber == q.Trim());
        }

        var songs = await query.OrderBy(s => s.Title).Take(500).ToListAsync(cancellationToken);
        var usage = await UsageAsync(null, ServingTime.Today(clock), cancellationToken);
        return songs.Select(s =>
            {
                var used = usage.GetValueOrDefault(s.Id) ?? [];
                return new SongSummaryDto(s.Id, s.Title, s.Author, s.CcliNumber, s.Themes, s.Arrangements.Select(a => a.Key).OfType<string>().Distinct().ToList(),
                    used.Count > 0 ? used.Max() : null, used.Count, s.IsArchived);
            })
            .ToList();
    }

    public async Task<Result<SongDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var song = await db.Songs.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        return song is null || !await CanReadAsync(cancellationToken) ? NotFound : await ToDtoAsync(song, cancellationToken);
    }

    public async Task<Result<SongDto>> CreateAsync(SaveSongRequest request, CancellationToken cancellationToken)
    {
        var scope = await ServingTime.ChooseScopeAsync(authorizer, church, ServicesPermissions.SongsEdit, null, cancellationToken);
        if (scope.IsFailure)
        {
            return scope.Error!;
        }

        if (request.CcliNumber is { Length: > 0 } ccli && await db.Songs.AnyAsync(s => s.CcliNumber == ccli.Trim(), cancellationToken))
        {
            return Error.Conflict("services.song_exists", "A song with that CCLI number is already in the library.");
        }

        var now = clock.GetUtcNow();
        var song = Song.Create(request.Title, scope.Value, now);
        song.Update(request.Title, request.Author, request.CcliNumber, request.Themes, request.Lyrics, request.ReferenceUrl, request.Arrangements, now);
        db.Songs.Add(song);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("services.song.created", song, cancellationToken);
        return await ToDtoAsync(song, cancellationToken);
    }

    public Task<Result<SongDto>> UpdateAsync(Guid id, SaveSongRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, "services.song.updated", s => s.Update(request.Title, request.Author, request.CcliNumber, request.Themes, request.Lyrics, request.ReferenceUrl, request.Arrangements, clock.GetUtcNow()), cancellationToken);

    public Task<Result<SongDto>> ArchiveAsync(Guid id, bool archive, CancellationToken cancellationToken) =>
        ChangeAsync(id, archive ? "services.song.archived" : "services.song.restored", s =>
        {
            if (archive)
            {
                s.Archive();
            }
            else
            {
                s.Restore();
            }
        }, cancellationToken);

    /// <summary>Songs used in services between two dates, with CCLI numbers: what the church reports to CCLI.</summary>
    public async Task<Result<IReadOnlyList<SongUsageDto>>> ReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (!await CanReadAsync(cancellationToken))
        {
            return Error.Forbidden("services.forbidden", "You don't have access to the song library.");
        }

        var usage = await UsageAsync(from, to, cancellationToken);
        var ids = usage.Keys.ToList();
        var songs = await db.Songs.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync(cancellationToken);
        return songs
            .Select(s => new SongUsageDto(s.Id, s.Title, s.Author, s.CcliNumber, usage[s.Id].Count, usage[s.Id].Order().ToList()))
            .OrderByDescending(u => u.Times).ThenBy(u => u.Title)
            .ToList();
    }

    /// <summary>For each song, the dates of the services it was in (once per service).</summary>
    private async Task<Dictionary<Guid, List<DateOnly>>> UsageAsync(DateOnly? from, DateOnly to, CancellationToken cancellationToken)
    {
        var plans = await db.Plans.AsNoTracking()
            .Where(p => (from == null || p.Date >= from) && p.Date <= to)
            .Select(p => new { p.Date, p.Items })
            .ToListAsync(cancellationToken);
        return plans
            .SelectMany(p => p.Items.Where(i => i.SongId is not null).Select(i => i.SongId!.Value).Distinct().Select(id => (id, p.Date)))
            .GroupBy(x => x.id)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Date).ToList());
    }

    private async Task<Result<SongDto>> ChangeAsync(Guid id, string action, Action<Song> change, CancellationToken cancellationToken)
    {
        var song = await db.Songs.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (song is null || !await authorizer.CanAsync(ServicesPermissions.SongsEdit, ScopePath.Parse(song.Scope), cancellationToken))
        {
            return NotFound;
        }

        change(song);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(action, song, cancellationToken);
        return await ToDtoAsync(song, cancellationToken);
    }

    private async Task<SongDto> ToDtoAsync(Song s, CancellationToken cancellationToken)
    {
        var used = (await UsageAsync(null, ServingTime.Today(clock), cancellationToken)).GetValueOrDefault(s.Id) ?? [];
        return new SongDto(s.Id, s.Title, s.Author, s.CcliNumber, s.Themes, s.Lyrics, s.ReferenceUrl, s.Arrangements, used.Count > 0 ? used.Max() : null, used.Count, s.IsArchived);
    }

    private async Task<bool> CanReadAsync(CancellationToken cancellationToken) =>
        await authorizer.HasAnywhereAsync(ServicesPermissions.SongsEdit, cancellationToken)
        || await authorizer.HasAnywhereAsync(ServicesPermissions.PlansEdit, cancellationToken)
        || await authorizer.HasAnywhereAsync(ServicesPermissions.Schedule, cancellationToken);

    private Task AuditAsync(string action, Song song, CancellationToken cancellationToken) =>
        audit.RecordAsync(new AuditRecord(action, "song", song.Id.ToString(), ScopePath.Parse(song.Scope), new { song.Title }), cancellationToken);
}
