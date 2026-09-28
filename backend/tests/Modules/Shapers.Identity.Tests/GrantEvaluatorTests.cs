using Shapers.Identity.Domain;

namespace Shapers.Identity.Tests;

public sealed class GrantEvaluatorTests
{
    private const string View = "people.profiles.view";
    private const string Edit = "people.profiles.edit";

    private static readonly ScopePath Church = ScopePath.Parse("shapers");
    private static readonly ScopePath Rivonia = ScopePath.Parse("shapers.campus_rivonia");
    private static readonly ScopePath Soweto = ScopePath.Parse("shapers.campus_soweto");
    private static readonly ScopePath RivoniaKids = ScopePath.Parse("shapers.campus_rivonia.ministry_kids");

    private static GrantEvaluator With(params (ScopePath Scope, string[] Permissions)[] grants) =>
        new(grants.Select(g => new EffectiveGrant(g.Scope, g.Permissions.ToHashSet())).ToList());

    [Fact]
    public void Church_wide_grant_covers_every_campus_and_ministry()
    {
        var evaluator = With((Church, [View]));

        Assert.True(evaluator.Can(View, Church));
        Assert.True(evaluator.Can(View, Rivonia));
        Assert.True(evaluator.Can(View, Soweto));
        Assert.True(evaluator.Can(View, RivoniaKids));
    }

    [Fact]
    public void Campus_grant_does_not_reach_another_campus_or_the_church()
    {
        var evaluator = With((Rivonia, [View]));

        Assert.True(evaluator.Can(View, Rivonia));
        Assert.True(evaluator.Can(View, RivoniaKids));
        Assert.False(evaluator.Can(View, Soweto));
        Assert.False(evaluator.Can(View, Church));
    }

    [Fact]
    public void Ministry_grant_does_not_flow_up_to_its_campus()
    {
        var evaluator = With((RivoniaKids, [View]));

        Assert.True(evaluator.Can(View, RivoniaKids));
        Assert.False(evaluator.Can(View, Rivonia));
    }

    [Fact]
    public void Similar_labels_are_not_confused_with_ancestors()
    {
        var evaluator = With((ScopePath.Parse("shapers.campus_riv"), [View]));

        Assert.False(evaluator.Can(View, Rivonia));
    }

    [Fact]
    public void Permission_must_be_in_the_grant()
    {
        var evaluator = With((Church, [View]));

        Assert.False(evaluator.Can(Edit, Rivonia));
        Assert.False(evaluator.HasAnywhere(Edit));
    }

    [Fact]
    public void Scopes_for_a_permission_drop_children_already_covered()
    {
        var evaluator = With((Rivonia, [View]), (RivoniaKids, [View, Edit]), (Soweto, [View]));

        Assert.Equal([Rivonia, Soweto], evaluator.ScopesFor(View).OrderBy(s => s.Value));
        Assert.Equal([RivoniaKids], evaluator.ScopesFor(Edit));
    }

    [Fact]
    public void Expired_and_revoked_grants_are_inactive()
    {
        var now = DateTimeOffset.UtcNow;
        var expiring = Grant.Create(Guid.NewGuid(), Guid.NewGuid(), Rivonia, null, now, now.AddDays(1), null);
        var revoked = Grant.Create(Guid.NewGuid(), Guid.NewGuid(), Rivonia, null, now, null, null);
        revoked.Revoke(null, now);

        Assert.True(expiring.IsActiveAt(now));
        Assert.False(expiring.IsActiveAt(now.AddDays(2)));
        Assert.False(revoked.IsActiveAt(now));
    }
}

public sealed class GrantPolicyTests
{
    private const string Manage = GrantPolicy.ManageGrantsPermission;
    private const string View = "people.profiles.view";
    private const string Merge = "people.profiles.merge";

    private static readonly ScopePath Rivonia = ScopePath.Parse("shapers.campus_rivonia");
    private static readonly ScopePath Soweto = ScopePath.Parse("shapers.campus_soweto");

    private static GrantEvaluator Grantor(ScopePath scope, params string[] permissions) =>
        new([new EffectiveGrant(scope, permissions.ToHashSet())]);

    [Fact]
    public void Can_grant_permissions_you_hold_at_the_target_scope()
    {
        var result = GrantPolicy.CanGrant(Grantor(Rivonia, Manage, View), new HashSet<string> { View }, Rivonia);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Cannot_grant_a_permission_you_do_not_hold()
    {
        var result = GrantPolicy.CanGrant(Grantor(Rivonia, Manage, View), new HashSet<string> { View, Merge }, Rivonia);

        Assert.True(result.IsFailure);
        Assert.Equal("identity.escalation", result.Error!.Code);
        Assert.Contains(Merge, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cannot_grant_outside_your_own_scope()
    {
        var result = GrantPolicy.CanGrant(Grantor(Rivonia, Manage, View), new HashSet<string> { View }, Soweto);

        Assert.Equal("identity.cannot_manage_grants", result.Error!.Code);
    }

    [Fact]
    public void Holding_permissions_without_grant_management_is_not_enough()
    {
        var result = GrantPolicy.CanGrant(Grantor(Rivonia, View), new HashSet<string> { View }, Rivonia);

        Assert.Equal("identity.cannot_manage_grants", result.Error!.Code);
    }

    [Fact]
    public void Revoking_needs_grant_management_at_the_grant_scope()
    {
        Assert.True(GrantPolicy.CanRevoke(Grantor(Rivonia, Manage), Rivonia).IsSuccess);
        Assert.True(GrantPolicy.CanRevoke(Grantor(Rivonia, Manage), Soweto).IsFailure);
    }
}
