using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Shapers.Platform.Web;

/// <summary>
/// Maps <see cref="Result"/> to typed HTTP results. Typed results let the OpenAPI document describe
/// every response, which the generated TypeScript client depends on.
/// </summary>
public static class HttpResults
{
    public static ProblemHttpResult ToProblem(this Error error) => TypedResults.Problem(
        statusCode: error.Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.RateLimited => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status400BadRequest,
        },
        title: error.Message,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    public static Results<NoContent, ProblemHttpResult> ToHttp(this Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();

    public static Results<Ok<T>, ProblemHttpResult> ToHttp<T>(this Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error!.ToProblem();

    public static Results<Created<T>, ProblemHttpResult> ToCreated<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess ? TypedResults.Created(location(result.Value), result.Value) : result.Error!.ToProblem();
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record PageRequest(int Page = 1, int PageSize = 25)
{
    public const int MaxPageSize = 100;

    public int SafePage => Math.Max(1, Page);

    public int SafePageSize => Math.Clamp(PageSize, 1, MaxPageSize);

    public int Skip => (SafePage - 1) * SafePageSize;
}

public static class RateLimitPolicies
{
    /// <summary>Sign-in and code endpoints: a small budget per client IP.</summary>
    public const string Auth = "auth";
}
