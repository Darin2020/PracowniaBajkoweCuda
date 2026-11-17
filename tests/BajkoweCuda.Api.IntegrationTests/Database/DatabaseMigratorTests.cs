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

    /// <summary>
    /// <c>TYPE = E</c> exists only in Azure SQL, so the SQL Server container cannot create the user. The user is
    /// created beforehand (as a user without login); the migrator must then skip creation and grant the roles.
    /// </summary>
    [Fact]
    [Trait("Category", SqlServerFixture.Category)]
    public async Task Migrate_gives_an_existing_app_user_read_and_write_access_and_can_run_again()
    {
        const string appUser = "id-bajkowecuda-test";
        await using var services = BuildServices(
            await sqlServer.CreateDatabaseAsync(),
            ("Database:AppUserName", appUser),
            ("Database:AppUserClientId", "8f1d7c2e-0f7a-4c55-9d43-5a3b8e0b6c11"));
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE USER [" + appUser + "] WITHOUT LOGIN", TestContext.Current.CancellationToken);
        }

        await RunMigratorAsync(services);
        await RunMigratorAsync(services);

        await using var check = services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<AppDbContext>().Database;
        var roles = await database
            .SqlQuery<int>($"SELECT IS_ROLEMEMBER('db_datareader', {appUser}) + IS_ROLEMEMBER('db_datawriter', {appUser}) + IS_ROLEMEMBER('db_owner', {appUser}) AS [Value]")
            .SingleAsync(TestContext.Current.CancellationToken);
        roles.ShouldBe(2, "reader + writer, and not owner");
    }

    [Fact]
    public void The_create_user_statement_uses_the_client_id_as_sid()
    {
        DatabaseMigrator.CreateUserStatement("id-bajkowecuda-staging", Guid.Parse("8f1d7c2e-0f7a-4c55-9d43-5a3b8e0b6c11"))
            .ShouldBe("CREATE USER [id-bajkowecuda-staging] WITH SID = 0x2E7C1D8F7A0F554C9D435A3B8E0B6C11, TYPE = E");
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

    private static ServiceProvider BuildServices(string connectionString, params (string Key, string Value)[] settings)
    {
        var values = new Dictionary<string, string?> { ["ConnectionStrings:Sql"] = connectionString };
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

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
