namespace BajkoweCuda.Application.UnitTests;

public class ArchitectureTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "BajkoweCuda.Infrastructure",
        "BajkoweCuda.Api",
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Azure.",
    ];

    [Fact]
    public void Application_does_not_depend_on_outer_layers_or_frameworks()
    {
        var references = ApplicationAssembly.Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .ToList();

        references.ShouldAllBe(name =>
            !ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
    }
}
