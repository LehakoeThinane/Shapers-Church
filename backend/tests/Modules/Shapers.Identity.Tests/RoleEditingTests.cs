using Shapers.Identity.Domain;

namespace Shapers.Identity.Tests;

public sealed class RoleEditingTests
{
    private const string View = "people.profiles.view";
    private const string Edit = "people.profiles.edit";
    private const string Chat = "media.chat.moderate";

    [Fact]
    public void Changing_a_built_in_role_keeps_its_name_and_marks_it_customised()
    {
        var role = Role.Create("Campus administrator", "Day-to-day", [View], isSystem: true);

        role.Update("Renamed", "Runs the campus office", [View, Edit]);

        Assert.Equal("Campus administrator", role.Name);
        Assert.Equal("Runs the campus office", role.Description);
        Assert.True(role.IsCustomised);
        Assert.Equal([Edit, View], role.PermissionKeys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Startup_sync_leaves_a_customised_role_alone()
    {
        var role = Role.Create("Campus administrator", null, [View], isSystem: true);
        role.Update("Campus administrator", null, [View, Chat]);

        role.SyncSystemPermissions([View, Edit]);

        Assert.Equal([Chat, View], role.PermissionKeys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Startup_sync_still_updates_an_untouched_built_in_role()
    {
        var role = Role.Create("Campus administrator", null, [View], isSystem: true);

        role.SyncSystemPermissions([View, Edit]);

        Assert.Equal([Edit, View], role.PermissionKeys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Reset_puts_a_built_in_role_back_and_lets_startup_sync_it_again()
    {
        var role = Role.Create("Campus administrator", "Day-to-day", [View], isSystem: true);
        role.Update("Campus administrator", "Changed", [Chat]);

        role.ResetToDefaults("Day-to-day", [View]);

        Assert.False(role.IsCustomised);
        Assert.Equal("Day-to-day", role.Description);
        Assert.Equal([View], role.PermissionKeys);
    }

    [Fact]
    public void The_full_access_role_cannot_be_changed()
    {
        var role = Role.Create(Role.FullAccessName, "Full access", [View, Edit], isSystem: true);

        Assert.Throws<DomainRuleException>(() => role.Update(Role.FullAccessName, null, [View]));
    }

    [Fact]
    public void A_custom_role_can_be_renamed_but_has_no_defaults()
    {
        var role = Role.Create("Welcome team", null, [View]);

        role.Update("Hospitality", null, [View]);

        Assert.Equal("Hospitality", role.Name);
        Assert.False(role.IsCustomised);
        Assert.Throws<DomainRuleException>(() => role.ResetToDefaults("x", [View]));
    }
}
