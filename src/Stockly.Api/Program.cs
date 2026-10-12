using Serilog;
using Stockly.Api.Configuration;
using Stockly.Api.Health;
using Stockly.Api.Identity;
using Stockly.Api.Middleware;
using Stockly.Application.Abstractions.Identity;
using Stockly.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Local secrets come from the repo-root .env (AGENTS.md rule 11); real environment variables still win.
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddRepositoryDotEnv(builder.Environment.ContentRootPath);
}

var postgres = BuildTimeOpenApi.ConnectionString(builder.Configuration, ConnectionStringNames.Postgres);
var redis = BuildTimeOpenApi.ConnectionString(builder.Configuration, ConnectionStringNames.Redis);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteToConsole(builder.Environment));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenant, HttpContextCurrentTenant>();
builder.Services.AddPersistence(postgres);

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsEnricher.Enrich);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddControllers();
builder.Services.AddOpenApi(BuildTimeOpenApi.DocumentName);
builder.Services.AddStocklyHealthChecks(postgres, redis);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapStocklyHealthChecks();
app.MapControllers();
if (!app.Environment.IsProduction())
{
    app.MapOpenApi();
}

await app.RunAsync();

/// <summary>Entry point; public so <c>WebApplicationFactory&lt;Program&gt;</c> can host the Api in tests.</summary>
public partial class Program;
