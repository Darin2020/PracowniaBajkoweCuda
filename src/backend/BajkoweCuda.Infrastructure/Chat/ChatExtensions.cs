using BajkoweCuda.Application.Files;
using BajkoweCuda.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BajkoweCuda.Infrastructure.Chat;

public static class ChatExtensions
{
    /// <summary>The chat: messages, file storage, the image pipeline, attachments and their cleanup.</summary>
    public static IServiceCollection AddChat(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<BlobFileStore>();
        services.AddSingleton<IFileStore>(provider => provider.GetRequiredService<BlobFileStore>());
        services.AddScoped<Attachments>();
        services.AddScoped<ChatService>();
        services.AddHostedService<AttachmentCleanup>();
        return services;
    }

    /// <summary>The readiness check of the storage account (docs/PLAN.md §6: <c>/health/ready</c> checks DB + Blob).</summary>
    public static IHealthChecksBuilder AddStorageCheck(this IHealthChecksBuilder builder, params string[] tags) =>
        builder.AddCheck<StorageHealthCheck>("storage", tags: tags);
}

internal sealed class StorageHealthCheck(BlobFileStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await store.CheckAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "File storage is not reachable.", error);
        }
    }
}
