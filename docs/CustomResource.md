# Building Custom Resources

This article focuses on building totally custom resources, that are not based upon the DCP managed  `ExecutableResource` or `ContainerResource`.  

Whilst aspire does provide some low level hooks for customer resources, they are very low level and there are a bunch of magic ceremonies you need to participate in to to get full benefit form those hooks.
Most of these could be encapsulated in some form in the future, but they aren't right now.

## Project Structure gotchas

When creating the project structure for your first integration, there are a few pitfalls you can fall into

- When adding a reference to your integration package from an app host, you'll need to include `IsAspireProjectResource="false"` on the `ProjectReference`
  ```xml
    <ProjectReference Include="..\blah\blah.csproj" IsAspireProjectResource="false" />
  ```
  https://github.com/dotnet/aspire/issues/11156
- For tests, you'll likely want to use `DistributedApplicationTestingBuilder.Create()` rather than `DistributedApplicationTestingBuilder.Create<T>`.  This requires you to configure your test project to use the aspire sdk
  ```xml
  <Project Sdk="Aspire.AppHost.Sdk/13.1.0">
  ```
  This however introduces a few complications
  - You'll need to specify `IsAspireProjectResource` on the `ProjectReference` to your integration project.
  - `aspire update` will get confused in the future when you try to update packages. https://github.com/dotnet/aspire/issues/12053, 

Consider enabling `<GenerateDocumentationFile>true</GenerateDocumentationFile>` for your integration package.

## Resource Capabilities

These are some capabilities you and

### `IResourceWithWaitSupport`

There are two sides to waiting:

1. Not starting your resource until it's dependencies have started
2. Blocking other resources until your resource has started up.

To have your resource blocked until dependencies are ready, you need to ensure your resource raises the `BeforeResourceStartedEvent`, and blocks it's startup process until that event completes

```cs
await evt.Eventing.PublishAsync(new BeforeResourceStartedEvent(resource, evt.Services), ct);
```

For other resources to start after your resource starts, you need the `ResourceReadyEvent` to be fired for your resource.  **DO NOT PUBLISH THIS EVENT YOURSELF**.

This event is fired by the `ResourceHealthCheckService` health check, and is fired after the following occurs:

1. Your resource transitions into the `Running` state
2. All health checks attached to your resource become healthy.

So ensure you have updated your resource to the Running state when 

```cs
await evt.Notifications.PublishUpdateAsync(resource, x => x with
{
    State = KnownResourceStates.Running,
    StartTimeStamp = DateTime.UtcNow,
});
```

### Arguments

Implement `IResourceWithArgs` on your resource to make the `.WithArgs()` extension method usable on your resource

To consume these arguments in your resource, use `ExecutionConfigurationBuilder` when transitioning your resource into the `Starting` state

```cs
var executionContext = evt.Services.GetRequiredService<DistributedApplicationExecutionContext>();
var result = await ExecutionConfigurationBuilder.Create(resource)
    .WithEnvironmentVariablesConfig()
    .BuildAsync(executionContext, evt.Logger, ct);

var envVars = result.EnvironmentVariables;
// Now do something with env vars
```

To have args show on the dashboard, set the `resource.appArgs` and `resource.appArgsSensitivity` properties, when publishing the `Starting` event

- `resource.appArgs` is an `ImmutableArray<string>` of arguments, which each value being an argument
- `resource.appArgsSensitivity` is an `ImmutableArray<int>`.  It's length should be the same as `resource.appArgs`, which each element begin a `0` or `1` to indicate whether the argument at that position is a secret or not
  - Confusingly, dashboard thinks this is `ImmutableArray<bool>` rather than `ImmutableArray<int>`.
- if `resource.source` (`CustomResourceKnownProperties.Source`) is not set, args are not displayed

```cs
await evt.Notifications.PublishUpdateAsync(resource, s => s with
{
    StartTimeStamp = DateTime.UtcNow,
    State = KnownResourceStates.Starting,
    Properties = [
        new(CustomResourceKnownProperties.Source, "Source")
        ..SetArgumentProperties(s.Properties, executionConfiguration)
    ]
});

static ImmutableArray<ResourcePropertySnapshot> SetArgumentProperties(
    ImmutableArray<ResourcePropertySnapshot> existing,
    IExecutionConfigurationResult configurationResult)
{
    const string AppArgsPropertyName = "resource.appArgs";
    const string AppArgsSensitivityPropertyName = "resource.appArgsSensitivity";

    var args = configurationResult.Arguments.Select(x => x.Value).ToImmutableArray();
    var argSensitivity = configurationResult.Arguments.Select(x => Convert.ToInt32(x.IsSensitive)).ToImmutableArray();
    bool isSensitive = configurationResult.Arguments.Any(x => x.IsSensitive);

    return [
        .. existing.Where(p => p.Name is not AppArgsPropertyName and not AppArgsSensitivityPropertyName),
        new ResourcePropertySnapshot(AppArgsPropertyName, args) { IsSensitive = isSensitive },
        new ResourcePropertySnapshot(AppArgsSensitivityPropertyName, argSensitivity)
    ];
}
```

### Environment Variables

Implement `IResourceWithEnvironment` on your resource to make the `.WithEnvironment()` extension method usable on your resource

To consume these environment variables in your resource, use `ExecutionConfigurationBuilder` when transitioning your resource into the `Starting` state

```cs
var result = await ExecutionConfigurationBuilder.Create(resource)
    .WithArgumentsConfig()
    .BuildAsync(executionContext, evt.Logger, ct);
```

To make environment variables show up in the dashboard, you set the `EnvironmentVariables` property on `CustomResourceSnapshot` when transitioning to the `Starting` state.

```cs
var envVarSnapshot = executionConfiguration.EnvironmentVariables
    .Select(x => new EnvironmentVariableSnapshot(x.Key, x.Value, true))
    .ToImmutableArray()

await evt.Notifications.PublishUpdateAsync(resource, s => s with
{
    StartTimeStamp = DateTime.UtcNow,
    State = KnownResourceStates.Starting,
    EnvironmentVariables = envVarSnapshot,
});
```


### Endpoints

Implement `IResourceWithEndpoints`

TODO:
- Allocating endpoints
- Working with the tunnel
- CustomResourceSnapshot.Url
- WithUrls()

### Start / Stop commands

You have to implement these yourselves, but as commands are extensible, you can do much the same thing as the core of aspire.

https://github.com/dotnet/aspire/blob/b3742041345ea7373d9970584965a89c54e7af91/src/Aspire.Hosting/ApplicationModel/CommandsConfigurationExtensions.cs#L92

### Other

- `IResourceWithContainerFiles`
- `IResourceWithoutLifetime`
- `IResourceWithParameters`
- `IResourceWithParent` \ `IResourceWithParent<T>`
- `IResourceWithProbes`
- `IResourceWithServiceDiscovery`


## Key Apis

- `Resource`
- `IResourceAnnotation`
- `CustomResourceSnapshot`
