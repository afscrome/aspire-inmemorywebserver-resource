using Microsoft.AspNetCore.Builder;

var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");

var server = builder.AddProject<Projects.Aspire_InMemoryWebServer_Server>("server")
    .WithReference(cache)
    .WaitFor(cache)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var webServer = builder.AddInMemoryWebserver("test", builder =>
{
    var app = builder.Build();
    app.MapGet("/", () => "Service Discovery is running");
    return Task.FromResult(app);
});

var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithReference(server)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
