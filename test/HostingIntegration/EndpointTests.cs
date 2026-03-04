using System.Net;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Builder;
using TUnit.Core.Services;

namespace Aspire.Hosting.InMemoryWebServer.Tests;

public class EndpointTests
{
    [Test]
    [Timeout(Common.TimeoutSeconds)]
    public async Task EndpointRespondsOnHost(CancellationToken cancellationToken)
    {
        using var builder = Common.CreateBuilder();

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

    [Test]
    [Timeout(Common.TimeoutSeconds)]
    public async Task EndpointRespondsFromPwshContainer(CancellationToken cancellationToken)
    {
        using var builder = Common.CreateBuilder();

        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var server = builder.AddInMemoryWebserver("test", builder =>
        {
            var app = builder.Build();
            app.MapGet("", () =>
            {
                return "Hello Container";
            });
            return Task.FromResult(app);
        });

        var testContainer = builder
            .AddContainer("testContainer", "mcr.microsoft.com/dotnet/sdk")
            .WithEntrypoint("pwsh")
            .WithEnvironment("Endpoint", server.GetEndpoint("http"))
            .WithArgs(
                "-NoLogo",
                "-NoProfile",
                "-Command",
                """
                Write-Host 'Hello World'
                # Try work around https://github.com/dotnet/aspire/issues/13760
                Start-Sleep -Seconds 5;
                Invoke-RestMethod -Uri $endpoint -TimeoutSec 5
                """)
            .WaitFor(server);

        using var app = await builder.BuildAsync(cancellationToken);

        var loggerService = app.Services.GetRequiredService<ResourceLoggerService>();

        await app.StartAsync(cancellationToken);

        await app.ResourceNotifications.WaitForResourceAsync(server.Resource.Name, KnownResourceStates.Running, cancellationToken);
        var containerState = await app.ResourceNotifications.WaitForResourceAsync(testContainer.Resource.Name, KnownResourceStates.TerminalStates, cancellationToken);

        await Assert.That(containerState).IsEqualTo(KnownResourceStates.Exited);

        var output = await loggerService.GetAllAsync(testContainer.Resource).LastAsync();

        await Assert.That(output).IsEqualTo("Hello Container");
    }
}
