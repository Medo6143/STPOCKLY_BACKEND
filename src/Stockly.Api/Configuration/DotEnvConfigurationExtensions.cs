using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;

namespace Stockly.Api.Configuration;

public static class DotEnvConfigurationExtensions
{
    /// <summary>
    /// Adds <c>.env</c> values just below the unprefixed environment variables, so the precedence is
    /// appsettings &lt; .env &lt; environment variables &lt; command line. <c>__</c> in keys maps to <c>:</c>.
    /// </summary>
    public static IConfigurationBuilder AddDotEnvBelowEnvironmentVariables(
        this IConfigurationBuilder builder, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(values);

        var source = new MemoryConfigurationSource
        {
            InitialData = values.Select(pair =>
                new KeyValuePair<string, string?>(pair.Key.Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal), pair.Value)),
        };

        var environmentIndex = IndexOfUnprefixedEnvironmentVariables(builder.Sources);
        if (environmentIndex < 0)
        {
            builder.Sources.Add(source);
        }
        else
        {
            builder.Sources.Insert(environmentIndex, source);
        }

        return builder;
    }

    /// <summary>In Development only: loads the repo-root .env when it exists.</summary>
    public static void AddRepositoryDotEnv(this IConfigurationBuilder builder, string contentRoot)
    {
        var path = DotEnvFile.FindInRepositoryRoot(contentRoot);
        if (path is not null)
        {
            builder.AddDotEnvBelowEnvironmentVariables(DotEnvFile.Parse(File.ReadAllLines(path)));
        }
    }

    private static int IndexOfUnprefixedEnvironmentVariables(IList<IConfigurationSource> sources)
    {
        for (var index = sources.Count - 1; index >= 0; index--)
        {
            if (sources[index] is EnvironmentVariablesConfigurationSource { Prefix: null or "" })
            {
                return index;
            }
        }

        return -1;
    }
}
