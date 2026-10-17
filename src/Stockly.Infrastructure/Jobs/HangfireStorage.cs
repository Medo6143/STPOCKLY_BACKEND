using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;

namespace Stockly.Infrastructure.Jobs;

/// <summary>
/// Hangfire's PostgreSQL storage in schema <c>hangfire</c>, created on first use. Creating it installs or upgrades
/// Hangfire's own tables (their DDL belongs to the Hangfire version, backend.md §17.1 item 8), which needs the database:
/// creating it lazily keeps the Api starting while PostgreSQL is down. A failed attempt is retried on the next call.
/// Singleton; never uses Hangfire's static <c>JobStorage.Current</c>.
/// </summary>
public sealed class HangfireStorage(string connectionString)
{
    private readonly Lock _gate = new();
    private volatile JobStorage? _storage;

    public JobStorage Get()
    {
        if (_storage is { } storage)
        {
            return storage;
        }

        lock (_gate)
        {
            return _storage ??= Create();
        }
    }

    private PostgreSqlStorage Create()
    {
        var options = new PostgreSqlStorageOptions
        {
            SchemaName = HangfireOptions.Schema,
            PrepareSchemaIfNecessary = true,

            // LISTEN/NOTIFY: a queued scan starts at once instead of at the next queue poll (the app polls for 60 s).
            EnableLongPolling = true,
        };
        return new PostgreSqlStorage(new NpgsqlConnectionFactory(connectionString, options), options);
    }
}
