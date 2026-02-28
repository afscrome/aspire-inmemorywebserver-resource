using Microsoft.Extensions.Configuration.CommandLine;

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
}
