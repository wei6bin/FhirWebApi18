using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath;
using Ihis.FhirEngine.Client;
using Ihis.FhirEngine.Core.Data;
using Ihis.FhirEngine.Data.Testing;
using Microsoft.Extensions.Primitives;
using TechTalk.SpecFlow;
using Xunit;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Synapxe.FhirWebApi18.IntegrationTests.Steps
{
    [Binding]
    public sealed class FhirStepDefinitions
    {
        private readonly ScenarioContext scenarioContext;
        private readonly ITestOutputHelper testOutput;
        private readonly PatchService patchService;
        private readonly Func<Task<HttpClient>> httpClientFactory;

        public FhirStepDefinitions(ScenarioContext scenarioContext, Func<Task<HttpClient>> httpClientFactory, ITestOutputHelper testOutput)
        {
            this.scenarioContext = scenarioContext;
            this.httpClientFactory = httpClientFactory;
            this.testOutput = testOutput;
            patchService = new PatchService(new FhirPathCompiler());
        }

        [StepArgumentTransformation]
        public Dictionary<string, StringValues> HeaderValuesTransform(Table headers)
        {
            var result = new Dictionary<string, StringValues>();
            foreach (var item in headers.Rows)
            {
                result.Add(item["HeaderName"], item["Value"]);
            }

            return result;
        }

        private async Task<HttpClient> CreateClient(Dictionary<string, StringValues> headers)
        {
            var client = await httpClientFactory();

            foreach (var item in headers)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(item.Key, (IEnumerable<string>)item.Value);
            }

            return client;
        }

        [Given(@"a new FhirClient as (\w+)")]
        public async Task CreateNewFhirClient(string name, Dictionary<string, StringValues> headers)
        {
            var client = new FhirHttpClient(await CreateClient(headers), ModelInfo.ModelInspector);

            scenarioContext.SetFhirClient(client, name);
        }

        [Given(@"a new HttpClient as (\w+)")]
        public async Task CreateNewHttpClient(string name, Dictionary<string, StringValues> headers)
        {
            var client = await CreateClient(headers);

            scenarioContext.SetHttpClient(client, name);
        }

        [Given(@"the FhirClient (\w+) is used")]
        [When(@"using FhirClient (\w+)")]
        public void UseFhirClient(string name)
        {
            scenarioContext.Set(name, nameof(FhirClient));
        }

        [Given(@"the HttpClient (\w+) is used")]
        [When(@"using HttpClient (\w+)")]
        public void UseHttpClient(string name)
        {
            scenarioContext.Set(name, nameof(HttpClient));
        }

        [Given("a random tag")]
        public void RandomTag()
        {
            scenarioContext.SetCurrentTag(Guid.NewGuid().ToString("N"));
        }

        [Given(@"a Resource is created from (.+) as (\w+)")]
        [When(@"creating from (.+) as (\w+)")]
        public Task GivenANewResource(string jsonPath, string destination)
            => GivenANewResourceImpl(jsonPath, null, destination);

        [Given(@"a Resource is created from (.+) with data as (\w+)")]
        [When(@"creating from (.+) with data as (\w+)")]
        public Task GivenANewResource(string jsonPath, string destination, Table data)
            => GivenANewResourceImpl(jsonPath, data, destination);

        private async Task GivenANewResourceImpl(string jsonPath, Table data, string destination)
        {
            FhirJsonParser parser = new FhirJsonParser();
            var json = File.ReadAllText(jsonPath);
            var resource = (DomainResource)parser.Parse(scenarioContext.ReplaceVariables(json));

            var currentTag = scenarioContext.GetCurrentTag();
            if (currentTag is not null)
            {
                resource.Meta ??= new Meta();
                resource.Meta.Tag = new List<Coding>
                {
                    new Coding { Code = currentTag },
                };
            }

            var client = scenarioContext.GetHttpClient();
            if (data != null)
            {
                await scenarioContext.ApplyData(resource, data, patchService, client);
            }

            try
            {
                var created = await client.CreateAsync(resource);
                scenarioContext.SetResponse(created, destination);
            }
            catch (FhirOperationWithResponseException fex)
            {
                scenarioContext.SetResponse(fex, destination);
            }
        }

        [Given(@"Resource (.+) is updated with data as (\w+)")]
        [When(@"updating (.+) with data as (\w+)")]
        public async Task WhenResourceCreatedIsUpdatedWithDataAsUpdated(string reference, string destination, Table table)
        {
            reference = scenarioContext.ReplaceVariables(reference);
            var client = scenarioContext.GetHttpClient();
            if (!scenarioContext.TryGetValue<Resource>(reference, out var resourceToUpdate))
            {
                resourceToUpdate = await client.ReadAsync<Resource>(reference);
            }
            else
            {
                reference = $"{resourceToUpdate.TypeName}/{resourceToUpdate.Id}";
            }

            try
            {
                await scenarioContext.ApplyData(resourceToUpdate, table, patchService, client);
                var updated = await client.UpdateAsync(reference, resourceToUpdate);
                scenarioContext.SetResponse(updated, destination);
            }
            catch (FhirOperationWithResponseException fex)
            {
                scenarioContext.SetResponse(fex, destination);
            }

        }

        [When(@"Resource (.+) is read as (\w+)")]
        [When(@"reading (.+) as (\w+)")]
        [When(@"searching (.+) as (\w+)")]
        public async Task ReadResourceFromReference(string reference, string destination)
        {
            reference = scenarioContext.ReplaceVariables(reference);
            var client = scenarioContext.GetHttpClient();

            try
            {
                var readResource = await client.ReadAsync<Resource>(reference);
                scenarioContext.SetResponse(readResource, destination);
            }
            catch (FhirOperationWithResponseException fex)
            {
                scenarioContext.SetResponse(fex, destination);
            }

        }

        [Then(@"(\w+) and (\w+) are exactly the same")]
        public void ResourcesAreExactlyTheSame(string resourseA, string resourceB)
        {
            var res1 = scenarioContext.SafeGet<Resource>(resourseA);
            var res2 = scenarioContext.SafeGet<Resource>(resourceB);

            Assert.Equal(res1, res2, FhirResourceComparer.Instance);
        }

        [Then(@"Bundle (\w+) contains (.+)")]
        [Then(@"(\w+) is a Fhir Bundle which contains (.+)")]
        [Then(@"(\w+) is a Fhir Bundle containing (.+)")]
        public void BundleContainsOnly(string bundleId, string containedResourceIds)
        {
            var bundle = Assert.IsType<Bundle>(scenarioContext.SafeGet<Resource>(bundleId));
            var resources = containedResourceIds.Split(',').Select(containedResourceId => scenarioContext.SafeGet<Resource>(containedResourceId)).ToList();
            var elemInspectors = resources
                .Select((Resource resource) => new Action<Bundle.EntryComponent>((Bundle.EntryComponent e) => Assert.Equal(resource, e.Resource, FhirResourceComparer.Instance)))
                .ToArray();
            Assert.Collection(
                bundle.Entry,
                elemInspectors);
        }

        [Then(@"(\w+) is a Fhir (\w+)")]
        public void ResourceIsAGivenResourcetype(string resourceId, string fhirResourceType)
        {
            ResourceIsAGivenResourcetype(resourceId, fhirResourceType, null);
        }

        [Then(@"(\w+) is a Fhir (\w+) with data")]
        public void ResourceIsAGivenResourcetype(string resourceId, string fhirResourceType, Table data)
        {
            var resource = scenarioContext.SafeGet<Resource>(resourceId);
            Assert.Equal(fhirResourceType, resource.TypeName);

            if (data != null)
            {
                var lastResponse = scenarioContext.SafeGetResponse(resourceId);
                scenarioContext.AssertMatch(data, resource, lastResponse);
            }
        }

        [Given(@"operation (.+) is executed with data as (\w+)")]
        [When(@"executing operation (.+) with data as (\w+)")]
        public async Task ExecuteInstanceOperation(string operationUrl, string destination, Table data)
        {
            operationUrl = scenarioContext.ReplaceVariables(operationUrl);
            var client = scenarioContext.GetHttpClient();

            var response = await client.OperationAsync<Resource>(
                new Uri(operationUrl, UriKind.RelativeOrAbsolute),
                scenarioContext.ToParameters(data));
            scenarioContext.SetResponse(response, destination);
        }

        [Given(@"(.+) is retrieved as (\w+)")]
        [When(@"getting (.+) as (\w+)")]
        public async Task HttpGet(string path, string destination)
        {
            path = scenarioContext.ReplaceVariables(path);
            var response = await scenarioContext.GetHttpClient().GetAsync(path);
            scenarioContext.Set(response, destination);
        }

        [Then(@"(\w+) has data")]
        public void HttpHasData(string source, Table data)
        {
            var lastResponse = scenarioContext.SafeGetResponse(source);
            scenarioContext.AssertMatch(data, null, lastResponse);
        }

        [Then(@"(\w+) has statusCode (\w+)")]
        public void HttpStatus(string source, int statusCode)
        {
            var response = scenarioContext.Get<HttpResponseMessage>(source);
            Assert.Equal(statusCode, (int)response.StatusCode);
        }

        [Then(@"(\w+) is a Fhir CapabilityStatement with resolvable links")]
        public async Task ValidateCapabilityStatement(string source)
        {
            var capabilityStatement = scenarioContext.SafeGet<CapabilityStatement>(source);

            await ValidateResource(capabilityStatement, true);

            async Task ValidateResource(Resource resource, bool recurse = false)
            {
                var canonicals = resource.GetAllChildren<Canonical>();

                foreach (var canonical in canonicals)
                {
                    if (canonical.Value.StartsWith("http") && canonical.Value.StartsWith("http://localhost/"))
                    {
                        testOutput.WriteLine($"\nRequesting {canonical.Value} ... ");
                        try
                        {
                            var child = await scenarioContext.GetHttpClient().ReadAsync<Resource>(canonical.Value);
                            Assert.NotNull(child);
                            if (recurse)
                            {
                                await ValidateResource(child);
                                testOutput.WriteLine("Ok.");
                            }
                        }
                        catch (Exception ex)
                        {
                            testOutput.WriteLine(ex.Message);
                        }
                    }
                    else
                    {
                        testOutput.WriteLine($"Canonical is not a local URL '{canonical.Value}'");
                    }
                }
            }
        }
    }
}
