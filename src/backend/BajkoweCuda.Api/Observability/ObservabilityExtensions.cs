using System.Globalization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace BajkoweCuda.Api.Observability;

internal static class ObservabilityExtensions
{
    /// <summary>Standard name of the setting that carries the Application Insights connection string.</summary>
    private const string ApplicationInsightsConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    /// <summary>
    /// Logs: Serilog to the console. In a container that is JSON, one event per line, which Container Apps ships
    /// to Log Analytics; every event carries the trace id, the app version and the environment.
    /// Traces, metrics and logs also go to Application Insights when its connection string is configured
    /// (server side only; there is no browser telemetry).
    /// Rule for callers: never log message bodies, full email addresses, tokens or file contents.
    /// </summary>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        var version = AppVersion.FromConfiguration(builder.Configuration);
        var environment = builder.Environment;

        builder.Services.AddSingleton(version);

        // Serilog owns the console. Without this the default console provider would print every event a second time.
        builder.Logging.ClearProviders();

        builder.Services.AddSerilog(
            logger =>
            {
                logger
                    .MinimumLevel.Information()
                    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("AppVersion", version.Value)
                    .Enrich.WithProperty("Environment", environment.EnvironmentName);

                if (environment.IsDevelopment())
                {
                    logger.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
                }
                else
                {
                    logger.WriteTo.Console(new RenderedCompactJsonFormatter());
                }
            },
            // Keep the other logger providers: OpenTelemetry forwards logs to Application Insights through them.
            writeToProviders: true);

        if (!string.IsNullOrWhiteSpace(builder.Configuration[ApplicationInsightsConnectionStringKey]))
        {
            builder.Services.AddOpenTelemetry().UseAzureMonitor();
        }

        return builder;
    }

    /// <summary>One log event per HTTP request: method, path (never the query string), status and duration.</summary>
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
    {
        return app.UseSerilogRequestLogging(options =>
        {
            // Probes hit the liveness endpoint every few seconds; logging them would only add noise and cost.
            options.GetLevel = (context, _, exception) =>
                exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError
                    ? LogEventLevel.Error
                    : context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
                        ? LogEventLevel.Verbose
                        : LogEventLevel.Information;
        });
    }
}
