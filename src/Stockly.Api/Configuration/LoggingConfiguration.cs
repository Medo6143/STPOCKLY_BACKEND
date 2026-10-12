using Serilog;
using Serilog.Formatting.Compact;
using Stockly.Api.Middleware;

namespace Stockly.Api.Configuration;

public static class LoggingConfiguration
{
    private const string TextTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {" + CorrelationIdMiddleware.LogProperty + "} {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>Production: one compact JSON object per event. Elsewhere: readable text.</summary>
    public static LoggerConfiguration WriteToConsole(this LoggerConfiguration logger, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(environment);

        return environment.IsProduction()
            ? logger.WriteTo.Console(new RenderedCompactJsonFormatter())
            : logger.WriteTo.Console(outputTemplate: TextTemplate, formatProvider: System.Globalization.CultureInfo.InvariantCulture);
    }
}
