using System.Linq.Expressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shapers.SharedKernel;

namespace Shapers.Platform.Authorization;

/// <summary>
/// Answers "may the current user do X here?". Implemented by the Identity module from the user's grants.
/// Never check role names in code; always ask for a permission at a scope.
/// </summary>
public interface IAuthorizer
{
    /// <summary>True when a grant for <paramref name="permission"/> covers <paramref name="scope"/>.</summary>
    Task<bool> CanAsync(string permission, ScopePath scope, CancellationToken cancellationToken = default);

    /// <summary>True when the user holds <paramref name="permission"/> at any scope. Use as a coarse gate on endpoints.</summary>
    Task<bool> HasAnywhereAsync(string permission, CancellationToken cancellationToken = default);

    /// <summary>The scopes where the user holds <paramref name="permission"/>, with redundant children removed.</summary>
    Task<IReadOnlyList<ScopePath>> ScopesForAsync(string permission, CancellationToken cancellationToken = default);
}

public static class AuthorizationEndpointExtensions
{
    /// <summary>
    /// Requires an authenticated user who holds <paramref name="permission"/> somewhere.
    /// Handlers still check the specific record's scope with <see cref="IAuthorizer.CanAsync"/>.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization();
        builder.AddEndpointFilter(async (context, next) =>
        {
            var authorizer = context.HttpContext.RequestServices.GetRequiredService<IAuthorizer>();
            return await authorizer.HasAnywhereAsync(permission, context.HttpContext.RequestAborted)
                ? await next(context)
                : Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: $"This needs the '{permission}' permission.",
                    extensions: new Dictionary<string, object?> { ["code"] = "permission_required", ["permission"] = permission });
        });
        return builder;
    }
}

public static class ScopeQueryExtensions
{
    /// <summary>
    /// Keeps rows whose scope path lies inside one of <paramref name="scopes"/>.
    /// Translates to <c>path = 'a' OR path LIKE 'a.%'</c>, which uses the scope index.
    /// </summary>
    public static IQueryable<T> WithinScopes<T>(
        this IQueryable<T> query,
        Expression<Func<T, string>> scopePath,
        IReadOnlyCollection<ScopePath> scopes)
    {
        if (scopes.Count == 0)
        {
            return query.Where(_ => false);
        }

        var parameter = scopePath.Parameters[0];
        var path = scopePath.Body;
        var startsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;

        Expression? predicate = null;
        foreach (var scope in ScopeSet.Collapse(scopes))
        {
            var clause = Expression.OrElse(
                Expression.Equal(path, Expression.Constant(scope.Value)),
                Expression.Call(path, startsWith, Expression.Constant(scope.Value + ".")));
            predicate = predicate is null ? clause : Expression.OrElse(predicate, clause);
        }

        return query.Where(Expression.Lambda<Func<T, bool>>(predicate!, parameter));
    }
}

public static class ScopeSet
{
    /// <summary>Removes scopes already covered by another scope in the set.</summary>
    public static IReadOnlyList<ScopePath> Collapse(IEnumerable<ScopePath> scopes)
    {
        var ordered = scopes.Distinct().OrderBy(s => s.Depth).ToList();
        var result = new List<ScopePath>();
        foreach (var scope in ordered)
        {
            if (!result.Any(r => r.Covers(scope)))
            {
                result.Add(scope);
            }
        }

        return result;
    }
}
