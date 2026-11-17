namespace BajkoweCuda.Infrastructure.Persistence;

/// <summary>Settings of the <c>migrate</c> command (section <c>Database</c>).</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Name of the application's managed identity. When set together with <see cref="AppUserClientId"/>, the
    /// migration creates a database user for it with read and write access only. Empty locally (the app uses sa).
    /// </summary>
    public string? AppUserName { get; set; }

    /// <summary>Client (application) id of the application's managed identity.</summary>
    public string? AppUserClientId { get; set; }
}
