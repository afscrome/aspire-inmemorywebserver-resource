using Microsoft.Extensions.Configuration.CommandLine;
using System.Collections.Immutable;

namespace Aspire.Hosting.InMemoryWebServer.Tests;

public class ArgumentsTests
{
    [Test]
    [Timeout(Common.TimeoutSeconds)]
    public async Task ArgsPassedToWebApplicationCorrectly(CancellationToken cancellationToken)
    {
        using var builder = Common.CreateBuilder();

        CommandLineConfigurationSource? args = null;

        var server = builder.AddInMemoryWebserver("test", builder =>
        {
            var app = builder.Build();
            args = builder.Configuration.Sources.OfType<CommandLineConfigurationSource>().Single();

            return Task.FromResult(app);
        });

        server.WithArgs("--TestArg", "SuccessfullyPassed");

        using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);

        await Assert.That(args).IsNotNull();
        await Assert.That(args.Args).IsEquivalentTo(["--TestArg", "SuccessfullyPassed"]);
    }

    [Test]
    [Timeout(Common.TimeoutSeconds)]
    public async Task ArgsEvaluatedOnlyOnce(CancellationToken cancellationToken)
    {
        using var builder = Common.CreateBuilder();

        var server = builder.AddInMemoryWebserver("test", webAppBuilder =>
        {
            var app = webAppBuilder.Build();
            return Task.FromResult(app);
        });

        int argsEvaluationCount = 0;
        server.WithArgs(ctx => { argsEvaluationCount++; });

        using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);

        await Assert.That(argsEvaluationCount).IsEqualTo(1);
    }

    [Test]
    [Timeout(Common.TimeoutSeconds)]
    public async Task ArgsPublishedToResourceEventProperties(CancellationToken cancellationToken)
    {
        using var builder = Common.CreateBuilder();

        var server = builder.AddInMemoryWebserver("test", async webAppBuilder =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            var app = webAppBuilder.Build();
            return app;
        });

        server.WithArgs("--Location", "Cerulean Cave", "--Creature", "Mewtwo");

        using var app = await builder.BuildAsync(cancellationToken);

        await app.StartAsync(cancellationToken);

        await app.ResourceNotifications.WaitForResourceAsync(server.Resource.Name, KnownResourceStates.Starting, cancellationToken);
        app.ResourceNotifications.TryGetCurrentState(server.Resource.Name, out var state);

        await Assert.That(state).IsNotNull();
        var appArgsProperty = await Assert.That(state.Snapshot.Properties).Contains(x => x.Name == "resource.appArgs");
        var appArgsSensitivityProperty = await Assert.That(state.Snapshot.Properties).Contains(x => x.Name == "resource.appArgsSensitivity");

        var appArgs = await Assert.That(appArgsProperty.Value).IsTypeOf<ImmutableArray<string>>();
        await Assert.That(appArgs).IsEquivalentTo(["--Location", "Cerulean Cave", "--Creature", "Mewtwo"]);
        await Assert.That(appArgsProperty.IsSensitive).IsFalse();

        var appArgsSensitivity = await Assert.That(appArgsSensitivityProperty.Value).IsTypeOf<ImmutableArray<int>>();
        await Assert.That(appArgsSensitivity).IsEquivalentTo([0, 0, 0, 0]);
        await Assert.That(appArgsSensitivityProperty.IsSensitive).IsFalse();
    }

    [Test]
    [Timeout(Common.TimeoutSeconds)]
    public async Task ArgsWithSecretPublishedToResourceEventProperties(CancellationToken cancellationToken)
    {
        using var builder = Common.CreateBuilder();

        var secret = builder.AddParameter("location", "Under the truck", secret: true);
        var server = builder.AddInMemoryWebserver("test", async webAppBuilder =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            var app = webAppBuilder.Build();
            return app;
        });

        server.WithArgs("--Location", secret, "--Creature", "Mew");

        using var app = await builder.BuildAsync(cancellationToken);

        await app.StartAsync(cancellationToken);

        await app.ResourceNotifications.WaitForResourceAsync(server.Resource.Name, KnownResourceStates.Starting, cancellationToken);
        app.ResourceNotifications.TryGetCurrentState(server.Resource.Name, out var state);

        await Assert.That(state).IsNotNull();
        var appArgsProperty = await Assert.That(state.Snapshot.Properties).Contains(x => x.Name == "resource.appArgs");
        var appArgsSensitivityProperty = await Assert.That(state.Snapshot.Properties).Contains(x => x.Name == "resource.appArgsSensitivity");

        var appArgs = await Assert.That(appArgsProperty.Value).IsTypeOf<ImmutableArray<string>>();
        await Assert.That(appArgs).IsEquivalentTo(["--Location", "Under the truck", "--Creature", "Mew"]);
        await Assert.That(appArgsProperty.IsSensitive).IsTrue();

        var appArgsSensitivity = await Assert.That(appArgsSensitivityProperty.Value).IsTypeOf<ImmutableArray<int>>();
        await Assert.That(appArgsSensitivity).IsEquivalentTo([0, 1, 0, 0]);
        await Assert.That(appArgsSensitivityProperty.IsSensitive).IsFalse();
    }
}
