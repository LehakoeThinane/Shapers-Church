using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Shapers.Platform.Modules;

/// <summary>
/// A module's entry point. The host discovers modules through this interface and knows nothing else about them.
/// </summary>
public interface IModule
{
    string Name { get; }

    void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment);

    void MapEndpoints(IEndpointRouteBuilder endpoints);

    /// <summary>Applies migrations and idempotent reference data. Runs once at startup, in module order.</summary>
    Task InitialiseAsync(IServiceProvider services, CancellationToken cancellationToken);
}
