using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable EXTEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

namespace Aspire.Hosting.InMemoryWebServer.Tests;

public static class Common
{
    public const int TimeoutSeconds = 30_000;  // Increased from 15s to 30s for CI environments

    public static IDistributedApplicationTestingBuilder CreateBuilder()
    {
        var builder = DistributedApplicationTestingBuilder.Create();

        // Reduce noise due to https://github.com/dotnet/aspire/issues/6788
        builder.Services.ConfigureHttpClientDefaults(http => http.RemoveAllResilienceHandlers());
        return builder;
    }
}
