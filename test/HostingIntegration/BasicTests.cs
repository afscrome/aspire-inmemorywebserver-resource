using System.Net;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Builder;

namespace Aspire.Hosting.InMemoryWebServer.Tests;

public class BasicTests
{
    [Test]
    [Timeout(30_000)]
    public async Task BasicEndpointResponds(CancellationToken cancellationToken)
    {
        var builder = DistributedApplicationTestingBuilder.Create();

        var server = builder.AddInMemoryWebserver("test", builder =>
        {
            var app = builder.Build();
            app.MapGet("", () => "Hello World");
            return Task.FromResult(app);
        });

        var app = await builder.BuildAsync();
        await app.StartAsync();

        await app.ResourceNotifications.WaitForResourceAsync(server.Resource.Name, KnownResourceStates.Running, cancellationToken);

        using var client = app.CreateHttpClient(server.Resource.Name, "https");

        var response = await client.GetAsync("", cancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("Hello World");
    }
}
