using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BajkoweCuda.Infrastructure.Orders;

public static class OrdersExtensions
{
    /// <summary>Orders and their payment instructions (section <c>PaymentInstructions</c>). Needs the chat services.</summary>
    public static IServiceCollection AddOrders(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PaymentInstructionsOptions>(configuration.GetSection(PaymentInstructionsOptions.SectionName));
        services.AddSingleton<PaymentInstructions>();
        services.AddScoped<OrderService>();
        return services;
    }
}
