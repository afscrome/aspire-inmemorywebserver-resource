using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

#pragma warning disable EXTEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

namespace Aspire.Hosting.InMemoryWebServer.Tests;

public class FailureTests
{
    [Test]
    [Timeout(Common.TimeoutSeconds)]
    public async Task ResourceEntersFailedToStartStateWhenCallbackThrows(CancellationToken cancellationToken)
    {
        using var builder = Common.CreateBuilder();

        var server = builder.AddInMemoryWebserver("test", builder =>
        {
            throw new InvalidOperationException("Simulated callback error");
        });

        using var app = await builder.BuildAsync(cancellationToken);
        
        // Start the app - the resource should fail
        await app.StartAsync(cancellationToken);

        // Wait for the resource to finish
        var terminalState = await app.ResourceNotifications.WaitForResourceAsync(
            server.Resource.Name, 
            KnownResourceStates.TerminalStates, 
            cancellationToken);

        await Assert.That(terminalState).IsEqualTo(KnownResourceStates.FailedToStart);
        
        // Verify StopTimeStamp is set
        app.ResourceNotifications.TryGetCurrentState(server.Resource.Name, out var state);
        await Assert.That(state?.Snapshot.StopTimeStamp).IsNotNull();
    }

    [Test]
    [Timeout(Common.TimeoutSeconds)]
    public async Task ResourceEntersFailedToStartStateWhenStartAsyncThrows(CancellationToken cancellationToken)
    {
        using var builder = Common.CreateBuilder();

        var server = builder.AddInMemoryWebserver("test", webAppBuilder =>
        {
            // Add a hosted service that throws during startup
            webAppBuilder.Services.AddHostedService<FailingHostedService>();
            
            var app = webAppBuilder.Build();
            return Task.FromResult(app);
        });

        using var app = await builder.BuildAsync(cancellationToken);
        
        // Start the app - the resource should fail during startup
        await app.StartAsync(cancellationToken);

        // Wait for the resource to finish
        var terminalState = await app.ResourceNotifications.WaitForResourceAsync(
            server.Resource.Name, 
            KnownResourceStates.TerminalStates, 
            cancellationToken);

        await Assert.That(terminalState).IsEqualTo(KnownResourceStates.FailedToStart);
        
        // Verify StopTimeStamp is set
        app.ResourceNotifications.TryGetCurrentState(server.Resource.Name, out var state);
        await Assert.That(state?.Snapshot.StopTimeStamp).IsNotNull();
    }

    private class FailingHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Simulated hosted service startup error");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
