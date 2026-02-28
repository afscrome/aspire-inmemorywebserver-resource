# Building a customr esource

## Creating your Integration project

To start off creating an integration, you'll want:
- An App Host for manual test
- A class library for your (Hosting) integration
- A Test project to test your integration

Firstly start up by creating a type for your resource.
```cs
namespace My.Namespace
{
    public class MyResource([ResourceName] string name) : Resource(name)
    {
    }
}
```

Next create some extension methods for working with your resource.  Start off with an `AddXYZ` extension method on IDistributedApplicationBuilder, that returns `IResourceBuilder<T>` for the resource you just created

```cs
namespace Aspire.Hosting
{
    // TODO: can we use  extension members here?
    // Are they backwards compatible with net8/net9 tfms?
    public static class MyResourceExtensions {

        public static IResourceBuilder<MyResource> AddMyResource(this IDistributedApplicationBuilder builder, [ResourceName] string name)
        {
            var resource = new MyResource(name);
            return builder.AddResource(resource)
                .WithInitialState(new CustomResourceSnapshot
                {
                    ResourceType = "MyResource",
                    CreationTimeStamp = DateTime.UtcNow,
                    State = KnownResourceStates.NotStarted,
                    Properties = []
                })
                .OnInitializeResource(Initialise);

            async Task Initialise(MyResource resource, InitializeResourceEvent evt, CancellationToken ct)
            {
                // Kick off your resource's lifetime
                evt.Logger.LogInformation("Initialising MyResource {ResourceName}", resource.Name);
            }
        }
    }
}
```

Right now the resoruce's lifecycle is very bare bones, but lets' expand that with a bit more of the nesesecary aspire lifetime
```cs
public static IResourceBuilder<MyResource> WithSomething(this IResourceBuilder<MyResource> builder)
{
    // Do something - likely ultimately resulting in adding / mutating  an annotation for processing later on
    return builder;
}
```





