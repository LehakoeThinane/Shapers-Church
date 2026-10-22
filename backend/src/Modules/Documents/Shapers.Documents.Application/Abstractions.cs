using Microsoft.EntityFrameworkCore;
using Shapers.Documents.Contracts;
using Shapers.Documents.Domain;
using Shapers.Platform.Authorization;

namespace Shapers.Documents.Application;

public interface IDocumentsDb
{
    DbSet<Library> Libraries { get; }

    DbSet<Folder> Folders { get; }

    DbSet<Document> Documents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class DocumentsPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(DocumentsPermissions.View, "documents", "Open shared documents"),
        new(DocumentsPermissions.Edit, "documents", "Upload, rename, move and delete shared documents"),
        new(DocumentsPermissions.Manage, "documents", "Create document libraries and choose who can use private ones"),
    ];
}

/// <summary>What someone may do in a library. Each level includes the ones before it.</summary>
public enum LibraryAccess
{
    None,
    View,
    Edit,
    Manage,
}

public sealed record LibraryDto(Guid Id, string Name, string Scope, bool IsPrivate, LibraryAccess Access);

public sealed record FolderDto(Guid Id, string Name, Guid? ParentId);

public sealed record DocumentDto(Guid Id, string Name, Guid? FolderId, string ContentType, long SizeBytes, int Version, DateTimeOffset UpdatedAt, string UpdatedBy);

public sealed record FolderViewDto(
    LibraryDto Library,
    FolderDto? Folder,
    IReadOnlyList<FolderDto> Breadcrumb,
    IReadOnlyList<FolderDto> Folders,
    IReadOnlyList<DocumentDto> Documents);

/// <summary>One document with where it lives and what the person may do with it (for the editor).</summary>
public sealed record DocumentDetailDto(DocumentDto Document, Guid LibraryId, string LibraryName, LibraryAccess Access);

public sealed record VersionDto(int Number, string ContentType, long SizeBytes, DateTimeOffset UploadedAt, string UploadedBy, bool IsCurrent);

/// <summary>A document found by search or listed as recent, with where it lives.</summary>
public sealed record DocumentHitDto(DocumentDto Document, Guid LibraryId, string LibraryName, string Path);

public sealed record BinItemDto(string Kind, Guid Id, string Name, Guid LibraryId, string LibraryName, DateTimeOffset DeletedAt);

public sealed record SaveLibraryRequest(string Name, string? Scope, bool IsPrivate);

public sealed record MemberInput(Guid PersonId, bool CanEdit);

public sealed record MemberDto(Guid PersonId, string Name, bool CanEdit);

public sealed record CreateFolderRequest(Guid LibraryId, Guid? ParentId, string Name);

public sealed record RenameRequest(string Name);

public sealed record MoveRequest(Guid? FolderId);

/// <summary>A file to send to the browser.</summary>
public sealed record FileContent(Stream Content, string ContentType, string FileName);
