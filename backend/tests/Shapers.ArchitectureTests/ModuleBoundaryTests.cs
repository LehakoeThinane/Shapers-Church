using System.Reflection;
using NetArchTest.Rules;

namespace Shapers.ArchitectureTests;

/// <summary>
/// Guards the modular monolith. A module may only use another module's Contracts; domain code stays
/// free of frameworks; Contracts stay free of everything but the shared kernel.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private static readonly string[] ModuleNames = ["Identity", "People", "Church", "Media", "Events", "Prayer", "Groups", "Communications", "Privacy", "Content", "Assist", "Services", "Kids"];

    public static readonly TheoryData<string> Modules = new(ModuleNames);

    private static readonly string[] Layers = ["Domain", "Application", "Infrastructure", "Api"];

    private static Assembly Load(string module, string layer) => Assembly.Load($"Shapers.{module}.{layer}");

    private static IEnumerable<string> OtherModules(string module) =>
        ModuleNames.Where(m => m != module);

    [Theory]
    [MemberData(nameof(Modules))]
    public void Modules_only_use_other_modules_through_contracts(string module)
    {
        var forbidden = OtherModules(module)
            .SelectMany(other => Layers.Select(layer => $"Shapers.{other}.{layer}"))
            .ToArray();

        foreach (var layer in Layers.Append("Contracts"))
        {
            var result = Types.InAssembly(Load(module, layer)).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
            Assert.True(result.IsSuccessful, Describe($"{module}.{layer} reaches into another module", result));
        }
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Domain_is_free_of_frameworks_and_outer_layers(string module)
    {
        var result = Types.InAssembly(Load(module, "Domain"))
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Npgsql",
                "Shapers.Platform",
                $"Shapers.{module}.Application",
                $"Shapers.{module}.Infrastructure",
                $"Shapers.{module}.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe($"{module}.Domain depends on a framework or outer layer", result));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Application_does_not_depend_on_infrastructure_or_api(string module)
    {
        var result = Types.InAssembly(Load(module, "Application"))
            .ShouldNot()
            .HaveDependencyOnAny($"Shapers.{module}.Infrastructure", $"Shapers.{module}.Api", "Microsoft.AspNetCore.Identity")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe($"{module}.Application depends on an outer layer", result));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Contracts_depend_on_nothing_but_the_shared_kernel(string module)
    {
        var result = Types.InAssembly(Load(module, "Contracts"))
            .ShouldNot()
            .HaveDependencyOnAny("Shapers.Platform", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", $"Shapers.{module}.Domain")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe($"{module}.Contracts has a heavy dependency", result));
    }

    [Fact]
    public void Shared_kernel_depends_on_nothing_in_the_solution()
    {
        var result = Types.InAssembly(typeof(ScopePath).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Shapers.Platform", "Shapers.Identity", "Shapers.People", "Shapers.Church", "Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe("SharedKernel has a dependency", result));
    }

    private static string Describe(string problem, NetArchTest.Rules.TestResult result) =>
        $"{problem}: {string.Join(", ", result.FailingTypeNames ?? [])}";
}
