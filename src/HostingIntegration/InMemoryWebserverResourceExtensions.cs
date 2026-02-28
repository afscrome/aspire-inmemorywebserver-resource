using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.InMemoryWebServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
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
                Properties = []
            })
            .WithOtlpExporter()
            .OnInitializeResource(Initialise)
            .WithHttpsEndpoint();


        async Task Initialise(InMemoryWebserverResource resource, InitializeResourceEvent evt, CancellationToken ct)
        {
#pragma warning disable ASPIRECERTIFICATES001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            var developerCertificateService = evt.Services.GetRequiredService<IDeveloperCertificateService>();
#pragma warning restore ASPIRECERTIFICATES001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

            var endpoint = resource.GetEndpoint("https");
            var endpointAnnotation = endpoint.EndpointAnnotation;

            // Publish BeforeStart event.  This will block if Waits have been configured
            await evt.Eventing.PublishAsync(new BeforeResourceStartedEvent(resource, evt.Services), ct);

            var executionConfiguration = await GetExecutionConfig();



            var envVarSnapshot = executionConfiguration.EnvironmentVariables.Select(x => new EnvironmentVariableSnapshot(x.Key, x.Value, true)).ToImmutableArray();

            await evt.Notifications.PublishUpdateAsync(resource, s => s with
            {
                StartTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Starting,
                EnvironmentVariables = envVarSnapshot
            });

            async Task<IExecutionConfigurationResult> GetExecutionConfig()
            {
                var executionConfigurationBuilder = ExecutionConfigurationBuilder.Create(resource)
                    .WithEnvironmentVariablesConfig()
                    .WithArgumentsConfig();

                var executionContext = evt.Services.GetRequiredService<DistributedApplicationExecutionContext>();
                return await executionConfigurationBuilder.BuildAsync(executionContext, evt.Logger, ct);
            }

            async Task<WebApplicationBuilder> CreateInitialBuilder()
            {
                var args = executionConfiguration.Arguments.Select(x => x.Value).ToArray();
                var builder = WebApplication.CreateBuilder(args);

                foreach (var envVar in executionConfiguration.EnvironmentVariables)
                {
                    builder.Configuration[envVar.Key] = envVar.Value;
                }

                ConfigureLogging();
                ConfigureOtel();
                await ConfigureUrls(builder, endpointAnnotation, ct);

                return builder;

                void ConfigureLogging()
                {
                    builder.Services.AddSingleton(evt.Services.GetRequiredService<IConfigureOptions<LoggerFilterOptions>>());

                    //builder.Logging.ClearProviders();
                    builder.Logging.AddProvider(new ForwardingLoggerProvider(evt.Logger));
                }

                void ConfigureOtel()
                {
                    if (resource.TryGetAnnotationsOfType<OtlpExporterAnnotation>(out var otlpAnnotation))
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

            //TODO: dispose properly, add start /stop commands
            WebApplication app;
            try
            {
                var builder = await CreateInitialBuilder();
                app = await configure(builder);

                await app.StartAsync(ct);

                AllocateEndpoint(app, endpointAnnotation, evt.Services);
                await evt.Eventing.PublishAsync(new ResourceEndpointsAllocatedEvent(resource, evt.Services), ct);

                await evt.Notifications.PublishUpdateAsync(resource, x => x with
                {
                    State = KnownResourceStates.Running,
                    StartTimeStamp = DateTime.UtcNow,
                    Urls = [
                        ..x.Urls,
                        new("http", endpoint.Url, false)
                    ],

                });
            }
            catch(Exception ex)
            {
                evt.Logger.LogError(ex, "Failed to start InMemoryWebServer resource");
                // Transition to failed to start
                await evt.Notifications.PublishUpdateAsync(resource, x => x with
                {
                    State = KnownResourceStates.FailedToStart,
                    StopTimeStamp = DateTime.UtcNow
                });
                throw;
            }

            int exitCode = 0;
            try
            {
                await app.WaitForShutdownAsync(ct);
            }
            catch
            {
                exitCode = 1;
                throw;
            }
            finally
            {
                await evt.Notifications.PublishUpdateAsync(resource, x => x with
                {
                    State = KnownResourceStates.Finished,
                    ExitCode = exitCode,
                    StopTimeStamp = DateTime.UtcNow
                });
            }
        }

        static void AllocateEndpoint(WebApplication app, EndpointAnnotation endpointAnnotation, IServiceProvider services)
        {
            var server = app.Services.GetRequiredService<IServer>();
            var addressFeature = server.Features.GetRequiredFeature<IServerAddressesFeature>();
            var actualAddress = addressFeature.Addresses.First();

            var actualUri = new Uri(actualAddress);


            var hostEndpoint = new AllocatedEndpoint(endpointAnnotation, actualUri.Host, actualUri.Port, EndpointBindingMode.SingleAddress, networkID: KnownNetworkIdentifiers.LocalhostNetwork);
            endpointAnnotation.AllocatedEndpointSnapshot.SetValue(hostEndpoint);

            var containerHostName = GetContainerHostName(services);

            var containerAllocatedEndpoint = new AllocatedEndpoint(endpointAnnotation, containerHostName, actualUri.Port, EndpointBindingMode.SingleAddress, networkID: KnownNetworkIdentifiers.DefaultAspireContainerNetwork);
            var snapshot = new ValueSnapshot<AllocatedEndpoint>();
            snapshot.SetValue(containerAllocatedEndpoint);

            endpointAnnotation.AllAllocatedEndpoints.TryAdd(KnownNetworkIdentifiers.DefaultAspireContainerNetwork, snapshot);


            // This is ugly, but the container name is derrived from several possible options, some of which are non public (_dcpInfo)
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

    }
}

