namespace Shapers.Documents.Domain;

/// <summary>Names for libraries, folders and files: what people type, checked once, used everywhere.</summary>
public static class DocumentNames
{
    public const int MaxLength = 200;
    private static readonly System.Buffers.SearchValues<char> Forbidden = System.Buffers.SearchValues.Create("/\\:*?\"<>|");

    public static string Check(string? name, string what)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxLength || trimmed.IndexOfAny(Forbidden) >= 0 || trimmed.Any(char.IsControl) || trimmed is "." or "..")
        {
            throw new DomainRuleException("documents.name", $"Give the {what} a name of up to {MaxLength} characters, without / \\ : * ? \" < > |.");
        }

        return trimmed;
    }
}

/// <summary>Someone with access to a private library.</summary>
public sealed record LibraryMember(Guid PersonId, bool CanEdit);

/// <summary>
/// A top-level shared folder, e.g. "Worship team". Belongs to the church, a campus or a ministry: people with the
/// documents permissions there can use it. A private library is only for the people listed (and document managers).
/// </summary>
public sealed class Library : AggregateRoot<Guid>
{
    private readonly List<LibraryMember> _members = [];

    private Library()
    {
    }

    public string Name { get; private set; } = null!;

    public string Scope { get; private set; } = null!;

    public bool IsPrivate { get; private set; }

    public IReadOnlyList<LibraryMember> Members => _members;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Library Create(string name, ScopePath scope, bool isPrivate, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        Name = DocumentNames.Check(name, "library"),
        Scope = scope.Value,
        IsPrivate = isPrivate,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void Update(string name, bool isPrivate, DateTimeOffset now)
    {
        Name = DocumentNames.Check(name, "library");
        IsPrivate = isPrivate;
        UpdatedAt = now;
    }

    public void SetMembers(IEnumerable<LibraryMember> members, DateTimeOffset now)
    {
        var distinct = members.GroupBy(m => m.PersonId).Select(g => g.Last()).ToList();
        if (distinct.Count > 500)
        {
            throw new DomainRuleException("documents.members", "A private library can have up to 500 people. Use a campus or ministry library for more.");
        }

        _members.Clear();
        _members.AddRange(distinct);
        UpdatedAt = now;
    }

    public LibraryMember? MemberFor(Guid? personId) => personId is { } id ? _members.FirstOrDefault(m => m.PersonId == id) : null;
}

/// <summary>A folder inside a library. Moving it moves everything in it.</summary>
public sealed class Folder : AggregateRoot<Guid>
{
    private Folder()
    {
    }

    public Guid LibraryId { get; private set; }

    /// <summary>Null for a folder at the top of its library.</summary>
    public Guid? ParentId { get; private set; }

    public string Name { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When it went to the recycle bin; null while in use.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    public static Folder Create(Guid libraryId, Guid? parentId, string name, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        LibraryId = libraryId,
        ParentId = parentId,
        Name = DocumentNames.Check(name, "folder"),
        CreatedAt = now,
    };

    public void Rename(string name) => Name = DocumentNames.Check(name, "folder");

    /// <summary>Moves the folder. The caller makes sure the new parent isn't the folder itself or inside it.</summary>
    public void MoveTo(Guid? parentId) => ParentId = parentId;

    public void Delete(DateTimeOffset now) => DeletedAt ??= now;

    public void Restore() => DeletedAt = null;
}

/// <summary>One saved copy of a document. Each upload with the same name adds a version.</summary>
public sealed record DocumentVersion(int Number, string StorageKey, string ContentType, long SizeBytes, DateTimeOffset UploadedAt, Guid? UploadedByPersonId, string UploadedByName);

/// <summary>A file in a library, with every version kept until the document is deleted for good.</summary>
public sealed class Document : AggregateRoot<Guid>
{
    /// <summary>Generous for a scanned PDF or a short video, small enough to keep storage costs predictable.</summary>
    public const long MaxBytes = 100L * 1024 * 1024;

    /// <summary>Older versions beyond this are let go (their files deleted) so storage doesn't grow without end.</summary>
    public const int MaxVersions = 25;

    private readonly List<DocumentVersion> _versions = [];

    private Document()
    {
    }

    public Guid LibraryId { get; private set; }

    public Guid? FolderId { get; private set; }

    public string Name { get; private set; } = null!;

    public IReadOnlyList<DocumentVersion> Versions => _versions;

    public DocumentVersion Current => _versions[^1];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public static Document Create(Guid libraryId, Guid? folderId, string name, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        LibraryId = libraryId,
        FolderId = folderId,
        Name = DocumentNames.Check(name, "file"),
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>The storage key for the next version's file, unique per document and version.</summary>
    public string NextStorageKey(string fileName) => $"documents/{Id:N}/{NextNumber}/{Guid.NewGuid():N}{SafeExtension(fileName)}";

    private int NextNumber => _versions.Count == 0 ? 1 : _versions[^1].Number + 1;

    /// <summary>Adds an uploaded version. Returns the storage keys of versions dropped to stay within <see cref="MaxVersions"/>.</summary>
    public IReadOnlyList<string> AddVersion(string storageKey, string contentType, long sizeBytes, Guid? personId, string personName, DateTimeOffset now)
    {
        if (sizeBytes is <= 0 or > MaxBytes)
        {
            throw new DomainRuleException("documents.size", "Files can be up to 100 MB.");
        }

        _versions.Add(new DocumentVersion(NextNumber, storageKey, contentType, sizeBytes, now, personId, personName));
        UpdatedAt = now;
        var dropped = _versions.Take(Math.Max(0, _versions.Count - MaxVersions)).ToList();
        _versions.RemoveRange(0, dropped.Count);

        // A restored version shares its file with the original: only files no remaining version uses can go.
        return [.. dropped.Select(v => v.StorageKey).Distinct().Where(key => _versions.TrueForAll(v => v.StorageKey != key))];
    }

    /// <summary>Makes an earlier version current again by adding it as the newest version (the history is kept).</summary>
    public void RestoreVersion(int number, Guid? personId, string personName, DateTimeOffset now)
    {
        var old = _versions.FirstOrDefault(v => v.Number == number)
            ?? throw new DomainRuleException("documents.version", "That version isn't there any more.");
        if (old.Number == Current.Number)
        {
            return;
        }

        // The same stored file, so restoring costs no storage; deleting the document still deletes it once.
        _versions.Add(old with { Number = NextNumber, UploadedAt = now, UploadedByPersonId = personId, UploadedByName = personName });
        UpdatedAt = now;
    }

    public void Rename(string name, DateTimeOffset now)
    {
        Name = DocumentNames.Check(name, "file");
        UpdatedAt = now;
    }

    public void MoveTo(Guid? folderId, DateTimeOffset now)
    {
        FolderId = folderId;
        UpdatedAt = now;
    }

    public void Delete(DateTimeOffset now) => DeletedAt ??= now;

    public void Restore() => DeletedAt = null;

    /// <summary>Removes a person's name from the versions they uploaded (erasure); the files stay for the church.</summary>
    public bool ForgetUploader(Guid personId)
    {
        var changed = false;
        for (var i = 0; i < _versions.Count; i++)
        {
            if (_versions[i].UploadedByPersonId == personId)
            {
                _versions[i] = _versions[i] with { UploadedByPersonId = null, UploadedByName = "Former member" };
                changed = true;
            }
        }

        return changed;
    }

    private static string SafeExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Length is > 1 and <= 10 && extension.Skip(1).All(char.IsAsciiLetterOrDigit) ? extension.ToLowerInvariant() : string.Empty;
    }
}
