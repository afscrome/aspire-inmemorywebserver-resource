using System.Net;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#pragma warning disable EXTEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

namespace Aspire.Hosting.InMemoryWebServer.Tests;

public class BasicTests
{
    IDistributedApplicationTestingBuilder CreateBuilder()
    {
        var builder = DistributedApplicationTestingBuilder.Create();

        // Reduce noise due to https://github.com/dotnet/aspire/issues/6788
        builder.Services.ConfigureHttpClientDefaults(http => http.RemoveAllResilienceHandlers());
        return builder;
    }

    [Test]
    [Timeout(30_000)]
    public async Task BasicEndpointResponds(CancellationToken cancellationToken)
    {
        using var builder = CreateBuilder();

        var server = builder.AddInMemoryWebserver("test", builder =>
        {
            var app = builder.Build();
            app.MapGet("", () => "Hello World");
            return Task.FromResult(app);
        });

        using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);

        await app.ResourceNotifications.WaitForResourceAsync(server.Resource.Name, KnownResourceStates.Running, cancellationToken);

        using var client = app.CreateHttpClient(server.Resource.Name, "https");

        var response = await client.GetAsync("", cancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync(cancellationToken)).IsEqualTo("Hello World");
    }
}
