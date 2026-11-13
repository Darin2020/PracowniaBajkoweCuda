using System.Reflection;

namespace BajkoweCuda.Domain;

/// <summary>Marker used to locate the Domain assembly (architecture tests, assembly scanning).</summary>
public static class DomainAssembly
{
    public static Assembly Assembly => typeof(DomainAssembly).Assembly;
}
