using System.Reflection;

namespace BajkoweCuda.Application;

/// <summary>Marker used to locate the Application assembly (architecture tests, assembly scanning).</summary>
public static class ApplicationAssembly
{
    public static Assembly Assembly => typeof(ApplicationAssembly).Assembly;
}
