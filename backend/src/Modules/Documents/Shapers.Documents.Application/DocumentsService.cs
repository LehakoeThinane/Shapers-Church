using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.Documents.Contracts;
using Shapers.Documents.Domain;
using Shapers.Media.Contracts;
using Shapers.People.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;

namespace Shapers.Documents.Application;

/// <summary>
/// Shared documents for staff and leaders. Every request checks the person's access to the library, so a
/// private library's files can't be reached by guessing an ID; files are streamed through the API, never linked.
/// </summary>
public sealed class DocumentsService(
    IDocumentsDb db,
    IPrivateFileStore files,
    IAuthorizer authorizer,
    ICurrentUser currentUser,
    IChurchDirectory church,
    IPeopleDirectory people,
    IAuditLog audit,
    TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("documents.not_found", "That isn't here, or you don't have access to it.");
    private static readonly Error ReadOnly = Error.Forbidden("documents.read_only", "You can open this library's files but not change them.");

    // ---------- Libraries ----------

    public async Task<IReadOnlyList<LibraryDto>> LibrariesAsync(CancellationToken cancellationToken)
    {
        var libraries = await db.Libraries.AsNoTracking().OrderBy(l => l.Name).ToListAsync(cancellationToken);
        var visible = new List<LibraryDto>();
        foreach (var library in libraries)
        {
            var access = await AccessAsync(library, cancellationToken);
            if (access > LibraryAccess.None)
            {
                visible.Add(ToDto(library, access));
            }
        }

        return visible;
    }

    public async Task<Result<LibraryDto>> CreateLibraryAsync(SaveLibraryRequest request, CancellationToken cancellationToken)
    {
        ScopePath? scope = string.IsNullOrWhiteSpace(request.Scope)
            ? ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path)
            : ScopePath.TryParse(request.Scope, out var parsed) && await church.ScopeExistsAsync(parsed.Value, cancellationToken) ? parsed : null;
        if (scope is null)
        {
            return new Error("documents.scope", "Choose the church, a campus or a ministry.");
        }

        if (!await authorizer.CanAsync(DocumentsPermissions.Manage, scope, cancellationToken))
        {
            return Error.Forbidden("documents.forbidden", "You can't create libraries here.");
        }

        var library = Library.Create(request.Name, scope, request.IsPrivate, clock.GetUtcNow());
        if (await db.Libraries.AnyAsync(l => l.Name == library.Name && l.Scope == library.Scope, cancellationToken))
        {
            return new Error("documents.library_exists", "There's already a library with that name here.");
        }

        db.Libraries.Add(library);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("documents.library.created", "library", library.Id, library, new { library.Name, library.IsPrivate }, cancellationToken);
        return ToDto(library, LibraryAccess.Manage);
    }

    public async Task<Result<LibraryDto>> UpdateLibraryAsync(Guid id, SaveLibraryRequest request, CancellationToken cancellationToken)
    {
        var library = await db.Libraries.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (library is null || await AccessAsync(library, cancellationToken) < LibraryAccess.Manage)
        {
            return NotFound;
        }

        library.Update(request.Name, request.IsPrivate, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("documents.library.updated", "library", library.Id, library, new { library.Name, library.IsPrivate }, cancellationToken);
        return ToDto(library, LibraryAccess.Manage);
    }

    public async Task<Result> DeleteLibraryAsync(Guid id, CancellationToken cancellationToken)
    {
        var library = await db.Libraries.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (library is null || await AccessAsync(library, cancellationToken) < LibraryAccess.Manage)
        {
            return NotFound;
        }

        if (await db.Folders.AnyAsync(f => f.LibraryId == id, cancellationToken) || await db.Documents.AnyAsync(d => d.LibraryId == id, cancellationToken))
        {
            return new Error("documents.library_not_empty", "Empty the library (and its recycle bin) before deleting it.");
        }

        db.Libraries.Remove(library);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("documents.library.deleted", "library", library.Id, library, new { library.Name }, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<MemberDto>>> MembersAsync(Guid id, CancellationToken cancellationToken)
    {
        var library = await db.Libraries.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (library is null || await AccessAsync(library, cancellationToken) < LibraryAccess.Manage)
        {
            return NotFound;
        }

        var names = await people.GetManyAsync(library.Members.Select(m => m.PersonId).ToList(), cancellationToken);
        return library.Members
            .Select(m => new MemberDto(m.PersonId, names.GetValueOrDefault(m.PersonId)?.DisplayName ?? "Unknown", m.CanEdit))
            .OrderBy(m => m.Name)
            .ToList();
    }

    public async Task<Result<IReadOnlyList<MemberDto>>> SetMembersAsync(Guid id, IReadOnlyList<MemberInput> members, CancellationToken cancellationToken)
    {
        var library = await db.Libraries.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (library is null || await AccessAsync(library, cancellationToken) < LibraryAccess.Manage)
        {
            return NotFound;
        }

        var known = await people.GetManyAsync(members.Select(m => m.PersonId).Distinct().ToList(), cancellationToken);
        if (members.Any(m => !known.ContainsKey(m.PersonId)))
        {
            return new Error("documents.unknown_person", "One of the people chosen isn't in the church's records.");
        }

        library.SetMembers(members.Select(m => new LibraryMember(m.PersonId, m.CanEdit)), clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("documents.library.members_changed", "library", library.Id, library, new { count = members.Count }, cancellationToken);
        return await MembersAsync(id, cancellationToken);
    }

    // ---------- Browsing ----------

    public async Task<Result<FolderViewDto>> FolderAsync(Guid libraryId, Guid? folderId, CancellationToken cancellationToken)
    {
        var (library, access) = await LibraryWithAccessAsync(libraryId, cancellationToken);
        if (library is null || access < LibraryAccess.View)
        {
            return NotFound;
        }

        var folders = await db.Folders.AsNoTracking().Where(f => f.LibraryId == libraryId && f.DeletedAt == null).ToListAsync(cancellationToken);
        var byId = folders.ToDictionary(f => f.Id);
        if (folderId is { } fid && !byId.ContainsKey(fid))
        {
            return NotFound;
        }

        var documents = await db.Documents.AsNoTracking()
            .Where(d => d.LibraryId == libraryId && d.FolderId == folderId && d.DeletedAt == null)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

        return new FolderViewDto(
            ToDto(library, access),
            folderId is { } id ? ToDto(byId[id]) : null,
            Breadcrumb(folderId, byId),
            [.. folders.Where(f => f.ParentId == folderId).OrderBy(f => f.Name).Select(ToDto)],
            [.. documents.Select(ToDto)]);
    }

    public async Task<IReadOnlyList<DocumentHitDto>> SearchAsync(string? query, CancellationToken cancellationToken)
    {
        var text = query?.Trim() ?? string.Empty;
        if (text.Length < 2)
        {
            return [];
        }

        var pattern = $"%{Exactly(text)}%";
        return await HitsAsync(q => q.Where(d => EF.Functions.ILike(d.Name, pattern)).OrderBy(d => d.Name), cancellationToken);
    }

    public Task<IReadOnlyList<DocumentHitDto>> RecentAsync(CancellationToken cancellationToken) =>
        HitsAsync(q => q.OrderByDescending(d => d.UpdatedAt), cancellationToken);

    // ---------- Folders ----------

    public async Task<Result<FolderDto>> CreateFolderAsync(CreateFolderRequest request, CancellationToken cancellationToken)
    {
        var (library, access) = await LibraryWithAccessAsync(request.LibraryId, cancellationToken);
        if (library is null || access < LibraryAccess.View)
        {
            return NotFound;
        }

        if (access < LibraryAccess.Edit)
        {
            return ReadOnly;
        }

        if (request.ParentId is { } parent && !await db.Folders.AnyAsync(f => f.Id == parent && f.LibraryId == library.Id && f.DeletedAt == null, cancellationToken))
        {
            return NotFound;
        }

        var folder = Folder.Create(library.Id, request.ParentId, request.Name, clock.GetUtcNow());
        if (await db.Folders.AnyAsync(f => f.LibraryId == library.Id && f.ParentId == request.ParentId && f.DeletedAt == null && EF.Functions.ILike(f.Name, Exactly(folder.Name)), cancellationToken))
        {
            return new Error("documents.folder_exists", "There's already a folder with that name here.");
        }

        db.Folders.Add(folder);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(folder);
    }

    public async Task<Result<FolderDto>> RenameFolderAsync(Guid id, RenameRequest request, CancellationToken cancellationToken) =>
        await ChangeFolderAsync(id, (folder, _) =>
        {
            folder.Rename(request.Name);
            return Task.FromResult<Error?>(null);
        }, cancellationToken);

    public async Task<Result<FolderDto>> MoveFolderAsync(Guid id, MoveRequest request, CancellationToken cancellationToken) =>
        await ChangeFolderAsync(id, async (folder, all) =>
        {
            // Not into itself or anything inside it.
            for (var at = request.FolderId; at is { } a; at = all.GetValueOrDefault(a)?.ParentId)
            {
                if (a == folder.Id)
                {
                    return new Error("documents.move_into_itself", "A folder can't go inside itself.");
                }

                if (!all.ContainsKey(a))
                {
                    return NotFound;
                }
            }

            folder.MoveTo(request.FolderId);
            return await Task.FromResult<Error?>(null);
        }, cancellationToken);

    public async Task<Result> DeleteFolderAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await ChangeFolderAsync(id, (folder, _) =>
        {
            folder.Delete(clock.GetUtcNow());
            return Task.FromResult<Error?>(null);
        }, cancellationToken);
        return result.IsSuccess ? Result.Success() : result.Error!;
    }

    // ---------- Documents ----------

    /// <summary>Saves an upload. A file with the same name in the same folder becomes its next version.</summary>
    public async Task<Result<DocumentDto>> UploadAsync(Guid libraryId, Guid? folderId, string fileName, string? contentType, long length, Stream content, CancellationToken cancellationToken)
    {
        var (library, access) = await LibraryWithAccessAsync(libraryId, cancellationToken);
        if (library is null || access < LibraryAccess.View)
        {
            return NotFound;
        }

        if (access < LibraryAccess.Edit)
        {
            return ReadOnly;
        }

        if (length is <= 0 or > Document.MaxBytes)
        {
            return new Error("documents.size", "Files can be up to 100 MB.");
        }

        if (folderId is { } fid && !await db.Folders.AnyAsync(f => f.Id == fid && f.LibraryId == libraryId && f.DeletedAt == null, cancellationToken))
        {
            return NotFound;
        }

        var name = DocumentNames.Check(Path.GetFileName(fileName), "file");
        var now = clock.GetUtcNow();
        var document = await db.Documents.SingleOrDefaultAsync(
            d => d.LibraryId == libraryId && d.FolderId == folderId && d.DeletedAt == null && EF.Functions.ILike(d.Name, Exactly(name)), cancellationToken);
        if (document is null)
        {
            document = Document.Create(libraryId, folderId, name, now);
            db.Documents.Add(document);
        }

        var type = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Split(';')[0].Trim().ToLowerInvariant();
        var key = document.NextStorageKey(name);
        await files.SaveAsync(key, content, type, cancellationToken);
        var dropped = document.AddVersion(key, type, length, currentUser.PersonId, await MyNameAsync(cancellationToken), now);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var old in dropped)
        {
            await files.DeleteAsync(old, cancellationToken);
        }

        await AuditAsync("documents.document.uploaded", "document", document.Id, library, new { document.Name, version = document.Current.Number }, cancellationToken);
        return ToDto(document);
    }

    /// <summary>Opens a document (the current version, or an earlier one) for preview or download. Logged.</summary>
    public async Task<Result<FileContent>> OpenAsync(Guid id, int? version, CancellationToken cancellationToken)
    {
        var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.DeletedAt == null, cancellationToken);
        if (document is null)
        {
            return NotFound;
        }

        var (library, access) = await LibraryWithAccessAsync(document.LibraryId, cancellationToken);
        var chosen = version is { } n ? document.Versions.FirstOrDefault(v => v.Number == n) : document.Current;
        if (library is null || access < LibraryAccess.View || chosen is null)
        {
            return NotFound;
        }

        Stream stream;
        try
        {
            stream = await files.OpenReadAsync(chosen.StorageKey, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return Error.NotFound("documents.file_missing", "This file's content is missing from storage. Upload it again.");
        }

        await AuditAsync("documents.document.opened", "document", document.Id, library, new { document.Name, version = chosen.Number }, cancellationToken);
        return new FileContent(stream, chosen.ContentType, document.Name);
    }

    public async Task<Result<IReadOnlyList<VersionDto>>> VersionsAsync(Guid id, CancellationToken cancellationToken)
    {
        var (document, _, error) = await DocumentForAsync(id, LibraryAccess.View, cancellationToken);
        if (document is null)
        {
            return error!;
        }

        return document.Versions
            .OrderByDescending(v => v.Number)
            .Select(v => new VersionDto(v.Number, v.ContentType, v.SizeBytes, v.UploadedAt, v.UploadedByName, v.Number == document.Current.Number))
            .ToList();
    }

    public async Task<Result<DocumentDto>> RestoreVersionAsync(Guid id, int number, CancellationToken cancellationToken)
    {
        var (document, library, error) = await DocumentForAsync(id, LibraryAccess.Edit, cancellationToken);
        if (document is null)
        {
            return error!;
        }

        document.RestoreVersion(number, currentUser.PersonId, await MyNameAsync(cancellationToken), clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("documents.document.version_restored", "document", document.Id, library!, new { document.Name, restored = number }, cancellationToken);
        return ToDto(document);
    }

    public async Task<Result<DocumentDto>> RenameDocumentAsync(Guid id, RenameRequest request, CancellationToken cancellationToken)
    {
        var (document, _, error) = await DocumentForAsync(id, LibraryAccess.Edit, cancellationToken);
        if (document is null)
        {
            return error!;
        }

        document.Rename(request.Name, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(document);
    }

    public async Task<Result<DocumentDto>> MoveDocumentAsync(Guid id, MoveRequest request, CancellationToken cancellationToken)
    {
        var (document, _, error) = await DocumentForAsync(id, LibraryAccess.Edit, cancellationToken);
        if (document is null)
        {
            return error!;
        }

        if (request.FolderId is { } fid && !await db.Folders.AnyAsync(f => f.Id == fid && f.LibraryId == document.LibraryId && f.DeletedAt == null, cancellationToken))
        {
            return NotFound;
        }

        document.MoveTo(request.FolderId, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(document);
    }

    public async Task<Result> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken)
    {
        var (document, library, error) = await DocumentForAsync(id, LibraryAccess.Edit, cancellationToken);
        if (document is null)
        {
            return error!;
        }

        document.Delete(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("documents.document.deleted", "document", document.Id, library!, new { document.Name }, cancellationToken);
        return Result.Success();
    }

    // ---------- Recycle bin ----------

    public async Task<IReadOnlyList<BinItemDto>> BinAsync(CancellationToken cancellationToken)
    {
        var libraries = await EditableLibrariesAsync(cancellationToken);
        var ids = libraries.Keys.ToList();
        var folders = await db.Folders.AsNoTracking().Where(f => ids.Contains(f.LibraryId) && f.DeletedAt != null).ToListAsync(cancellationToken);
        var documents = await db.Documents.AsNoTracking().Where(d => ids.Contains(d.LibraryId) && d.DeletedAt != null).ToListAsync(cancellationToken);
        return folders.Select(f => new BinItemDto("folder", f.Id, f.Name, f.LibraryId, libraries[f.LibraryId].Name, f.DeletedAt!.Value))
            .Concat(documents.Select(d => new BinItemDto("document", d.Id, d.Name, d.LibraryId, libraries[d.LibraryId].Name, d.DeletedAt!.Value)))
            .OrderByDescending(i => i.DeletedAt)
            .ToList();
    }

    /// <summary>Puts a deleted file or folder back. If the folder it was in has gone, it goes back to the top of the library.</summary>
    public async Task<Result> RestoreAsync(string kind, Guid id, CancellationToken cancellationToken)
    {
        var editable = await EditableLibrariesAsync(cancellationToken);
        if (kind == "folder")
        {
            var folder = await db.Folders.SingleOrDefaultAsync(f => f.Id == id && f.DeletedAt != null, cancellationToken);
            if (folder is null || !editable.ContainsKey(folder.LibraryId))
            {
                return NotFound;
            }

            if (folder.ParentId is { } parent && !await db.Folders.AnyAsync(f => f.Id == parent && f.DeletedAt == null, cancellationToken))
            {
                folder.MoveTo(null);
            }

            folder.Restore();
        }
        else
        {
            var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == id && d.DeletedAt != null, cancellationToken);
            if (document is null || !editable.ContainsKey(document.LibraryId))
            {
                return NotFound;
            }

            if (document.FolderId is { } folderId && !await db.Folders.AnyAsync(f => f.Id == folderId && f.DeletedAt == null, cancellationToken))
            {
                document.MoveTo(null, clock.GetUtcNow());
            }

            document.Restore();
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ---------- Helpers ----------

    public async Task<LibraryAccess> AccessAsync(Library library, CancellationToken cancellationToken)
    {
        var scope = ScopePath.Parse(library.Scope);
        if (await authorizer.CanAsync(DocumentsPermissions.Manage, scope, cancellationToken))
        {
            return LibraryAccess.Manage;
        }

        if (library.IsPrivate)
        {
            // Members still need to be staff who can use documents at all.
            var member = library.MemberFor(currentUser.PersonId);
            return member is null || !await authorizer.HasAnywhereAsync(DocumentsPermissions.View, cancellationToken)
                ? LibraryAccess.None
                : member.CanEdit ? LibraryAccess.Edit : LibraryAccess.View;
        }

        if (await authorizer.CanAsync(DocumentsPermissions.Edit, scope, cancellationToken))
        {
            return LibraryAccess.Edit;
        }

        return await authorizer.CanAsync(DocumentsPermissions.View, scope, cancellationToken) ? LibraryAccess.View : LibraryAccess.None;
    }

    private async Task<(Library? Library, LibraryAccess Access)> LibraryWithAccessAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        var library = await db.Libraries.AsNoTracking().SingleOrDefaultAsync(l => l.Id == libraryId, cancellationToken);
        return library is null ? (null, LibraryAccess.None) : (library, await AccessAsync(library, cancellationToken));
    }

    private async Task<(Document? Document, Library? Library, Error? Error)> DocumentForAsync(Guid id, LibraryAccess needed, CancellationToken cancellationToken)
    {
        var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == id && d.DeletedAt == null, cancellationToken);
        if (document is null)
        {
            return (null, null, NotFound);
        }

        var (library, access) = await LibraryWithAccessAsync(document.LibraryId, cancellationToken);
        if (library is null || access < LibraryAccess.View)
        {
            return (null, null, NotFound);
        }

        return access < needed ? (null, null, ReadOnly) : (document, library, null);
    }

    private async Task<Result<FolderDto>> ChangeFolderAsync(Guid id, Func<Folder, IReadOnlyDictionary<Guid, Folder>, Task<Error?>> change, CancellationToken cancellationToken)
    {
        var folder = await db.Folders.SingleOrDefaultAsync(f => f.Id == id && f.DeletedAt == null, cancellationToken);
        if (folder is null)
        {
            return NotFound;
        }

        var (library, access) = await LibraryWithAccessAsync(folder.LibraryId, cancellationToken);
        if (library is null || access < LibraryAccess.View)
        {
            return NotFound;
        }

        if (access < LibraryAccess.Edit)
        {
            return ReadOnly;
        }

        var all = await db.Folders.AsNoTracking().Where(f => f.LibraryId == folder.LibraryId && f.DeletedAt == null).ToDictionaryAsync(f => f.Id, cancellationToken);
        if (await change(folder, all) is { } error)
        {
            return error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(folder);
    }

    private async Task<Dictionary<Guid, Library>> EditableLibrariesAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Library>();
        foreach (var library in await db.Libraries.AsNoTracking().ToListAsync(cancellationToken))
        {
            if (await AccessAsync(library, cancellationToken) >= LibraryAccess.Edit)
            {
                result[library.Id] = library;
            }
        }

        return result;
    }

    /// <summary>Documents in libraries the person can open, outside the recycle bin (including inside deleted folders).</summary>
    private async Task<IReadOnlyList<DocumentHitDto>> HitsAsync(Func<IQueryable<Document>, IQueryable<Document>> shape, CancellationToken cancellationToken)
    {
        var libraries = (await LibrariesAsync(cancellationToken)).ToDictionary(l => l.Id);
        var ids = libraries.Keys.ToList();
        var folders = await db.Folders.AsNoTracking().Where(f => ids.Contains(f.LibraryId)).ToDictionaryAsync(f => f.Id, cancellationToken);
        var candidates = await shape(db.Documents.AsNoTracking().Where(d => ids.Contains(d.LibraryId) && d.DeletedAt == null)).Take(200).ToListAsync(cancellationToken);
        return candidates
            .Where(d => !InDeletedFolder(d.FolderId, folders))
            .Take(50)
            .Select(d => new DocumentHitDto(ToDto(d), d.LibraryId, libraries[d.LibraryId].Name, string.Join(" / ", Breadcrumb(d.FolderId, folders).Select(f => f.Name))))
            .ToList();
    }

    private static bool InDeletedFolder(Guid? folderId, Dictionary<Guid, Folder> folders)
    {
        for (var at = folderId; at is { } id && folders.TryGetValue(id, out var folder); at = folder.ParentId)
        {
            if (folder.DeletedAt is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A LIKE pattern matching this text exactly (ignoring case with ILIKE): the wildcards in it are escaped.</summary>
    private static string Exactly(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private static List<FolderDto> Breadcrumb(Guid? folderId, Dictionary<Guid, Folder> folders)
    {
        var trail = new List<FolderDto>();
        for (var at = folderId; at is { } id && folders.TryGetValue(id, out var folder) && trail.Count < 50; at = folder.ParentId)
        {
            trail.Insert(0, ToDto(folder));
        }

        return trail;
    }

    private async Task<string> MyNameAsync(CancellationToken cancellationToken) =>
        currentUser.PersonId is { } id ? (await people.GetAsync(id, cancellationToken))?.DisplayName ?? "Staff" : "Staff";

    private Task AuditAsync(string action, string type, Guid id, Library library, object details, CancellationToken cancellationToken) =>
        audit.RecordAsync(new AuditRecord(action, type, id.ToString(), ScopePath.Parse(library.Scope), details), cancellationToken);

    private static LibraryDto ToDto(Library l, LibraryAccess access) => new(l.Id, l.Name, l.Scope, l.IsPrivate, access);

    private static FolderDto ToDto(Folder f) => new(f.Id, f.Name, f.ParentId);

    private static DocumentDto ToDto(Document d) =>
        new(d.Id, d.Name, d.FolderId, d.Current.ContentType, d.Current.SizeBytes, d.Current.Number, d.UpdatedAt, d.Current.UploadedByName);
}

/// <summary>Daily: anything in the recycle bin for 30 days is deleted for good, files included.</summary>
public sealed class DocumentsRecycleJob(IDocumentsDb db, IPrivateFileStore files, TimeProvider clock)
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow() - KeepFor;
        var expiredFolders = await db.Folders.Where(f => f.DeletedAt != null && f.DeletedAt < cutoff).ToListAsync(cancellationToken);

        // Everything inside an expired folder goes with it.
        var libraryIds = expiredFolders.Select(f => f.LibraryId).Distinct().ToList();
        var all = await db.Folders.Where(f => libraryIds.Contains(f.LibraryId)).ToListAsync(cancellationToken);
        var doomed = new HashSet<Guid>(expiredFolders.Select(f => f.Id));
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var folder in all.Where(f => f.ParentId is { } p && doomed.Contains(p) && !doomed.Contains(f.Id)))
            {
                grew = doomed.Add(folder.Id) || grew;
            }
        }

        var documents = await db.Documents
            .Where(d => (d.DeletedAt != null && d.DeletedAt < cutoff) || (d.FolderId != null && doomed.Contains(d.FolderId.Value)))
            .ToListAsync(cancellationToken);
        foreach (var key in documents.SelectMany(d => d.Versions.Select(v => v.StorageKey)).Distinct())
        {
            await files.DeleteAsync(key, cancellationToken);
        }

        db.Documents.RemoveRange(documents);
        db.Folders.RemoveRange(all.Where(f => doomed.Contains(f.Id)));
        await db.SaveChangesAsync(cancellationToken);
        return documents.Count;
    }
}

/// <summary>Files a person uploaded: listed in their export; on erasure their name comes off, the church keeps the files.</summary>
public sealed class DocumentsPersonalData(IDocumentsDb db) : Shapers.Platform.Privacy.IPersonalDataSource
{
    public string Name => "Shared documents uploaded";

    public async Task<object?> ExportAsync(Guid personId, CancellationToken cancellationToken)
    {
        var documents = await db.Documents.AsNoTracking().Where(d => d.Versions.Any(v => v.UploadedByPersonId == personId)).ToListAsync(cancellationToken);
        var mine = documents
            .SelectMany(d => d.Versions.Where(v => v.UploadedByPersonId == personId).Select(v => new { Document = d.Name, Version = v.Number, v.UploadedAt }))
            .ToList();
        return mine.Count == 0 ? null : mine;
    }

    public async Task<int> EraseAsync(Guid personId, CancellationToken cancellationToken)
    {
        var documents = await db.Documents.Where(d => d.Versions.Any(v => v.UploadedByPersonId == personId)).ToListAsync(cancellationToken);
        var changed = documents.Count(d => d.ForgetUploader(personId));
        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }
}
