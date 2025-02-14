using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath;
using Ihis.FhirEngine.Client;
using Ihis.FhirEngine.Core.Constants;
using Ihis.FhirEngine.Core.Data;
using Ihis.FhirEngine.Data.Testing;
using TechTalk.SpecFlow;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Synapxe.FhirWebApi18.IntegrationTests.Steps
{
    internal static class SpecFlowScenarioContextExtensions
    {
        private static readonly Regex IdVariablePattern = new("({(?<name>\\w+)\\.Id})", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));
        private static readonly Regex GeneratedRandomVariablePattern = new("({#(?<name>\\w+)(\\((?<generator>.+)\\))?})", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

        private static string GetRequestedName(this ScenarioContext context, string key)
        {
            var fromTags = context.ScenarioInfo.Tags
                .Where(x => x.StartsWith(key + ":", StringComparison.Ordinal))
                .Select(x => x.Substring(key.Length + 1))
                .SingleOrDefault();

            return fromTags ?? context.Get<string>(key);
        }

        public static FhirHttpClient GetFhirClient(this ScenarioContext context)
        {
            var name = context.GetRequestedName(nameof(FhirHttpClient));

            var client = context.Get<FhirHttpClient>($"{nameof(FhirHttpClient)}:{name}");

            return client;
        }

        public static HttpClient GetHttpClient(this ScenarioContext context)
        {
            var name = context.GetRequestedName(nameof(HttpClient));

            var client = context.Get<HttpClient>($"{nameof(HttpClient)}:{name}");

            return client;
        }

        public static void SetFhirClient(this ScenarioContext context, FhirHttpClient client, string name, bool setAsCurrent = true)
        {
            context.Set(client, $"{nameof(FhirHttpClient)}:{name}");
            if (setAsCurrent)
            {
                context.Set(name, nameof(FhirHttpClient));
            }
        }

        public static void SetHttpClient(this ScenarioContext context, HttpClient client, string name, bool setAsCurrent = true)
        {
            context.Set(client, $"{nameof(HttpClient)}:{name}");
            if (setAsCurrent)
            {
                context.Set(name, nameof(HttpClient));
            }
        }

        public static T SafeGet<T>(this ScenarioContext context, string id)
            where T : Resource
        {
            if (context.TryGetValue(id, out var value))
            {
                if (value is T tvalue)
                {
                    return tvalue;
                }
                else if (value is FhirResponse<Resource> response)
                {
                    return (T)response.Resource;
                }
                else if (value is FhirOperationWithResponseException exception)
                {
                    throw exception;
                }
                else if (value is HttpResponseMessage responseMsg)
                {
                    var body = responseMsg.Content.ReadAsStringAsync().Result;
                    var parser = new FhirJsonParser(new ParserSettings { AllowUnrecognizedEnums = true });
                    try
                    {
                        return parser.Parse<T>(body);
                    }
                    catch (FormatException)
                    {
                        return null;
                    }
                }
            }

            return null;
        }

        public static Bundle.ResponseComponent SafeGetResponse(this ScenarioContext context, string id)
        {
            if (context.TryGetValue(id, out var responseMsgT) &&
                responseMsgT is HttpResponseMessage response)
            {
                return new Bundle.ResponseComponent
                {
                    Status = ((int?)response.StatusCode).ToString(),
                    Location = response.Headers.TryGetValues(KnownFhirHeaders.Location, out var locationHeader) ? locationHeader.FirstOrDefault() : null,
                    Etag = response.Headers.TryGetValues(KnownFhirHeaders.ETag, out var etagHeader) ? etagHeader.FirstOrDefault() : null,
                    LastModified = response.Headers.TryGetValues(KnownFhirHeaders.LastModified, out var lastModifiedHeader) && DateTimeOffset.TryParse(lastModifiedHeader.FirstOrDefault(), out var dt) ? new DateTimeOffset?(dt) : null,
                };
            }
            else if (context.TryGetValue<Bundle.ResponseComponent>(id + ".response", out var bundleResponse))
            {
                return bundleResponse;
            }
            else
            {
                return null;
            }
        }

        public static void SetResponse(this ScenarioContext scenarioContext, FhirOperationWithResponseException fex, string destination)
        {
            scenarioContext.Set(fex.Outcome, destination);
            scenarioContext.Set(fex.ToResponseComponent(), destination + ".response");
        }

        public static void SetResponse(this ScenarioContext scenarioContext, FhirResponse<Resource> fex, string destination)
        {
            scenarioContext.Set(fex.Resource, destination);
            scenarioContext.Set(fex.ToResponseComponent(), destination + ".response");
        }

        public static void SetResponse(this ScenarioContext scenarioContext, FhirResponse<DomainResource> fex, string destination)
        {
            scenarioContext.Set(fex.Resource, destination);
            scenarioContext.Set(fex.ToResponseComponent(), destination + ".response");
        }

        public static string GetCurrentTag(this ScenarioContext context)
        {
            context.TryGetValue<string>("currentTag", out var val);
            return val;
        }

        public static void SetCurrentTag(this ScenarioContext context, string value)
            => context.Set<string>(value, "currentTag");

        public static string ReplaceVariables(this ScenarioContext context, string value)
        {
            value = value.Replace("{newguid}", Guid.NewGuid().ToString("N"));
            value = value.Replace("{currentTag}", context.GetCurrentTag());

            var idMatches = IdVariablePattern.Matches(value);
            for (int i = idMatches.Count - 1; i >= 0; i--)
            {
                var match = idMatches[i];
                var name = match.Groups["name"].Value;
                var resource = context.SafeGet<Resource>(name);
                value = value.Remove(match.Index, match.Length).Insert(match.Index, resource.Id);
            }

            var genMatches = GeneratedRandomVariablePattern.Matches(value);
            for (int i = genMatches.Count - 1; i >= 0; i--)
            {
                var match = genMatches[i];
                var name = match.Groups["name"].Value;

                if (!context.TryGetValue(name, out string generated))
                {
                    var generator = match.Groups["generator"];
                    if (generator.Success)
                    {
                        generated = GenerateValue(generator.Value);
                    }
                    else
                    {
                        generated = Guid.NewGuid().ToString("N");
                    }

                    context.Set(generated, name);
                }

                value = value.Remove(match.Index, match.Length).Insert(match.Index, generated);
            }

            return value;
        }

        public static void AssertMatch(this ScenarioContext context, Table data, Resource resource, Bundle.ResponseComponent lastResponse)
        {
            foreach (var row in data.Rows)
            {
                var path = row["Path"];
                var value = context.ReplaceVariables(row["Value"]);

                if ("statusCode".Equals(path, StringComparison.OrdinalIgnoreCase))
                {
                    Assert.Equal(value, lastResponse.Status);
                }
                else if (path.StartsWith("headers.", StringComparison.OrdinalIgnoreCase))
                {
                    var headerName = path[8..].ToLowerInvariant();
                    var extractedValue = headerName switch
                    {
                        "etag" => lastResponse.Etag,
                        "location" => lastResponse.Location,
                        "lastModified" => lastResponse.LastModified?.ToString("O"),
                        _ => throw new InvalidOperationException($"Unknown header '{headerName}'"),
                    };

                    Assert.Equal(value, extractedValue);
                }
                else
                {
                    var type = row["FhirType"];
                    var val = Read(value, type);
                    var extracted = resource.Select(path).FirstOrDefault() as DataType;
                    Assert.Equal(val, extracted, FhirResourceComparer.Instance);
                }
            }
        }

        public static Parameters ToParameters(this ScenarioContext context, Table data)
        {
            var parameters = new Parameters();
            foreach (var row in data.Rows)
            {
                var name = row["Name"];
                var value = context.ReplaceVariables(row["Value"]);
                var type = row["FhirType"];
                parameters.Add(name, Read(value, type));
            }

            return parameters;
        }

        public static Task ApplyData<TResource>(this ScenarioContext context, TResource resource, Table data, IPatchService patchService, HttpClient client)
            where TResource : Resource
            => context.ApplyData(resource, data, patchService, client.DefaultRequestHeaders);

        public static Task ApplyData<TResource>(this ScenarioContext context, TResource resource, Table data, IPatchService patchService, FhirClient client)
            where TResource : Resource
            => context.ApplyData(resource, data, patchService, client.RequestHeaders);

        private static async Task ApplyData<TResource>(this ScenarioContext context, TResource resource, Table data, IPatchService patchService, HttpRequestHeaders requestHeaders)
            where TResource : Resource
        {
            var parameters = new Parameters();
            foreach (var row in data.Rows)
            {
                var path = row["Path"];
                var value = context.ReplaceVariables(row["Value"]);

                if (path.StartsWith("headers.", StringComparison.OrdinalIgnoreCase))
                {
                    requestHeaders.TryAddWithoutValidation(path[8..], value);
                }
                else
                {
                    var type = row["FhirType"];
                    parameters.AddReplacePatchParameter(path, Read(value, type));
                }
            }

            await patchService.ApplyAsync(null, resource, parameters, default);
        }

        private static DataType Read(string value, string type)
        {
            var parameters = new Parameters { { "value", new FhirString(value) } };

            return type switch
            {
                "Quantity" => parameters.TryGetValue("value", out Quantity quantity) ? quantity : null,
                "Identifier" => parameters.TryGetValue("value", out Identifier identifier) ? identifier : null,
                "Coding" => parameters.TryGetValue("value", out Coding coding) ? coding : null,
                "CodeableConcept" => parameters.TryGetValue("value", out CodeableConcept codeableConcept) ? codeableConcept : null,
                "Reference" => parameters.TryGetValue("value", out ResourceReference reference) ? reference : null,
                "boolean" => new FhirBoolean(bool.Parse(value)),
                "uri" => new FhirUri(value),
                "canonical" => new Canonical(value),
                "url" => new FhirUrl(value),
                "oid" => new Oid(value),
                "uuid" => new Uuid(value),
                "base64Binary" => new Base64Binary(Convert.FromBase64String(value)),
                "datetime" => new FhirDateTime(value),
                "date" => new Date(value),
                "time" => new Time(value),
                "instant" => new Instant(TryParseDateTimeExpression(value)),
                "decimal" => new FhirDecimal(decimal.Parse(value)),
                "int" or "integer" => new Integer(int.Parse(value)),
                "unsignedInt" => new UnsignedInt(int.Parse(value)),
                "positiveInt" => new PositiveInt(int.Parse(value)),
                "string" => new FhirString(value),
                "id" => new Id(value),
                "code" => new Code(value),
                "markdown" => new Markdown(value),
                "xhtml" => new XHtml(value),
                _ => throw new InvalidOperationException($"Unsupported Fhir type '{type}'"),
            };
        }

        private static DateTimeOffset? TryParseDateTimeExpression(string value)
        {
            var relativeDateTimeRegex = new Regex("([+-])(\\d\\.?\\d*)(d|min|s|h)", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));
            if (DateTimeOffset.TryParse(value, out var result))
            {
                return result;
            }
            else
            {
                var match = relativeDateTimeRegex.Match(value);
                if (match.Success)
                {
                    var sign = match.Groups[1].Value;
                    var relative = double.Parse(match.Groups[2].Value);
                    var unit = match.Groups[3].Value;
                    if (sign == "-")
                    {
                        relative = -relative;
                    }

                    return unit switch
                    {
                        "d" => DateTimeOffset.Now.AddDays(relative),
                        "min" => DateTimeOffset.Now.AddMinutes(relative),
                        "s" => DateTimeOffset.Now.AddSeconds(relative),
                        "h" => DateTimeOffset.Now.AddHours(relative),
                        _ => throw new InvalidOperationException($"Unknown unit '{unit}'"),
                    };
                }
                else
                {
                    return null;
                }
            }
        }

        private static string GenerateValue(string expression)
        {
            if (expression.StartsWith("datetime"))
            {
                var datetimeVal = TryParseDateTimeExpression(expression.Substring(8));
                return datetimeVal?.ToString("O");
            }
            else
            {
                throw new InvalidOperationException($"Unknown generator expression '{expression}'");
            }
        }
    }
}
