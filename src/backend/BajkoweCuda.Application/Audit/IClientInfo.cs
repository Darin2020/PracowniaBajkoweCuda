namespace BajkoweCuda.Application.Audit;

/// <summary>Facts about the client of the current request, recorded with audit entries.</summary>
public interface IClientInfo
{
    /// <summary>The client's IP address, or <c>null</c> outside a request (for example in the migration job).</summary>
    string? IpAddress { get; }
}
