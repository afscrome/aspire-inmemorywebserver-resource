using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.InMemoryWebServer;

public class InMemoryWebserverResource([ResourceName] string name) : Resource(name),
    IResourceWithArgs, IResourceWithEndpoints, IResourceWithEnvironment, IResourceWithWaitSupport
{
}

