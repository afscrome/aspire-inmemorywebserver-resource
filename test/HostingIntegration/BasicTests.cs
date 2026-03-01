using System.Net;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Builder;

namespace Aspire.Hosting.InMemoryWebServer.Tests;

public class BasicTests
{
    [Test]
    [Timeout(Common.TimeoutSeconds)]
    public async Task BasicEndpointResponds(CancellationToken cancellationToken)
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
}
