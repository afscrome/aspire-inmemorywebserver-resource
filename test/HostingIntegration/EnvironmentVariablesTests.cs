using Microsoft.Extensions.Configuration;

namespace Aspire.Hosting.InMemoryWebServer.Tests;

public class EnvironmentVariablesTests
{
    [Test]
    [Timeout(Common.TimeoutSeconds)]
    public async Task EnvVarsPassedToWebApplicationCorrectly(CancellationToken cancellationToken)
    {
        using var builder = Common.CreateBuilder();

        IConfigurationRoot? configuration = default;

        var server = builder.AddInMemoryWebserver("test", webAppBuilder =>
        {
            var app = webAppBuilder.Build();
            configuration = webAppBuilder.Configuration;
            return Task.FromResult(app);
        });

        server.WithEnvironment("PROFESSOR", "OAK");

        using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);

        await Assert.That(configuration?["PROFESSOR"]).IsEqualTo("OAK");
    }

    [Test]
    [Timeout(Common.TimeoutSeconds)]
    public async Task EnvVarsEvaluatedOnlyOnce(CancellationToken cancellationToken)
    {
        using var builder = Common.CreateBuilder();

        var server = builder.AddInMemoryWebserver("test", webAppBuilder =>
        {
            var app = webAppBuilder.Build();
            return Task.FromResult(app);
        });

        int envVarEvaluationCount = 0;
        server.WithEnvironment(ctx => {
            envVarEvaluationCount++;
        });

        using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);

        // The configuration was accessed exactly once during WebApplicationBuilder setup
        await Assert.That(envVarEvaluationCount).IsEqualTo(1);
    }

    [Test]
    [Timeout(Common.TimeoutSeconds)]
    public async Task EnvVarsPopulatedInResourceEventsBeforeStart(CancellationToken cancellationToken)
    {
        using var builder = Common.CreateBuilder();

        var server = builder.AddInMemoryWebserver("test", async webAppBuilder =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            var app = webAppBuilder.Build();
            return app;
        });

        server
            .WithEnvironment("GRASS", "Bulbasur")
            .WithEnvironment("FIRE", "Charmander")
            .WithEnvironment("WATER", "Squirtle");

        using var app = await builder.BuildAsync(cancellationToken);
       
        await app.StartAsync(cancellationToken);

        await app.ResourceNotifications.WaitForResourceAsync(server.Resource.Name, KnownResourceStates.Starting, cancellationToken);
        app.ResourceNotifications.TryGetCurrentState(server.Resource.Name, out var state);

        var envVars = state?.Snapshot.EnvironmentVariables.ToDictionary(x => x.Name, x => x.Value);

        await Assert.That(envVars).ContainsKeyWithValue("GRASS", "Bulbasur")
            .And.ContainsKeyWithValue("FIRE", "Charmander")
            .And.ContainsKeyWithValue("WATER", "Squirtle");
    }
}
