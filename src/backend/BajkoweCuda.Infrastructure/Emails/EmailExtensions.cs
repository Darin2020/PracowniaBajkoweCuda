using Azure.Core;
using Azure.Identity;
using BajkoweCuda.Application.Emails;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BajkoweCuda.Infrastructure.Emails;

public static class EmailExtensions
{
    /// <summary>Outbox, renderer, the configured sender (SMTP or ACS) and the background dispatcher.</summary>
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(EmailOptions.SectionName);
        services.Configure<EmailOptions>(section);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<EmailDispatchSignal>();
        services.AddScoped<IEmailOutbox, EmailOutbox>();

        var provider = section.GetValue(nameof(EmailOptions.Provider), EmailProvider.Smtp);
        if (provider == EmailProvider.Acs)
        {
            // DefaultAzureCredential picks the managed identity selected by AZURE_CLIENT_ID in Azure.
            services.TryAddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
            services.AddSingleton<IEmailSender, AcsEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }

        services.AddSingleton<EmailDispatcher>();
        services.AddHostedService(provider => provider.GetRequiredService<EmailDispatcher>());

        return services;
    }
}
