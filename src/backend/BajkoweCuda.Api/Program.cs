using BajkoweCuda.Api.Health;
using BajkoweCuda.Api.Hosting;
using BajkoweCuda.Api.Security;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddPublicSite();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UsePublicSite();

app.MapHealthEndpoints();

app.Run();
