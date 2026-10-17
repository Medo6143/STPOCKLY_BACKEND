using Amazon.Runtime;
using Amazon.S3;
using Hangfire;
using Hangfire.AspNetCore;
using Hangfire.Common;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Stockly.Application.Abstractions.Ai;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Jobs;
using Stockly.Application.Abstractions.Limits;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Stock;
using Stockly.Application.Abstractions.Storage;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Auth;
using Stockly.Application.Features.Invoices;
using Stockly.Infrastructure.Ai.OpenRouter;
using Stockly.Infrastructure.Caching;
using Stockly.Infrastructure.Configuration;
using Stockly.Infrastructure.Identity;
using Stockly.Infrastructure.Jobs;
using Stockly.Infrastructure.Persistence;
using Stockly.Infrastructure.Persistence.Interceptors;
using Stockly.Infrastructure.Persistence.Queries;
using Stockly.Infrastructure.Persistence.Repositories;
using Stockly.Infrastructure.Persistence.Stock;
using Stockly.Infrastructure.Storage;
using Stockly.Infrastructure.Time;

namespace Stockly.Infrastructure;

public static class DependencyInjection
{
    private const string OpenRouterPipeline = "openrouter";
    private const int TooManyRequests = 429;
    private const int FirstServerError = 500;

    /// <summary>First retry delay of an OpenRouter call; the second is about twice as long (docs/design/backend.md §6.1).</summary>
    private static readonly TimeSpan OpenRouterRetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>Longest wait before a retry, also when a 429 asks for more with <c>Retry-After</c>.</summary>
    private static readonly TimeSpan OpenRouterMaxRetryDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Registers <see cref="StocklyDbContext"/> with its interceptors, <see cref="IClock"/> and a <see cref="TimeProvider"/>
    /// reading it (for framework code), the repositories, the catalog and invoice queries, the stock service and the unit
    /// of work. The caller registers
    /// <c>ICurrentTenant</c> (from the JWT in the Api).
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton<IClock, SystemClock>();
        services.AddSingleton<TimeProvider, ClockTimeProvider>();
        services.AddScoped<TenantStampInterceptor>();
        services.AddScoped<AuditTimestampsInterceptor>();
        services.AddDbContext<StocklyDbContext>((provider, options) =>
            StocklyDbContextOptions.Configure(options, connectionString)
                .AddInterceptors(
                    provider.GetRequiredService<TenantStampInterceptor>(),
                    provider.GetRequiredService<AuditTimestampsInterceptor>()));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IShopRepository, ShopRepository>();
        services.AddScoped<IShopMembershipRepository, ShopMembershipRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<IShopSubscriptionRepository, ShopSubscriptionRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductQueries, ProductQueries>();
        services.AddScoped<IStockService, StockService>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IInvoiceQueries, InvoiceQueries>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<IProductAliasRepository, ProductAliasRepository>();
        services.AddScoped<IAiUsageRecordRepository, AiUsageRecordRepository>();
        services.AddScoped<IShopPlanQueries, ShopPlanQueries>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<ISaleQueries, SaleQueries>();
        services.AddScoped<IDashboardQueries, DashboardQueries>();
        services.AddScoped<IProductUnitUsageQueries, ProductUnitUsageQueries>();

        return services;
    }

    /// <summary>
    /// Redis (docs/design/redis.md): the read cache (<see cref="ICacheService"/>), the AI result cache, the invoice status
    /// cache, and the scan rate limit and daily quota. All share one lazily opened connection with a 250 ms operation
    /// timeout. Requires logging and <see cref="IClock"/>; the limiter also needs <see cref="ScanOptions"/>
    /// (<see cref="AddInvoiceScanning"/>).
    /// </summary>
    public static IServiceCollection AddStocklyCaching(this IServiceCollection services, string redisConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redisConnectionString);

        services.AddSingleton(_ => new RedisConnection(redisConnectionString));
        services.AddStackExchangeRedisCache(_ => { });
        services.AddOptions<RedisCacheOptions>()
            .Configure<RedisConnection>((options, connection) => options.ConnectionMultiplexerFactory = connection.GetMultiplexerAsync);
        services.AddSingleton<ICacheService, RedisCacheService>();
        services.AddSingleton<RedisDegradationLog>();
        services.AddSingleton<IAiResultCache, RedisAiResultCache>();
        services.AddSingleton<IInvoiceStatusCache, RedisInvoiceStatusCache>();
        services.AddSingleton<RedisScanLimiter>();
        services.AddSingleton<IScanRateLimiter>(provider => provider.GetRequiredService<RedisScanLimiter>());
        services.AddSingleton<IScanQuotaCounter>(provider => provider.GetRequiredService<RedisScanLimiter>());

        return services;
    }

    /// <summary>
    /// ASP.NET Core Identity on <see cref="StocklyDbContext"/> (users only, no roles), the password and lockout
    /// policies, and the token issuer. Requires <see cref="AddPersistence"/> and logging.
    /// </summary>
    public static IServiceCollection AddStocklyIdentity(this IServiceCollection services, JwtOptions jwtOptions)
    {
        ArgumentNullException.ThrowIfNull(jwtOptions);

        services.AddSingleton(jwtOptions);
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = AuthLimits.PasswordMinLength;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.User.RequireUniqueEmail = true;

                // The user name is the email; any character an email may hold is allowed.
                options.User.AllowedUserNameCharacters = string.Empty;

                options.Lockout.MaxFailedAccessAttempts = AuthProtection.MaxFailedLoginAttempts;
                options.Lockout.DefaultLockoutTimeSpan = AuthProtection.LockoutDuration;
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddUserManager<StocklyUserManager>()
            .AddEntityFrameworkStores<StocklyDbContext>();
        services.AddScoped<IUserAccounts, IdentityUserAccounts>();

        return services;
    }

    /// <summary>
    /// Everything the invoice scan needs besides persistence and Redis (docs/design/backend.md §3–§6, §14): the scan
    /// limits, image storage, the OpenRouter extractor and the Hangfire jobs. Each options section is validated at startup.
    /// </summary>
    public static IServiceCollection AddInvoiceScanning(
        this IServiceCollection services, IConfiguration configuration, string postgresConnectionString)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<ScanOptions>(configuration, ScanOptions.SectionName);
        services.AddStocklyStorage(configuration);
        services.AddOpenRouter(configuration);
        services.AddStocklyJobs(configuration, postgresConnectionString);
        return services;
    }

    /// <summary>Invoice images in S3-compatible storage (MinIO, ADR-0008) as <see cref="IFileStorage"/>.</summary>
    public static IServiceCollection AddStocklyStorage(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<StorageOptions>(configuration, StorageOptions.SectionName);
        services.AddSingleton<IAmazonS3>(provider =>
        {
            var options = provider.GetRequiredService<StorageOptions>();
            var config = new AmazonS3Config
            {
                ServiceURL = options.Endpoint!.ToString(),
                ForcePathStyle = options.ForcePathStyle,
                AuthenticationRegion = options.Region,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            };
            return new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
        });
        services.AddSingleton<IFileStorage, S3FileStorage>();
        return services;
    }

    /// <summary>
    /// The OpenRouter extractor as a typed client (rule 42) with the pipeline of docs/design/backend.md §6.1: retry
    /// (outer; 429 and 5xx only; exponential backoff with jitter; honours <c>Retry-After</c>), then a per-attempt timeout,
    /// then <see cref="OpenRouterAttemptRecorder"/> (innermost, sees every attempt). No circuit breaker and no hedging:
    /// hedging would duplicate paid calls.
    /// </summary>
    public static IServiceCollection AddOpenRouter(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<OpenRouterOptions>(configuration, OpenRouterOptions.SectionName);
        services.AddTransient<OpenRouterAttemptRecorder>();
        services.AddHttpClient<IAiInvoiceExtractor, OpenRouterInvoiceExtractor>((provider, client) =>
            {
                client.BaseAddress = provider.GetRequiredService<OpenRouterOptions>().BaseUrl;
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddResilienceHandler(OpenRouterPipeline, (pipeline, context) =>
            {
                var options = context.ServiceProvider.GetRequiredService<OpenRouterOptions>();
                if (options.MaxRetries > 0)
                {
                    pipeline.AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = options.MaxRetries,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        Delay = OpenRouterRetryDelay,
                        MaxDelay = OpenRouterMaxRetryDelay,
                        ShouldRetryAfterHeader = true,
                        ShouldHandle = arguments => ValueTask.FromResult(
                            arguments.Outcome.Result is { } response
                            && ((int)response.StatusCode == TooManyRequests || (int)response.StatusCode >= FirstServerError)),
                    });
                }

                pipeline.AddTimeout(TimeSpan.FromSeconds(options.TimeoutSeconds));
            })
            .Services
            .AddHttpClient<IAiInvoiceExtractor, OpenRouterInvoiceExtractor>()
            .AddHttpMessageHandler<OpenRouterAttemptRecorder>();
        return services;
    }

    /// <summary>
    /// Hangfire on PostgreSQL (schema <c>hangfire</c>) without Hangfire's static configuration: the storage, the job
    /// scheduler, the DI job activator (one scope per job run), the job tenant and the in-process server.
    /// </summary>
    public static IServiceCollection AddStocklyJobs(
        this IServiceCollection services, IConfiguration configuration, string postgresConnectionString)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(postgresConnectionString);

        services.AddValidatedOptions<HangfireOptions>(configuration, HangfireOptions.SectionName);
        services.AddSingleton(new HangfireStorage(postgresConnectionString));
        services.AddSingleton<HangfireJobScheduler>();
        services.AddSingleton<IBackgroundJobScheduler>(provider => provider.GetRequiredService<HangfireJobScheduler>());
        services.AddSingleton<InvoiceJobFailureFilter>();
        services.AddSingleton<IJobFilterProvider, StocklyJobFilterProvider>();
        services.AddSingleton<JobActivator>(provider => new AspNetCoreJobActivator(provider.GetRequiredService<IServiceScopeFactory>()));
        services.AddScoped<JobTenantContext>();
        services.AddTransient<ProcessInvoiceJob>();
        services.AddTransient<FailInvoiceProcessingJob>();
        services.AddHostedService<HangfireServerHost>();
        return services;
    }

    // Binds the section, validates it at startup with messages that name the environment variable to set, and registers
    // the bound value itself for constructor injection.
    private static void AddValidatedOptions<TOptions>(this IServiceCollection services, IConfiguration configuration, string sectionName)
        where TOptions : class
    {
        services.AddOptions<TOptions>().Bind(configuration.GetSection(sectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<TOptions>>(new EnvironmentVariableValidation<TOptions>(sectionName));
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<TOptions>>().Value);
    }
}
