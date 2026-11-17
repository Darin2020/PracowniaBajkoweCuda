using BajkoweCuda.Api.IntegrationTests.Database;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace BajkoweCuda.Api.IntegrationTests.Database;

/// <summary>
/// One SQL Server container for the whole test run (Testcontainers; needs Docker). Each test creates its own
/// database in it, so tests do not see each other's data. Tests that use it carry the <c>Database</c> trait, which
/// lets environments without Docker skip them (<c>--filter-not-trait Category=Database</c>).
/// </summary>
public sealed class SqlServerFixture : IAsyncDisposable
{
    public const string Category = "Database";

    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private bool _started;

    /// <summary>
    /// Returns a connection string to a new, empty database. The container starts on first use, so test runs that
    /// filter out the database tests need no Docker at all.
    /// </summary>
    public async Task<string> CreateDatabaseAsync()
    {
        await StartAsync();

        var builder = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = $"test_{Guid.NewGuid():N}",
        };

        return builder.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        _startLock.Dispose();
    }

    private async Task StartAsync()
    {
        await _startLock.WaitAsync();
        try
        {
            if (!_started)
            {
                await _container.StartAsync();
                _started = true;
            }
        }
        finally
        {
            _startLock.Release();
        }
    }
}
