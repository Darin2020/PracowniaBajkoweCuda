using BajkoweCuda.Application.Audit;
using BajkoweCuda.Infrastructure.Audit;
using BajkoweCuda.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BajkoweCuda.Infrastructure.Accounts;

public static class AccountsExtensions
{
    /// <summary>
    /// Audit log, admin role sync and account data. Needs the Identity services (AddIdentity in the application,
    /// AddIdentityCore in the migration job). The web application replaces <see cref="IClientInfo"/> with one that
    /// reads the request.
    /// </summary>
    public static IServiceCollection AddAccounts(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AdminOptions>(configuration.GetSection(AdminOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IClientInfo, NoClientInfo>();
        services.AddSingleton<IAuditLog, AuditLog>();
        services.AddScoped<AdminRoleSync>();
        services.AddScoped<AccountData>();
        return services;
    }
}
