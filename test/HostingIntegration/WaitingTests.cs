namespace Aspire.Hosting.InMemoryWebServer.Tests;

public class WaitingTests
{
    [Test]
    [Timeout(Common.TimeoutSeconds)]
    public async Task InMemoryWebServerResourceGoesHealthy(CancellationToken cancellationToken)
    {
        using var builder = Common.CreateBuilder();

        var server = builder.AddInMemoryWebserver("test", builder =>
        {
            var app = builder.Build();
            return Task.FromResult(app);
        });

        using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);

        // Wait for the resource to reach the Running state
        await app.ResourceNotifications.WaitForResourceAsync(server.Resource.Name, KnownResourceStates.Running, cancellationToken);
    }

    [Test]
    [Timeout(Common.TimeoutSeconds)]
    public async Task InMemoryWebServerWaitsForOtherResource(CancellationToken cancellationToken)
    {
        using var builder = Common.CreateBuilder();

        //TODO: Replace with resource start / WithExplicitStart
        var tcs = new TaskCompletionSource();

        var blocker = AddFakeResource(builder, "blocker")
        .OnBeforeResourceStarted((_,_,_) => tcs.Task);
        var server = builder.AddInMemoryWebserver("test-server", webBuilder =>
        {
            var app = webBuilder.Build();
            return Task.FromResult(app);
        })
        .WaitFor(blocker);

        using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
      
        await app.ResourceNotifications.WaitForResourceAsync(server.Resource.Name, KnownResourceStates.Waiting, cancellationToken);
        tcs.SetResult();
        await app.ResourceNotifications.WaitForResourceAsync(blocker.Resource.Name, KnownResourceStates.Running, cancellationToken);
        await app.ResourceNotifications.WaitForResourceAsync(server.Resource.Name, KnownResourceStates.Running, cancellationToken);

    }

    [Test]
    [Timeout(Common.TimeoutSeconds)]
    public async Task OtherResourcesWaitForWebServer(CancellationToken cancellationToken)
    {
        using var builder = Common.CreateBuilder();
        //TODO: Replace with resource start / WithExplicitStart
        var tcs = new TaskCompletionSource();

        var server = builder.AddInMemoryWebserver("dependent-web-server", async webBuilder =>
        {
            var app = webBuilder.Build();
            await tcs.Task;
            return app;
        })
        .WithExplicitStart();

        var blockee = AddFakeResource(builder, "blockee")
        .WaitFor(server);

        using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);

        await app.ResourceNotifications.WaitForResourceAsync(blockee.Resource.Name, KnownResourceStates.Waiting, cancellationToken);
        tcs.SetResult();
        await app.ResourceNotifications.WaitForResourceAsync(server.Resource.Name, KnownResourceStates.Running, cancellationToken);
        await app.ResourceNotifications.WaitForResourceAsync(blockee.Resource.Name, KnownResourceStates.Running, cancellationToken);
    }

    IResourceBuilder<FakeResource> AddFakeResource(IDistributedApplicationBuilder builder, string name)
    {
        var resource = new FakeResource(name);

        return builder.AddResource(resource)
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "FakeResource",
                State = KnownResourceStates.NotStarted,
                Properties = []
            })
            .OnInitializeResource(async (res, evt, ct) => 
            {
                await evt.Eventing.PublishAsync(new BeforeResourceStartedEvent(res, evt.Services), ct);
                await evt.Notifications.PublishUpdateAsync(res, x => x with { State = KnownResourceStates.Running });
            });
    }
}

internal class FakeResource([ResourceName] string name) : Resource(name), IResourceWithWaitSupport
{
}

