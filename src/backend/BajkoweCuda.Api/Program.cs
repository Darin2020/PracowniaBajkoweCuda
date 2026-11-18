using BajkoweCuda.Api.Auth;
using BajkoweCuda.Api.Database;
using BajkoweCuda.Api.Health;
using BajkoweCuda.Api.Hosting;
using BajkoweCuda.Api.Observability;
using BajkoweCuda.Api.Problems;
using BajkoweCuda.Api.Security;
using BajkoweCuda.Infrastructure.Emails;
using BajkoweCuda.Infrastructure.Persistence;

if (MigrateCommand.IsRequested(args))
{
    return await MigrateCommand.RunAsync(args);
}

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.AddObservability();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddEmail(builder.Configuration);
builder.Services.AddHealth();
builder.Services.AddPublicSite(builder.Configuration);
builder.Services.AddAuth(builder.Configuration);
builder.Services
    .AddControllers(options =>
    {
        // Every unsafe request (POST, PUT, PATCH, DELETE) needs the antiforgery header (docs/PLAN.md §8.1).
        options.Filters.Add<AntiforgeryFilter>();
    })
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ApiProblems.InvalidModelState);

var app = builder.Build();

// Behind the Container Apps ingress the scheme and client address come from X-Forwarded-* headers.
// ASP.NET Core applies them itself when ASPNETCORE_FORWARDEDHEADERS_ENABLED=true (set by the infrastructure).

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseRequestLogging();
app.UsePublicSite();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapVersionEndpoint();
app.MapControllers().RequireAuthorization();

await app.RunAsync();

return 0;
