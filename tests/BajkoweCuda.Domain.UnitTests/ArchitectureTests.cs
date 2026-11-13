namespace BajkoweCuda.Domain.UnitTests;

public class ArchitectureTests
{
    [Fact]
    public void Domain_references_only_the_base_class_library()
    {
        var references = DomainAssembly.Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .ToList();

        references.ShouldAllBe(name =>
            name == "netstandard" || name.StartsWith("System.", StringComparison.Ordinal));
    }
}
