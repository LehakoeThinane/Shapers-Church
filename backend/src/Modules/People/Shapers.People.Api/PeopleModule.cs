using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shapers.People.Application;
using Shapers.People.Contracts;
using Shapers.People.Domain;
using Shapers.People.Infrastructure;
using Shapers.Platform.Authorization;
using Shapers.Platform.Modules;
using Shapers.Platform.Web;

namespace Shapers.People.Api;

public sealed class PeopleModule : IModule
{
    public string Name => "people";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddPeopleInfrastructure(configuration);

    public Task InitialiseAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.InitialisePeopleAsync(cancellationToken);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapStaffEndpoints(endpoints.MapGroup("/api/admin").WithTags("People").RequireAuthorization());
        MapMemberEndpoints(endpoints.MapGroup("/api/me").WithTags("My profile").RequireAuthorization());
    }

    private static void MapStaffEndpoints(RouteGroupBuilder admin)
    {
        var people = admin.MapGroup("/people");

        people.MapGet("/", ([AsParameters] PeopleListQuery query, PeopleService service, CancellationToken ct) => service.ListAsync(query, ct))
            .WithName("ListPeople")
            .RequirePermission(PeoplePermissions.ProfilesView);

        people.MapGet("/{id:guid}", async (Guid id, PeopleService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttp())
            .WithName("GetPerson")
            .RequirePermission(PeoplePermissions.ProfilesView);

        people.MapPost("/", async (CreatePersonRequest request, PeopleService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)).ToCreated(p => $"/api/admin/people/{p.Id}"))
            .WithName("CreatePerson")
            .RequirePermission(PeoplePermissions.ProfilesEdit);

        people.MapPut("/{id:guid}", async (Guid id, UpdatePersonRequest request, PeopleService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttp())
            .WithName("UpdatePerson")
            .RequirePermission(PeoplePermissions.ProfilesEdit);

        people.MapPost("/{id:guid}/contacts", async (Guid id, AddContactRequest request, PeopleService service, CancellationToken ct) =>
                (await service.AddContactAsync(id, request, ct)).ToHttp())
            .WithName("AddPersonContact")
            .RequirePermission(PeoplePermissions.ProfilesEdit);

        people.MapDelete("/{id:guid}/contacts/{contactId:guid}", async (Guid id, Guid contactId, PeopleService service, CancellationToken ct) =>
                (await service.RemoveContactAsync(id, contactId, ct)).ToHttp())
            .WithName("RemovePersonContact")
            .RequirePermission(PeoplePermissions.ProfilesEdit);

        people.MapPost("/{id:guid}/status", async (Guid id, ChangeStatusRequest request, PeopleService service, CancellationToken ct) =>
                (await service.ChangeStatusAsync(id, request, ct)).ToHttp())
            .WithName("ChangeMembershipStatus")
            .RequirePermission(PeoplePermissions.ProfilesEdit);

        people.MapPost("/{id:guid}/campus", async (Guid id, MoveCampusRequest request, PeopleService service, CancellationToken ct) =>
                (await service.MoveCampusAsync(id, request, ct)).ToHttp())
            .WithName("MovePersonCampus")
            .RequirePermission(PeoplePermissions.ProfilesEdit);

        people.MapPost("/{id:guid}/consents", async (Guid id, RecordConsentRequest request, PeopleService service, CancellationToken ct) =>
                (await service.RecordConsentAsync(id, request, ct)).ToHttp())
            .WithName("RecordPersonConsent")
            .RequirePermission(PeoplePermissions.ProfilesEdit);

        people.MapPost("/merge", async (MergeRequest request, MergeService service, CancellationToken ct) =>
                (await service.MergeAsync(request, ct)).ToHttp())
            .WithName("MergePeople")
            .RequirePermission(PeoplePermissions.ProfilesMerge);

        var duplicates = admin.MapGroup("/duplicates");
        duplicates.MapGet("/", (MergeService service, CancellationToken ct) => service.ListDuplicatesAsync(ct))
            .WithName("ListDuplicates")
            .RequirePermission(PeoplePermissions.ProfilesMerge);
        duplicates.MapPost("/{id:guid}/dismiss", async (Guid id, MergeService service, CancellationToken ct) =>
                (await service.DismissAsync(id, ct)).ToHttp())
            .WithName("DismissDuplicate")
            .RequirePermission(PeoplePermissions.ProfilesMerge);

        var households = admin.MapGroup("/households");
        households.MapGet("/{id:guid}", async (Guid id, HouseholdService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttp())
            .WithName("GetHousehold")
            .RequirePermission(PeoplePermissions.ProfilesView);
        households.MapPost("/", async (CreateHouseholdRequest request, HouseholdService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)).ToCreated(h => $"/api/admin/households/{h.Id}"))
            .WithName("CreateHousehold")
            .RequirePermission(PeoplePermissions.ProfilesEdit);
        households.MapPost("/{id:guid}/members", async (Guid id, HouseholdMemberRequest request, HouseholdService service, CancellationToken ct) =>
                (await service.AddMemberAsync(id, request, ct)).ToHttp())
            .WithName("AddHouseholdMember")
            .RequirePermission(PeoplePermissions.ProfilesEdit);
        households.MapDelete("/{id:guid}/members/{personId:guid}", async (Guid id, Guid personId, HouseholdService service, CancellationToken ct) =>
                (await service.RemoveMemberAsync(id, personId, ct)).ToHttp())
            .WithName("RemoveHouseholdMember")
            .RequirePermission(PeoplePermissions.ProfilesEdit);
        households.MapPost("/{id:guid}/primary-contact/{personId:guid}", async (Guid id, Guid personId, HouseholdService service, CancellationToken ct) =>
                (await service.SetPrimaryContactAsync(id, personId, ct)).ToHttp())
            .WithName("SetHouseholdPrimaryContact")
            .RequirePermission(PeoplePermissions.ProfilesEdit);

        admin.MapGet("/membership-statuses", (PeopleService service, CancellationToken ct) => service.ListStatusesAsync(ct))
            .WithName("ListMembershipStatuses");
        admin.MapPost("/membership-statuses", async (CreateMembershipStatusRequest request, PeopleService service, CancellationToken ct) =>
                (await service.CreateStatusAsync(request, ct)).ToHttp())
            .WithName("CreateMembershipStatus")
            .RequirePermission(PeoplePermissions.StatusesManage);
    }

    private static void MapMemberEndpoints(RouteGroupBuilder me)
    {
        me.MapGet("/profile", async (MyProfileService service, CancellationToken ct) => (await service.GetAsync(ct)).ToHttp())
            .WithName("GetMyProfile");

        me.MapPut("/profile", async (UpdateMyProfileRequest request, MyProfileService service, CancellationToken ct) =>
                (await service.UpdateAsync(request, ct)).ToHttp())
            .WithName("UpdateMyProfile");

        me.MapPost("/consents", async (RecordConsentRequest request, MyProfileService service, CancellationToken ct) =>
                (await service.RecordConsentAsync(request.Decisions, request.PolicyVersion, request.Source, ct)).ToHttp())
            .WithName("RecordMyConsent");
    }
}
