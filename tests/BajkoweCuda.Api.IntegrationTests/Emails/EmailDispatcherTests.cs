using BajkoweCuda.Api.IntegrationTests.Database;
using BajkoweCuda.Application.Emails;
using BajkoweCuda.Infrastructure.Emails;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace BajkoweCuda.Api.IntegrationTests.Emails;

[Trait("Category", SqlServerFixture.Category)]
public sealed class EmailDispatcherTests(SqlServerFixture sqlServer)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task A_queued_email_is_sent_once()
    {
        var sender = new CapturingEmailSender();
        await using var services = await BuildAsync(sender);

        await EnqueueAsync(services);
        var nextDue = await Dispatcher(services).DispatchDueAsync(Ct);
        await Dispatcher(services).DispatchDueAsync(Ct);

        nextDue.ShouldBeNull();
        sender.Sent.Count.ShouldBe(1);
        (await SingleAsync(services)).Status.ShouldBe(EmailStatus.Sent);
    }

    [Fact]
    public async Task A_failing_email_is_retried_with_growing_delays_and_given_up_after_the_last_attempt()
    {
        await using var services = await BuildAsync(new FailingEmailSender(), maxAttempts: 3);
        await EnqueueAsync(services);

        var first = await Dispatcher(services).DispatchDueAsync(Ct);
        first.ShouldBe(_time.GetUtcNow() + TimeSpan.FromSeconds(30));
        var message = await SingleAsync(services);
        message.Attempts.ShouldBe(1);
        message.LastError!.ShouldContain("a***@example.test");
        message.LastError!.ShouldNotContain("anna@example.test");

        (await Dispatcher(services).DispatchDueAsync(Ct)).ShouldBe(first, "not due yet: nothing is tried");
        (await SingleAsync(services)).Attempts.ShouldBe(1);

        _time.Advance(TimeSpan.FromSeconds(30));
        var second = await Dispatcher(services).DispatchDueAsync(Ct);
        second.ShouldBe(_time.GetUtcNow() + TimeSpan.FromMinutes(1));

        _time.Advance(TimeSpan.FromMinutes(1));
        (await Dispatcher(services).DispatchDueAsync(Ct)).ShouldBeNull();
        message = await SingleAsync(services);
        message.Status.ShouldBe(EmailStatus.Dead);
        message.Attempts.ShouldBe(3);
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(5, 480)]
    [InlineData(20, 7200)]
    public void Retry_delays_double_up_to_two_hours(int attempts, int seconds)
    {
        EmailDispatcher.RetryDelay(attempts).ShouldBe(TimeSpan.FromSeconds(seconds));
    }

    private async Task<ServiceProvider> BuildAsync(IEmailSender sender, int maxAttempts = 8)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Sql"] = await sqlServer.CreateDatabaseAsync(),
                ["Email:MaxAttempts"] = maxAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            })
            .Build();

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeProvider>(_time)
            .AddPersistence(configuration)
            .AddEmail(configuration)
            .AddSingleton<IEmailSender>(sender)
            .BuildServiceProvider();

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(Ct);
        return services;
    }

    private static EmailDispatcher Dispatcher(ServiceProvider services) => services.GetRequiredService<EmailDispatcher>();

    private static async Task EnqueueAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IEmailOutbox>().EnqueueAsync(
            EmailTemplate.PasswordChanged, toUserId: null, "anna@example.test", "en", new Dictionary<string, string>(), Ct);
    }

    private static async Task<EmailOutboxMessage> SingleAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().EmailOutbox.AsNoTracking().SingleAsync(Ct);
    }

    private sealed class FailingEmailSender : IEmailSender
    {
        public Task SendAsync(RenderedEmail email, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"550 <{email.To}>: Recipient address rejected");
    }
}
