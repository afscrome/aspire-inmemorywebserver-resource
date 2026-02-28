using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging;
using My.Namespace;

namespace My.Namespace {
    public class MyResource([ResourceName] string name) : Resource(name)
    {
    }
}

// Put your extension method class in the `Aspire.Hosting` namespace so it shows up
// in auto completion without users needing ot know your namespace
namespace Aspire.Hosting {
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

        public static IResourceBuilder<MyResource> WithSomething(this IResourceBuilder<MyResource> builder)
        {
            // Do something - likely ultimatel resulting in adding / mutating  an annotation for processing later on
            return builder;
        }

    }
    
}