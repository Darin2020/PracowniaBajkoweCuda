using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BajkoweCuda.Api.IntegrationTests.Database;

public sealed class DatabaseMigratorTests(SqlServerFixture sqlServer)
{
    [Fact]
    public void The_model_has_no_changes_without_a_migration()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);

        db.Database.HasPendingModelChanges().ShouldBeFalse(
            "the model changed: add a migration (dotnet ef migrations add, see CLAUDE.md)");
    }

    [Fact]
    [Trait("Category", SqlServerFixture.Category)]
    public async Task Migrate_creates_the_schema_and_can_run_again()
    {
        await using var services = BuildServices(await sqlServer.CreateDatabaseAsync());

        await RunMigratorAsync(services);
        await RunMigratorAsync(services);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var applied = await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
        applied.ShouldBe(db.Database.GetMigrations());
        (await db.Users.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Theory]
    [InlineData(null, "8f1d7c2e-0f7a-4c55-9d43-5a3b8e0b6c11")]
    [InlineData("id-bajkowecuda-staging];DROP TABLE x;--", "8f1d7c2e-0f7a-4c55-9d43-5a3b8e0b6c11")]
    [InlineData("id-bajkowecuda-staging", null)]
    [InlineData("id-bajkowecuda-staging", "not-a-guid")]
    public void Invalid_app_user_settings_are_rejected(string? name, string? clientId)
    {
        var settings = new DatabaseOptions { AppUserName = name, AppUserClientId = clientId };

        Should.Throw<InvalidOperationException>(() => DatabaseMigrator.Validate(settings));
    }

    [Fact]
    public void Valid_app_user_settings_are_accepted()
    {
        var settings = new DatabaseOptions
        {
            AppUserName = "id-bajkowecuda-staging",
            AppUserClientId = "8f1d7c2e-0f7a-4c55-9d43-5a3b8e0b6c11",
        };

        DatabaseMigrator.Validate(settings)
            .ShouldBe(("id-bajkowecuda-staging", Guid.Parse("8f1d7c2e-0f7a-4c55-9d43-5a3b8e0b6c11")));
    }

    private static ServiceProvider BuildServices(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Sql"] = connectionString })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddPersistence(configuration)
            .BuildServiceProvider();
    }

    private static async Task RunMigratorAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().RunAsync(TestContext.Current.CancellationToken);
    }
}
