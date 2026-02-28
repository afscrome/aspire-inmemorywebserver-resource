# Aspire InMemoryWebServer Hosting Integration

An exploration of building totally custom resources in Aspire.  A resource that is not based on the built in `ContainerResource` or `ExecutableResource`

[docs/CustomResource.md](docs/CustomResource.md) and [docs/Tutorial.md](docs/Tutorial.md) documents more about some of the process / challenges identified with writing custom resources.

## In Memory Web Server

This resource represents an in memory web server, intended for some very light weight API endpoints to be hosted within the app host itself.

```cs
builder.AddInMemoryWebserver("test", builder => {
    var app = builder.Build();
    app.MapGet("/", () => "Service Discovery is running");
    return Task.FromResult(app);
})
.WithArgs("--ConnectionString", cache)
.WithEnvironment("FOO", "bar");
```

It is loosely based on [DashboardServiceHost](https://github.com/dotnet/aspire/blob/main/src/Aspire.Hosting/Dashboard/DashboardServiceHost.cs) from Aspire.


## Running the Sample

```bash
aspire run
```

## Contributing

Tests should cover:
- Feature functionality
- Edge cases (empty args, secrets, failures)
- State transitions
- Resource property correctness

## License

See LICENSE file for details.
