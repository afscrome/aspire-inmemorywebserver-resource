using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#pragma warning disable EXTEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

namespace Aspire.Hosting.InMemoryWebServer.Tests;

public static class Common
{
    public const int TimeoutSeconds = 15_000;

    public static IDistributedApplicationTestingBuilder CreateBuilder()
    {
        Environment.SetEnvironmentVariable("DCP_DIAGNOSTICS_LOG_LEVEL", "debug");
        var builder = DistributedApplicationTestingBuilder.Create();

        // Reduce noise due to https://github.com/dotnet/aspire/issues/6788
        builder.Services.ConfigureHttpClientDefaults(http => http.RemoveAllResilienceHandlers());
        builder.Services.AddLogging(x => x
            .AddFilter("", LogLevel.Debug)
            .SetMinimumLevel(LogLevel.Trace)
        );
        return builder;
    }
}
