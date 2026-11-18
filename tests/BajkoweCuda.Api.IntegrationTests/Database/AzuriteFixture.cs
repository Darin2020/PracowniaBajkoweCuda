using BajkoweCuda.Api.IntegrationTests.Database;
using Testcontainers.Azurite;

[assembly: AssemblyFixture(typeof(AzuriteFixture))]

namespace BajkoweCuda.Api.IntegrationTests.Database;

/// <summary>
/// One Azurite (Blob Storage emulator) container for the whole test run, started on first use like
/// <see cref="SqlServerFixture"/>. Tests that use it also carry the <c>Database</c> trait (they need Docker).
/// </summary>
public sealed class AzuriteFixture : IAsyncDisposable
{
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private AzuriteContainer? _container;

    public async Task<string> GetConnectionStringAsync()
    {
        await _startLock.WaitAsync();
        try
        {
            if (_container is null)
            {
                var container = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0")
                    .WithCommand("--skipApiVersionCheck", "--loose")
                    .Build();
                await container.StartAsync();
                _container = container;
            }
        }
        finally
        {
            _startLock.Release();
        }

        return WithIpAddress(_container.GetConnectionString(), _container.Hostname);
    }

    /// <summary>
    /// The Blob client reads the account name from the path only for IP addresses, "localhost" and the emulator's
    /// default ports. Tests that run in a container reach Azurite as host.docker.internal on a random port, so the host
    /// name is replaced with its address.
    /// </summary>
    private static string WithIpAddress(string connectionString, string hostname)
    {
        if (hostname == "localhost" || System.Net.IPAddress.TryParse(hostname, out _))
        {
            return connectionString;
        }

        var address = System.Net.Dns.GetHostAddresses(hostname)
            .First(candidate => candidate.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        return connectionString.Replace($"//{hostname}:", $"//{address}:", StringComparison.Ordinal);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        _startLock.Dispose();
    }
}
