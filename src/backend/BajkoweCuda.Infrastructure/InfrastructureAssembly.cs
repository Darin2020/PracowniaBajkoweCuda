using System.Reflection;

namespace BajkoweCuda.Infrastructure;

/// <summary>Marker used to locate the Infrastructure assembly (architecture tests, assembly scanning).</summary>
public static class InfrastructureAssembly
{
    public static Assembly Assembly => typeof(InfrastructureAssembly).Assembly;
}
