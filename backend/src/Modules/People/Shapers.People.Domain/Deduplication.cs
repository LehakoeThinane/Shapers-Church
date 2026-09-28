using System.Globalization;
using System.Text;

namespace Shapers.People.Domain;

public enum DuplicateStatus
{
    Open,
    Merged,
    Dismissed,
}

/// <summary>Two records that might be the same person, waiting for a staff member to decide.</summary>
public sealed class DuplicateCandidate
{
    private DuplicateCandidate()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Always the smaller of the two IDs, so a pair is stored once.</summary>
    public Guid PersonAId { get; private set; }

    public Guid PersonBId { get; private set; }

    public int Score { get; private set; }

    public string Reasons { get; private set; } = null!;

    public DuplicateStatus Status { get; private set; }

    public DateTimeOffset DetectedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public Guid? ResolvedByUserId { get; private set; }

    public static DuplicateCandidate Create(Guid first, Guid second, DuplicateMatch match, DateTimeOffset now)
    {
        if (first == second)
        {
            throw new DomainRuleException("people.duplicate_self", "A record cannot duplicate itself.");
        }

        var (a, b) = first.CompareTo(second) < 0 ? (first, second) : (second, first);
        return new DuplicateCandidate
        {
            Id = Guid.CreateVersion7(),
            PersonAId = a,
            PersonBId = b,
            Score = match.Score,
            Reasons = string.Join(", ", match.Reasons),
            Status = DuplicateStatus.Open,
            DetectedAt = now,
        };
    }

    public void Resolve(DuplicateStatus outcome, Guid? by, DateTimeOffset now)
    {
        if (outcome == DuplicateStatus.Open)
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        Status = outcome;
        ResolvedByUserId = by;
        ResolvedAt = now;
    }
}

public sealed record DuplicateMatch(int Score, IReadOnlyList<string> Reasons)
{
    public bool IsLikely => Score >= DuplicateMatcher.Threshold;
}

/// <summary>
/// Scores how likely two records are the same person. Deliberately simple and explainable:
/// staff see the reasons and always make the final call.
/// </summary>
public static class DuplicateMatcher
{
    public const int Threshold = 60;

    public static DuplicateMatch Compare(Person a, Person b)
    {
        var score = 0;
        var reasons = new List<string>();

        var phonesA = a.Contacts.Where(c => c.Type is ContactType.Mobile or ContactType.WhatsApp).Select(c => c.Value).ToHashSet();
        if (b.Contacts.Any(c => c.Type is ContactType.Mobile or ContactType.WhatsApp && phonesA.Contains(c.Value)))
        {
            score += 40;
            reasons.Add("same phone number");
        }

        var emailsA = a.Contacts.Where(c => c.Type == ContactType.Email).Select(c => c.Value).ToHashSet();
        if (b.Contacts.Any(c => c.Type == ContactType.Email && emailsA.Contains(c.Value)))
        {
            score += 40;
            reasons.Add("same email");
        }

        if (NormaliseName(a.LastName) == NormaliseName(b.LastName))
        {
            var firstA = NormaliseName(a.FirstName);
            var firstB = NormaliseName(b.FirstName);
            var preferredA = a.PreferredName is null ? null : NormaliseName(a.PreferredName);
            var preferredB = b.PreferredName is null ? null : NormaliseName(b.PreferredName);
            if (firstA == firstB || firstA == preferredB || preferredA == firstB)
            {
                score += 30;
                reasons.Add("same name");
            }
        }

        if (a.DateOfBirth is { } dobA && b.DateOfBirth is { } dobB)
        {
            if (dobA == dobB)
            {
                score += 25;
                reasons.Add("same date of birth");
            }
            else
            {
                // Different birthdays on both records is strong evidence of two people sharing a phone or email.
                score -= 50;
                reasons.Add("different date of birth");
            }
        }

        return new DuplicateMatch(Math.Max(0, score), reasons);
    }

    /// <summary>Lower-case, accents removed, only letters kept: "Thánde-Mokoena" and "thande mokoena" match.</summary>
    public static string NormaliseName(string name)
    {
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (char.IsLetter(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}
