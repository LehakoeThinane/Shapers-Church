using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shapers.Identity.Application;
using Shapers.Identity.Contracts;

namespace Shapers.Identity.Infrastructure;

internal sealed class UserAccounts(UserManager<User> users, IdentityDbContext db, TimeProvider clock) : IUserAccounts
{
    public async Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Map(await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken));

    public async Task<UserAccount?> FindByVerifiedPhoneAsync(string phone, CancellationToken cancellationToken) =>
        Map(await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.PhoneNumber == phone && u.PhoneNumberConfirmed, cancellationToken));

    public async Task<UserAccount?> FindByPersonAsync(Guid personId, CancellationToken cancellationToken) =>
        Map(await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.PersonId == personId, cancellationToken));

    public async Task<Result<UserAccount>> CreateMemberAsync(Guid personId, string verifiedPhone, string? email, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(u => u.PersonId == personId, cancellationToken))
        {
            return Error.Conflict("identity.person_has_login", "This church record already has a login.");
        }

        var id = Guid.CreateVersion7();
        var user = new User
        {
            Id = id,
            UserName = id.ToString("N"),
            PersonId = personId,
            PhoneNumber = verifiedPhone,
            PhoneNumberConfirmed = true,
            CreatedAt = clock.GetUtcNow(),
        };

        // A member-supplied email stays unconfirmed and isn't used to sign in, so it can't collide with staff accounts.
        var result = await users.CreateAsync(user);
        return result.Succeeded ? Map(user)! : ToError(result);
    }

    public async Task<Result<(UserAccount Account, string SetupToken)>> PrepareStaffLoginAsync(Guid personId, string email, CancellationToken cancellationToken)
    {
        var owner = await users.FindByEmailAsync(email);
        if (owner is not null && owner.PersonId != personId)
        {
            return Error.Conflict("identity.email_in_use", "That email is already used by another login.");
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.PersonId == personId, cancellationToken);
        if (user is null)
        {
            var id = Guid.CreateVersion7();
            user = new User { Id = id, UserName = id.ToString("N"), PersonId = personId, CreatedAt = clock.GetUtcNow() };
            var created = await users.CreateAsync(user);
            if (!created.Succeeded)
            {
                return ToError(created);
            }
        }

        var setEmail = await users.SetEmailAsync(user, email);
        if (!setEmail.Succeeded)
        {
            return ToError(setEmail);
        }

        var token = await users.GeneratePasswordResetTokenAsync(user);
        return (Map(user)!, token);
    }

    public async Task RecordSignInAsync(Guid userId, DateTimeOffset at, CancellationToken cancellationToken) =>
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSignInAt, at), cancellationToken);

    public async Task SetPaletteAsync(Guid userId, string palette, CancellationToken cancellationToken) =>
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Palette, palette), cancellationToken);

    public async Task RelinkPersonAsync(Guid userId, Guid personId, CancellationToken cancellationToken) =>
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.PersonId, personId), cancellationToken);

    internal static UserAccount? Map(User? u) => u is null
        ? null
        : new UserAccount(u.Id, u.PersonId, u.Email, u.PhoneNumber, u.TwoFactorEnabled, u.PasswordHash is not null, u.Palette, u.CreatedAt, u.LastSignInAt);

    private static Error ToError(IdentityResult result) =>
        new("identity.account_error", string.Join(" ", result.Errors.Select(e => e.Description)));
}

internal sealed class UserDirectory(IdentityDbContext db) : IUserDirectory
{
    public async Task<Guid?> GetUserIdForPersonAsync(Guid personId, CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking().Where(u => u.PersonId == personId).Select(u => (Guid?)u.Id).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> PeopleWithUsersAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken = default) =>
        (await db.Users.AsNoTracking().Where(u => personIds.Contains(u.PersonId)).Select(u => u.PersonId).ToListAsync(cancellationToken)).ToHashSet();
}
