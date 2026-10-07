using System.Security.Cryptography;

namespace Shapers.Kids.Domain;

/// <summary>A kids church class for an age range, e.g. "Pre-school" for ages 3 to 5. The church sets its own.</summary>
public sealed class KidsClass : AggregateRoot<Guid>
{
    public const int MaxAge = 17;

    private KidsClass()
    {
    }

    public string Name { get; private set; } = null!;

    /// <summary>The youngest age, in whole years, that goes to this class.</summary>
    public int FromAge { get; private set; }

    /// <summary>The oldest age, in whole years, that goes to this class.</summary>
    public int ToAge { get; private set; }

    public string Scope { get; private set; } = null!;

    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static KidsClass Create(string name, int fromAge, int toAge, ScopePath scope, DateTimeOffset now)
    {
        var kidsClass = new KidsClass { Id = Guid.CreateVersion7(), Scope = scope.Value, CreatedAt = now };
        kidsClass.Update(name, fromAge, toAge);
        return kidsClass;
    }

    public void Update(string name, int fromAge, int toAge)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("kids.class_name_required", "Give the class a name.");
        }

        if (fromAge < 0 || toAge > MaxAge || fromAge > toAge)
        {
            throw new DomainRuleException("kids.class_ages_invalid", $"Choose ages from 0 to {MaxAge}, with the youngest first.");
        }

        Name = name.Trim();
        FromAge = fromAge;
        ToAge = toAge;
    }

    public void Archive() => IsArchived = true;

    public void Restore() => IsArchived = false;

    public bool Fits(int age) => !IsArchived && age >= FromAge && age <= ToAge;

    /// <summary>Age in whole years on a date.</summary>
    public static int AgeOn(DateOnly dateOfBirth, DateOnly date)
    {
        var age = date.Year - dateOfBirth.Year;
        return dateOfBirth > date.AddYears(-age) ? age - 1 : age;
    }

    /// <summary>The class a child of this age goes to: the narrowest range that fits, so a special class can sit inside a general one.</summary>
    public static KidsClass? For(IEnumerable<KidsClass> classes, int age) =>
        classes.Where(c => c.Fits(age)).OrderBy(c => c.ToAge - c.FromAge).ThenBy(c => c.FromAge).FirstOrDefault();
}

/// <summary>
/// What the kids team must know to keep a child safe: allergies, medical needs and anything else. Health information
/// about a child is special personal information under POPIA, so the check-in screen only shows that notes exist;
/// reading them needs a separate, sensitive permission and is audited.
/// </summary>
public sealed class CareNote
{
    public const int MaxLength = 500;

    private CareNote()
    {
    }

    /// <summary>The child's church record (People module).</summary>
    public Guid ChildId { get; private set; }

    public string? Allergies { get; private set; }

    public string? Medical { get; private set; }

    public string? Other { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsEmpty => Allergies is null && Medical is null && Other is null;

    public static CareNote For(Guid childId) => new() { ChildId = childId };

    public void Update(string? allergies, string? medical, string? other, DateTimeOffset now)
    {
        Allergies = Clean(allergies, "Allergies");
        Medical = Clean(medical, "Medical needs");
        Other = Clean(other, "Anything else");
        UpdatedAt = now;
    }

    private static string? Clean(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        return trimmed.Length <= MaxLength
            ? trimmed
            : throw new DomainRuleException("kids.care_note_too_long", $"{label}: keep it under {MaxLength} characters.");
    }
}

public enum CheckInMethod
{
    /// <summary>A parent checked in from the app.</summary>
    App,

    /// <summary>The kids team checked in at the desk.</summary>
    Desk,
}

/// <summary>
/// A child in a class on a day. The pickup code is given to the parent who checked them in; the child is only handed
/// back to someone who shows it.
/// </summary>
public sealed class CheckIn : AggregateRoot<Guid>
{
    /// <summary>No 0/O or 1/I/L, so a code read off a phone in a hurry can't be mistaken.</summary>
    public const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int CodeLength = 4;

    private CheckIn()
    {
    }

    public Guid ChildId { get; private set; }

    /// <summary>The parent or guardian who checked the child in.</summary>
    public Guid? GuardianId { get; private set; }

    public Guid ClassId { get; private set; }

    /// <summary>The class's name on the day, so old records still read right after a class is renamed.</summary>
    public string ClassName { get; private set; } = null!;

    public string Scope { get; private set; } = null!;

    /// <summary>The church calendar date.</summary>
    public DateOnly Date { get; private set; }

    public string PickupCode { get; private set; } = null!;

    public CheckInMethod Method { get; private set; }

    public DateTimeOffset CheckedInAt { get; private set; }

    /// <summary>The kids team member who checked the child in at the desk.</summary>
    public Guid? CheckedInByUserId { get; private set; }

    public DateTimeOffset? CollectedAt { get; private set; }

    public Guid? CollectedByUserId { get; private set; }

    public bool IsCollected => CollectedAt is not null;

    /// <summary>Checks a child in. <paramref name="childScope"/> is the child's campus, so that campus's kids team sees them.</summary>
    public static CheckIn Create(
        Guid childId, Guid? guardianId, KidsClass kidsClass, ScopePath childScope, DateOnly date, string pickupCode, CheckInMethod method, Guid? byUserId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        ChildId = childId,
        GuardianId = guardianId,
        ClassId = kidsClass.Id,
        ClassName = kidsClass.Name,
        Scope = childScope.Value,
        Date = date,
        PickupCode = IsValidCode(pickupCode) ? pickupCode : throw new DomainRuleException("kids.code_invalid", "That pickup code isn't valid."),
        Method = method,
        CheckedInAt = now,
        CheckedInByUserId = byUserId,
    };

    /// <summary>Hands the child back. The code must match the one given at check-in.</summary>
    public void Collect(string code, Guid byUserId, DateTimeOffset now)
    {
        if (IsCollected)
        {
            throw new DomainRuleException("kids.already_collected", "This child has already been collected.");
        }

        if (!CodesMatch(code, PickupCode))
        {
            throw new DomainRuleException("kids.code_mismatch", "That pickup code doesn't match. Don't hand the child over; ask a leader.");
        }

        CollectedAt = now;
        CollectedByUserId = byUserId;
    }

    /// <summary>Forgets the parent, for erasure. The attendance itself stays for the child's record.</summary>
    public void ForgetGuardian() => GuardianId = null;

    public static string NewCode() => string.Create(CodeLength, 0, (chars, _) =>
    {
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }
    });

    public static string Normalise(string? code) => (code ?? "").Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal);

    public static bool IsValidCode(string code) => code.Length == CodeLength && code.All(CodeAlphabet.Contains);

    private static bool CodesMatch(string given, string expected) =>
        CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(Normalise(given)), System.Text.Encoding.ASCII.GetBytes(expected));
}
