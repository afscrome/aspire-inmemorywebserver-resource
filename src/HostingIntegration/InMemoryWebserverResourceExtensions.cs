using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.InMemoryWebServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using System.Collections.Immutable;
using System.Net;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Aspire.Hosting;
#pragma warning restore IDE0130 // Namespace does not match folder structure

// Semi based on https://github.com/dotnet/aspire/blob/main/src/Aspire.Hosting/Dashboard/DashboardServiceHost.cs
public static class InMemoryWebserverResourceExtensions
{
    public static IResourceBuilder<InMemoryWebserverResource> AddInMemoryWebserver(this IDistributedApplicationBuilder builder, [ResourceName] string name, Func<WebApplicationBuilder, Task<WebApplication>> configure)
    {
        var resource = new InMemoryWebserverResource(name);
        return builder.AddResource(resource)
            .WithIconName("GlobeDesktop")
            .WithInitialState(new CustomResourceSnapshot // Aspire type for custom resource state.
            {
                ResourceType = "InMemoryWebServer",
                CreationTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.NotStarted,
                Properties = [
                    new(CustomResourceKnownProperties.Source, ".")
                ],
            })
            .WithOtlpExporter()
            .OnInitializeResource(Initialise)
            .WithHttpsEndpoint();

        async Task Initialise(InMemoryWebserverResource resource, InitializeResourceEvent evt, CancellationToken ct)
        {
            WebApplication? app = default;
            try
            {
                var endpoint = resource.GetEndpoint("https");
                var endpointAnnotation = endpoint.EndpointAnnotation;
                var executionContext = evt.Services.GetRequiredService<DistributedApplicationExecutionContext>();

                // Publish BeforeStart event.  This will block if Waits have been configured
                await evt.Eventing.PublishAsync(new BeforeResourceStartedEvent(resource, evt.Services), ct);

                var executionConfiguration = await BuildExecutionConfig(resource, executionContext, evt.Logger, ct);

                await evt.Notifications.PublishUpdateAsync(resource, s => s with
                {
                    StartTimeStamp = DateTime.UtcNow,
                    State = KnownResourceStates.Starting,
                    EnvironmentVariables = [.. executionConfiguration.EnvironmentVariables.Select(x => new EnvironmentVariableSnapshot(x.Key, x.Value, true))],
                    Properties = SetArgumentProperties(s.Properties, executionConfiguration)
                });

                app = await BuildWebApp(evt, configure, executionConfiguration, endpointAnnotation, ct);

                await app.StartAsync(ct);

                await AllocateEndpoints(app, endpointAnnotation, evt, ct);

                await evt.Notifications.PublishUpdateAsync(resource, x => x with
                {
                    State = KnownResourceStates.Running,
                    StartTimeStamp = DateTime.UtcNow,
                });
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    evt.Logger.LogError(ex, "Failed to start InMemoryWebServer resource");
                }

                if (app is not null)
                {
                    await app.DisposeAsync();
                }

                await evt.Notifications.PublishUpdateAsync(resource, x => x with
                {
                    State = KnownResourceStates.FailedToStart,
                    StopTimeStamp = DateTime.UtcNow,
                });

                throw;
            }


            int exitCode = 0;
            try
            {
                //TODO: A better delay
                await Task.Delay(Timeout.Infinite, ct);
                await app.WaitForShutdownAsync(ct);
            }
            catch
            {
                exitCode = ct.IsCancellationRequested ? 0 : 1;
                throw;
            }
            finally
            {
                await app.DisposeAsync();

                await evt.Notifications.PublishUpdateAsync(resource, x => x with
                {
                    State = KnownResourceStates.Finished,
                    ExitCode = exitCode,
                    StopTimeStamp = DateTime.UtcNow
                });
            }
        }
    }

    static async Task<WebApplication> BuildWebApp(InitializeResourceEvent evt, Func<WebApplicationBuilder, Task<WebApplication>> configure, IExecutionConfigurationResult executionConfigurationResult, EndpointAnnotation endpointAnnotation, CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();

        ConfigureConfiguration();
        ConfigureLogging();
        ConfigureOtel();

        //TODO: Configure TLs based on certs?
        await ConfigureUrls(builder, endpointAnnotation, ct);

        var app = await configure(builder);
        return app;

        void ConfigureConfiguration()
        {
            string[] args = [.. executionConfigurationResult.Arguments.Select(x => x.Value)];
            var envVars = executionConfigurationResult.EnvironmentVariables.ToDictionary(x => x.Key, x => (string?)x.Value);
            builder.Configuration
                .AddCommandLine(args)
                .AddInMemoryCollection(envVars);
        }

        void ConfigureLogging()
        {
            builder.Services.AddSingleton(evt.Services.GetRequiredService<IConfigureOptions<LoggerFilterOptions>>());
            builder.Logging.AddProvider(new ForwardingLoggerProvider(evt.Logger));
        }

        void ConfigureOtel()
        {
            if (evt.Resource.TryGetAnnotationsOfType<OtlpExporterAnnotation>(out var otlpAnnotation))
            {
                builder.Services
                    .AddOpenTelemetry()
                    .UseOtlpExporter();
            }
        }

        async Task ConfigureUrls(WebApplicationBuilder builder, EndpointAnnotation endpointAnnotation, CancellationToken ct)
        {
            builder.Configuration["ASPNETCORE_PREFERHOSTINGURLS"] = "true";

            var targetAddress = endpointAnnotation.TargetHost;

            var dnsEntry = await Dns.GetHostEntryAsync(endpointAnnotation.TargetHost, ct);
            //if entry is localhost
            if (dnsEntry.AddressList.Any(x => x.Equals(IPAddress.Loopback)))
            {
                targetAddress = IPAddress.Loopback.ToString();
            }
            else if (dnsEntry.AddressList.Any(x => x.Equals(IPAddress.IPv6Loopback)))
            {
                targetAddress = IPAddress.IPv6Loopback.ToString();
            }

            var url = new UriBuilder
            {
                Scheme = endpointAnnotation.UriScheme,
                Host = targetAddress,
                Port = endpointAnnotation.TargetPort ?? 0
            }.ToString();

            builder.WebHost.UseUrls(url);
            if (endpointAnnotation.UriScheme == "https")
            {
                builder.WebHost.UseKestrelHttpsConfiguration();
            }
        }
    }

    static async Task AllocateEndpoints(WebApplication app, EndpointAnnotation endpointAnnotation, InitializeResourceEvent evt, CancellationToken ct)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addressFeature = server.Features.GetRequiredFeature<IServerAddressesFeature>();
        var actualAddress = addressFeature.Addresses.First();

        var actualUri = new Uri(actualAddress);

        var hostEndpoint = new AllocatedEndpoint(endpointAnnotation, actualUri.Host, actualUri.Port, EndpointBindingMode.SingleAddress, networkID: KnownNetworkIdentifiers.LocalhostNetwork);

        var containerHostName = GetContainerHostName(evt.Services);
        var containerAllocatedEndpoint = new AllocatedEndpoint(endpointAnnotation, containerHostName, actualUri.Port, EndpointBindingMode.SingleAddress, networkID: KnownNetworkIdentifiers.DefaultAspireContainerNetwork);

        endpointAnnotation.AllAllocatedEndpoints.AddOrUpdateAllocatedEndpoint(KnownNetworkIdentifiers.LocalhostNetwork, hostEndpoint);
        endpointAnnotation.AllAllocatedEndpoints.AddOrUpdateAllocatedEndpoint(KnownNetworkIdentifiers.DefaultAspireContainerNetwork, containerAllocatedEndpoint);

        await evt.Eventing.PublishAsync(new ResourceEndpointsAllocatedEvent(evt.Resource, evt.Services), ct);
        await evt.Notifications.PublishUpdateAsync(evt.Resource, x => x with
        {
            Urls = [
                ..x.Urls,
                new("http", hostEndpoint.UriString, false)
            ],
        });


        // This is ugly, but the container name is derived from several possible options, some of which are non public (_dcpInfo)
        // so I can't even replicate the checks against IConfiguration myself 
        static string GetContainerHostName(IServiceProvider services)
        {
            var dcpExecutorType = Type.GetType("Aspire.Hosting.Dcp.IDcpExecutor, Aspire.Hosting")
                ?? throw new InvalidOperationException("Failed to get DcpExecutor");

            var dcpExecutor = services.GetRequiredService(dcpExecutorType);

            var hostNameMethod = dcpExecutor.GetType().GetMethod("get_ContainerHostName", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Failed to get ContainerHostName from DcpExecutor");

            return (string)hostNameMethod.Invoke(dcpExecutor, [])!;
        }
    }

    static ImmutableArray<ResourcePropertySnapshot> SetArgumentProperties(
        ImmutableArray<ResourcePropertySnapshot> existing,
        IExecutionConfigurationResult configurationResult)
    {
        // From internal  `KnownProperties.Resource` strings
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

    static async Task<IExecutionConfigurationResult> BuildExecutionConfig(IResource resource, DistributedApplicationExecutionContext executionContext, ILogger logger, CancellationToken ct)
    {
        var executionConfigurationBuilder = ExecutionConfigurationBuilder.Create(resource);

        if (resource is IResourceWithArgs)
        {
            executionConfigurationBuilder.WithArgumentsConfig();
        }
        if (resource is IResourceWithEnvironment)
        {
            executionConfigurationBuilder.WithEnvironmentVariablesConfig();
        }

        return await executionConfigurationBuilder.BuildAsync(executionContext, logger, ct);
    }

}
