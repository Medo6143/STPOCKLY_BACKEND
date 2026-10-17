using Serilog;
using Stockly.Api.Authentication;
using Stockly.Api.Configuration;
using Stockly.Api.Hangfire;
using Stockly.Api.Health;
using Stockly.Api.Identity;
using Stockly.Api.Middleware;
using Stockly.Api.RateLimiting;
using Stockly.Application;
using Stockly.Application.Abstractions.Identity;
using Stockly.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Local secrets come from the repo-root .env (AGENTS.md rule 11); real environment variables still win.
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddRepositoryDotEnv(builder.Environment.ContentRootPath);
}

var postgres = builder.Configuration.GetRequiredConnectionString(ConnectionStringNames.Postgres);
var redis = builder.Configuration.GetRequiredConnectionString(ConnectionStringNames.Redis);
var jwt = builder.Configuration.GetRequiredJwtOptions();
var authRateLimit = builder.Configuration.GetAuthRateLimitOptions();

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteToConsole(builder.Environment));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<RequestTenantScope>();
builder.Services.AddScoped<ITenantScope>(services => services.GetRequiredService<RequestTenantScope>());
builder.Services.AddScoped<ICurrentTenant, HttpContextCurrentTenant>();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddPersistence(postgres);
builder.Services.AddStocklyCaching(redis);
builder.Services.AddInvoiceScanning(builder.Configuration, postgres);
builder.Services.AddStocklyIdentity(jwt);
builder.Services.AddApplication();
builder.Services.AddPlatformAdmin(builder.Configuration);
builder.Services.AddDataProtection();
builder.Services.AddStocklyAuthentication(jwt);
builder.Services.AddStocklyLocalization();
builder.Services.AddStocklyRateLimiting(authRateLimit);

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsEnricher.Enrich);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddStocklyControllers();
builder.Services.AddOpenApi(OpenApiDocuments.V1);
builder.Services.AddStocklyHealthChecks(postgres, redis);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseRequestLocalization();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStocklyHangfireDashboard();
app.MapStocklyHealthChecks();
app.MapControllers();
if (!app.Environment.IsProduction())
{
    app.MapOpenApi();
}

await app.RunAsync();

/// <summary>Entry point; public so <c>WebApplicationFactory&lt;Program&gt;</c> can host the Api in tests.</summary>
public partial class Program;
