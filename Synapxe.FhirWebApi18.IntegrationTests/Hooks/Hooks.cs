using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TechTalk.SpecFlow;

namespace Synapxe.FhirWebApi18.IntegrationTests
{
    [Binding]
    public static class Hooks
    {
        private static readonly SemaphoreSlim appSyncLock = new(1);

        private static readonly ConcurrentDictionary<string, Task<DistributedApplication>> applicationCache = new();

        private static async Task<DistributedApplication> CreateApplication(string environmentName)
        {
            var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Synapxe_FhirWebApi18_AppHost>();

            var resources = appHost.Resources.OfType<ProjectResource>().Select(appHost.CreateResourceBuilder);
            foreach (var resource in resources)
            {
                resource.WithEnvironment("ASPNETCORE_ENVIRONMENT", environmentName);
                resource.WithEnvironment("ASPNETCORE_CONTENTROOT", Directory.GetCurrentDirectory());
            }

            appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
            {
                clientBuilder.AddStandardResilienceHandler(options =>
                {
                    options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(1);
                    options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2); // needs to be at least double the AttemptTimeout to pass options validation
                    options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(4);
                });
            });

            appHost.Services.AddLogging(loggingBuilder => loggingBuilder.ClearProviders().AddConsole().SetMinimumLevel(LogLevel.Trace));

            var app = await appHost.BuildAsync();
            app.Start();

            var resourceNotificationService = app.Services.GetRequiredService<ResourceNotificationService>();

            var completed = resourceNotificationService.WaitForResourceAsync(
                "server",
                KnownResourceStates.Running)
                .Wait(TimeSpan.FromSeconds(120));

            Console.WriteLine($"Server status: {(completed ? "running" : "not ready")}");

            SemaphoreSlim healthCheckSyncLock = new(1);
            await WaitUntilHealthy(app.CreateHttpClient("server"));

            return app;

            async Task WaitUntilHealthy(HttpClient client, string healthEndpoint = "/health", int maxAttempts = 5)
            {
                for (int i = 0; i < maxAttempts; i++)
                {
                    await healthCheckSyncLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        var response = await client.GetAsync(healthEndpoint).ConfigureAwait(false);
                        Console.WriteLine($"Healthcheck attempt {i + 1}: {await response.Content.ReadAsStringAsync()}");
                        if (response.IsSuccessStatusCode)
                        {
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        // do nothing
                        Console.WriteLine($"Healthcheck attempt {i + 1}: {ex}");
                    }
                    finally
                    {
                        healthCheckSyncLock.Release();
                    }
                }
            }
        }

        [BeforeFeature]
        public static void BeforeFeature(FeatureContext featureContext)
        {
            var environmentName = featureContext.FeatureInfo.Tags
                .Where(x => x.StartsWith("Environment:"))
                .Select(x => x[12..])
                .SingleOrDefault() ?? "Integration";
            featureContext.FeatureContainer.RegisterInstanceAs<Func<Task<HttpClient>>>(async () =>
            {
                await appSyncLock.WaitAsync();
                var application = await applicationCache.GetOrAdd(environmentName, CreateApplication);
                appSyncLock.Release();
                return application.CreateHttpClient("server");
            });
        }

        [AfterTestRun]
        public static void AfterTestRun()
        {
            foreach (var app in applicationCache)
            {
                app.Value.Dispose();
            }
        }
    }
}
