using Microsoft.AspNetCore.Identity;
using Shapers.Identity.Infrastructure;

namespace Shapers.Api.Hosting;

internal static class SecurityMiddleware
{
    /// <summary>
    /// Cookie-authenticated writes must carry a custom header. Browsers can't add one cross-site without a
    /// CORS preflight, so together with SameSite=Strict this blocks cross-site request forgery.
    /// Bearer-token requests (the mobile app) are not exposed to CSRF and are not checked.
    /// </summary>
    public static IApplicationBuilder UseCsrfProtection(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var unsafeMethod = !HttpMethods.IsGet(context.Request.Method)
                && !HttpMethods.IsHead(context.Request.Method)
                && !HttpMethods.IsOptions(context.Request.Method);
            var cookieSession = context.User.Identity is { IsAuthenticated: true, AuthenticationType: var type }
                && type == IdentityConstants.ApplicationScheme;

            if (unsafeMethod && cookieSession && !context.Request.Headers.ContainsKey(IdentityInfrastructure.CsrfHeader))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { title = "Missing CSRF header.", code = "csrf_header_missing" });
                return;
            }

            await next();
        });

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            await next();
        });
}
