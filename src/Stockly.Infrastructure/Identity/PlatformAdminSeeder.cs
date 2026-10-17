using System.Data.Common;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stockly.Application.Features.Admin.SeedPlatformAdmin;
using Stockly.Domain.Common;

namespace Stockly.Infrastructure.Identity;

/// <summary>
/// Creates the first platform admin at start-up from <see cref="AdminSeedOptions"/> when none exists (contract §11.1),
/// through <see cref="SeedPlatformAdminCommand"/>. The first attempt runs during start-up, so the admin exists once the
/// host has started; when PostgreSQL is down it retries in the background instead of failing start-up. Logs the outcome
/// and the user id, never the email or the password (rule 12).
/// </summary>
public sealed partial class PlatformAdminSeeder(
    IServiceScopeFactory scopes,
    AdminSeedOptions options,
    IHostApplicationLifetime lifetime,
    ILogger<PlatformAdminSeeder> logger) : IHostedService
{
    /// <summary>Wait between two attempts while PostgreSQL is unreachable.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    private Task _retry = Task.CompletedTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.IsComplete)
        {
            LogNotConfigured(logger);
            return;
        }

        if (!await TrySeedAsync(cancellationToken))
        {
            _retry = RetryAsync(lifetime.ApplicationStopping);
        }
    }

    // The retry loop ends with the host's ApplicationStopping token, which fires before StopAsync.
    public async Task StopAsync(CancellationToken cancellationToken) =>
        await _retry.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

    private async Task RetryAsync(CancellationToken stoppingToken)
    {
        try
        {
            do
            {
                await Task.Delay(RetryDelay, stoppingToken);
            }
            while (!await TrySeedAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }

    // False only when PostgreSQL could not be reached; every other outcome is final and logged.
    private async Task<bool> TrySeedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var result = await sender.Send(
                new SeedPlatformAdminCommand(options.Email!, options.Password!, options.FullName!), cancellationToken);
            Log(result);
            return true;
        }
        catch (Exception exception) when (exception is DbException or TimeoutException)
        {
            LogDatabaseUnavailable(logger, exception.GetType().Name, (int)RetryDelay.TotalSeconds);
            return false;
        }
    }

    private void Log(Result<SeedPlatformAdminOutcome> result)
    {
        if (result.IsFailure)
        {
            LogRejected(logger, result.Error.Code);
        }
        else if (result.Value is { Created: true, UserId: { } userId })
        {
            LogCreated(logger, userId);
        }
        else
        {
            LogAlreadyExists(logger);
        }
    }

    [LoggerMessage(EventId = 80, Level = LogLevel.Information, Message = "Platform admin {UserId} created from configuration")]
    private static partial void LogCreated(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 81, Level = LogLevel.Information, Message = "A platform admin exists; the configured admin was not seeded")]
    private static partial void LogAlreadyExists(ILogger logger);

    [LoggerMessage(
        EventId = 82,
        Level = LogLevel.Error,
        Message = "The configured platform admin was not created ({Code}); check Admin__Email, Admin__Password and Admin__FullName")]
    private static partial void LogRejected(ILogger logger, string code);

    [LoggerMessage(
        EventId = 83,
        Level = LogLevel.Information,
        Message = "Platform admin seeding skipped: Admin__Email, Admin__Password or Admin__FullName is not set")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(
        EventId = 84,
        Level = LogLevel.Warning,
        Message = "Platform admin seeding waits for PostgreSQL ({ExceptionType}); retrying in {RetrySeconds} s")]
    private static partial void LogDatabaseUnavailable(ILogger logger, string exceptionType, int retrySeconds);
}
