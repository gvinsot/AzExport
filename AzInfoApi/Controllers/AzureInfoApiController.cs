using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using AzInfoApi.Models;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http;
using System.IO;
using System.IO.Compression;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using AzExport;
using Newtonsoft.Json.Linq;
using AzInfoApi.Helpers;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace AzInfoApi.Controllers
{
    [Route("api")]
    [Produces("application/json")]
    public class AzureInfoApiController : Controller
    {
        private IMemoryCache _cache;
        private AppSettings _settings;
        TraceSource _tracer = new TraceSource("TraceAzureInfoApiController");

        public AzureInfoApiController(IMemoryCache memoryCache, IOptions<AppSettings> appSettings)
        {
            _cache = memoryCache;
            _settings = appSettings.Value;
        }


        // GET api/values
        [Route("operations")]
        [HttpGet]
        public IEnumerable<ApiInfo> GetAllOperations([FromQuery] string datacenter, [FromQuery] string apiVersion, [FromQuery] string provider, [FromQuery] string resourceType)
        {
            List<ApiInfo> items = null;

            bool cacheAvailable = _cache.TryGetValue("ApiInfoList", out items);

            if (!cacheAvailable)
            {
                if (!System.IO.File.Exists("getOperations.json"))
                    UpdateOperations("43211234");

                // Key not in cache, so get data.
                using (var fs = System.IO.File.OpenText("getOperations.json"))
                {
                    items = Newtonsoft.Json.JsonSerializer.Create().Deserialize(fs, typeof(List<ApiInfo>)) as List<ApiInfo>;
                }
                items = items.Where(el => el != null).ToList();
                // Set cache options.
                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    // Keep in cache for this time, reset time if accessed.
                    .SetSlidingExpiration(TimeSpan.FromDays(14));
                // Save data in cache.
                _cache.Set("ApiInfoList", items, cacheEntryOptions);
            }

            IEnumerable<ApiInfo> result = items;

            if (datacenter != null)
            {
                result = result.Where(el => el.DataCenter.ToLower() == datacenter.ToLower());
            }
            if (apiVersion != null)
            {
                result = result.Where(el => el.ApiVersion.ToLower() == apiVersion.ToLower());
            }
            if (provider != null)
            {
                result = result.Where(el => el.Provider.ToLower() == provider.ToLower());
            }
            if (resourceType != null)
            {
                result = result.Where(el => el.ResourceType.ToLower() == resourceType.ToLower());
            }

            return result;
        }

        [Route("filterableoperations")]
        [HttpGet]
        public IEnumerable<ApiInfo> GetOperationsFilterable([FromQuery] string datacenter, [FromQuery] string apiVersion, [FromQuery] string provider, [FromQuery] string resourceType)
        {
            List<ApiInfo> operations = null;
            string cacheKey = "filterableoperations-" + datacenter + "-" + apiVersion + "-" + provider + "-" + resourceType;
            bool cacheAvailable = _cache.TryGetValue(cacheKey, out operations);

            if (cacheAvailable)
                return operations;

            var all = GetAllOperations(null, null, null, null);
            operations = all.Distinct(LambdaEqualityComparer.Create<ApiInfo, string>(a => a.Operation)).Select(el => new ApiInfo() { ApiVersion = el.ApiVersion, DataCenter = el.DataCenter, Operation = el.Operation, Provider = el.Provider, Verb = el.Verb, ResourceType = el.ResourceType }).ToList();
            foreach (var operation in operations)
            {
                var operationAllInstances = all.Where(el => el.Operation == operation.Operation).ToList();
                operation.DataCenter = operationAllInstances.Select(el => el.DataCenter).Aggregate((a, b) => a + ";" + b);
                operation.ApiVersion = operationAllInstances.Select(el => el.ApiVersion).Aggregate((a, b) => a + ";" + b);
            }
            operations = operations.OrderBy(el => el.Operation).ToList();

            _cache.Set(cacheKey, operations, new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromDays(1)));

            return operations;
        }

        [HttpGet]
        [Route("versions")]
        public List<ApiInfo> GetDistinctVersion([FromQuery] string apiVersion, [FromQuery] string dataCenter, [FromQuery] string provider, [FromQuery] string resourceType)
        {
            List<ApiInfo> result = null;
            string cacheKey = "versions-" + dataCenter + "-" + apiVersion + "-" + provider + "-" + resourceType;
            bool cacheAvailable = _cache.TryGetValue(cacheKey, out result);
            if (cacheAvailable)
                return result;

            var all = GetAllOperations(dataCenter, apiVersion, provider, resourceType);
            result = all.Distinct(LambdaEqualityComparer.Create<ApiInfo, string>(a => a.ApiVersion.ToLower())).OrderByDescending(el=>el.ApiVersion).ToList();

            _cache.Set(cacheKey, result, new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromDays(1)));
            return result;
        }

        [HttpGet]
        [Route("datacenters")]
        public List<ApiInfo> GetDistinctDataCenters([FromQuery] string apiVersion, [FromQuery] string provider, [FromQuery] string resourceType)
        {
            List<ApiInfo> result = null;
            string cacheKey = "datacenters-" + apiVersion + "-" + provider + "-" + resourceType;
            bool cacheAvailable = _cache.TryGetValue(cacheKey, out result);
            if (cacheAvailable)
                return result;

            var all = GetAllOperations(null, apiVersion, provider, resourceType);
            result = all.Distinct(LambdaEqualityComparer.Create<ApiInfo, string>(a => a.DataCenter.ToLower())).OrderBy(el => el.DataCenter).ToList();

            _cache.Set(cacheKey, result, new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromDays(1)));
            return result;
        }

        [HttpGet]
        [Route("providers")]
        public List<ApiInfo> GetDistinctProviders([FromQuery] string datacenter, [FromQuery] string apiVersion)
        {
            List<ApiInfo> result = null;
            string cacheKey = "providers-" + datacenter + "-" + apiVersion;
            bool cacheAvailable = _cache.TryGetValue(cacheKey, out result);
            if (cacheAvailable)
                return result;

            var all = GetAllOperations(datacenter, apiVersion, null, null);
            result = all.Distinct(LambdaEqualityComparer.Create<ApiInfo, string>(a => a.Provider.ToLower())).OrderBy(el => el.Provider).ToList();

            _cache.Set(cacheKey, result, new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromDays(1)));
            return result;
        }

        [HttpGet]
        [Route("resourcestypes")]
        public List<ApiInfo> GetDistinctResource([FromQuery] string datacenter, [FromQuery] string provider, [FromQuery] string apiVersion)
        {
            List<ApiInfo> result = null;
            string cacheKey = "resourcestypes-" + datacenter + "-" + apiVersion + "-" + provider;
            bool cacheAvailable = _cache.TryGetValue(cacheKey, out result);
            if (cacheAvailable)
                return result;

            var all = GetAllOperations(datacenter,apiVersion, provider, null);
            result = all.Distinct(LambdaEqualityComparer.Create<ApiInfo, string>(a => a.Provider.ToLower() + "/" + a.ResourceType.ToLower())).OrderBy(el => el.Provider).ToList();

            _cache.Set(cacheKey, result, new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromDays(1)));
            return result;
        }

        [Route("update")]
        [HttpGet]
        public string UpdateOperations([FromQuery] string key)
        {
            if (key != _settings.updateKey)
                return "Wrong update key";

            string ExtractPath = "extracted";

            var swaggerOperationsDownloadTask = RetrieveAllOperationFromSwaggerDefinitions(ExtractPath);
            var providersInfo = GetProvidersInformation(_settings);
            List<ApiInfo> allOperations = swaggerOperationsDownloadTask.Result;
            List<ApiInfo> results = new List<ApiInfo>(1000);
            var providers = providersInfo.value.Values<dynamic>() as IEnumerable<dynamic>;

            providers.AsParallel().ForAll(provider =>
            {
                var splitted = provider.id.ToString().Split(new char[] { '/' }) as IEnumerable<string>;

                string providerName = splitted.Last();
                dynamic providersResourceTypes = provider.resourceTypes;
                var resourceTypes = providersResourceTypes.Values<dynamic>() as IEnumerable<dynamic>;
                foreach (var resourceTypeInfo in resourceTypes)
                {
                    var resourceType = resourceTypeInfo.resourceType.ToString();
                    var apiVersions = resourceTypeInfo.apiVersions.Values<dynamic>() as IEnumerable<dynamic>;

                    var resourceOperations = allOperations.Where(el => el.Provider.ToLower() == providerName.ToLower() && el.ResourceType.ToLower() == resourceType.ToLower()).ToList();


                    foreach (var apiVersion in apiVersions)
                    {
                        string apiVersionString = apiVersion.ToString().ToLower();

                        var operations = resourceOperations.Where(res => res.ApiVersion.ToLower() == apiVersionString).ToList();
                        var dataCenters = resourceTypeInfo.locations.Values<dynamic>() as IEnumerable<dynamic>;

                        // sortedResult.Remove(el);
                        foreach (var dataCenter in dataCenters)
                        {
                            string datacenterName = dataCenter.ToString();
                            lock (results)
                            {
                                AddNewResults(results, providerName, resourceType, apiVersionString, operations, datacenterName);
                            }
                        }

                    }
                }
            });
            //results = results.Distinct(LambdaEqualityComparer.Create<ApiInfo, string>(a => a.Provider + "/" + a.ResourceType + ":" + a.Operation)).ToList();

            try
            {
                System.IO.File.Delete("getOperations.json");
                //save result
                using (var fs = System.IO.File.CreateText("getOperations.json"))
                {
                    Newtonsoft.Json.JsonSerializer.Create().Serialize(fs, results);
                }
            }
            catch(Exception ex)
            {
                
                _tracer.TraceEvent(TraceEventType.Error,1,ex.ToString());
            }

            if (Directory.Exists("D:\\Home"))
            {
                try
                {
                    System.IO.File.Delete("D:\\Home\\site\\wwwroot\\getOperations.json");
                    //save result
                    using (var fs = System.IO.File.CreateText("D:\\Home\\site\\wwwroot\\getOperations.json"))
                    {
                        Newtonsoft.Json.JsonSerializer.Create().Serialize(fs, results);
                    }
                }
                catch (Exception ex)
                {

                    _tracer.TraceEvent(TraceEventType.Error, 2, ex.ToString());
                }
            }
            _cache.Remove("ApiInfoList");

            try
            {
                Directory.Delete(ExtractPath, true);
            }
            catch { }

            return "Update succeeded";
        }

        private static void AddNewResults(List<ApiInfo> results, string providerName, dynamic resourceType, string apiVersionString, List<ApiInfo> operations, string datacenterName)
        {
            if (operations.Count > 0)
            {

                foreach (var el in operations)
                {

                    results.Add(new ApiInfo()
                    {
                        ApiVersion = apiVersionString,
                        DataCenter = datacenterName,
                        Operation = el.Operation,
                        Provider = providerName,
                        ResourceType = resourceType,
                        Verb = el.Verb
                    });
                }
            }
            else
            {
                results.Add(new ApiInfo()
                {
                    ApiVersion = apiVersionString,
                    DataCenter = datacenterName,
                    Operation = $"/subscriptions/providers/{providerName}/{resourceType}",
                    Provider = providerName,
                    ResourceType = resourceType,
                    Verb = "get"
                });
            }
        }

        private static Task<List<ApiInfo>> RetrieveAllOperationFromSwaggerDefinitions(string ExtractPath)
        {
            var task = Task.Run(() =>
            {
                var file = DownloadSwaggerDefinitions().Result;

                if (Directory.Exists(ExtractPath))
                    Directory.Delete(ExtractPath, true);
                //unzip
                ZipFile.ExtractToDirectory(file, ExtractPath);

                //list all json files
                var files = Directory.GetFiles(ExtractPath, "*.json", SearchOption.AllDirectories).Where(el => el.ToLower().Contains("\\swagger\\")).ToList();

                return ExtractAllOperations(files);
            });
            return task;
        }

        private static async Task<string> DownloadSwaggerDefinitions()
        {
            HttpClient client = new HttpClient();
            using (var stream = await client.GetStreamAsync("https://github.com/Azure/azure-rest-api-specs/archive/master.zip"))
            {
                using (var sw = System.IO.File.OpenWrite("master.zip"))
                {
                    await stream.CopyToAsync(sw);
                }
            }
            return "master.zip";
        }

        private static List<ApiInfo> ExtractAllOperations(List<string> files)
        {
            var result = new List<ApiInfo>(1000);
            files.AsParallel().ForAll(file =>
            {
                var splitted = file.Split(new char[] { '\\' });
                var apiVersion = splitted[splitted.Length - 3].ToLower();
                var text = System.IO.File.ReadAllText(file);
                var operations = JObject.Parse(text) as dynamic;

                IList<string> keys = (operations["paths"] as JObject).Properties().Select(p => p.Name).ToList();
                foreach (var key in keys)
                {
                    var splittedKey = key.Split(new char[] { '/' });
                    var providerIndex = splittedKey.ToList().IndexOf("providers") + 1;
                    string provider = "";
                    string resourceType = "";
                    if (providerIndex != 0 && splittedKey.Length > providerIndex)
                        provider = splittedKey[providerIndex];
                    if (providerIndex != 0 && splittedKey.Length > providerIndex + 1)
                        resourceType = splittedKey[providerIndex + 1];

                    IList<string> verbs = (operations["paths"][key] as JObject).Properties().Select(p => p.Name).ToList();
                    var verbsString = verbs.Aggregate((a, b) => a + "," + b);
                    lock (result)
                    {
                        result.Add(new ApiInfo()
                        {
                            ApiVersion = apiVersion,
                            Operation = key,
                            OperationDetails = operations["paths"][key],
                            Verb = verbsString,
                            Provider = provider,
                            ResourceType = resourceType,
                        });
                    }
                }
            });
            return result;
        }

        private static dynamic GetProvidersInformation(AppSettings appSettings)
        {
            var token = AzExport.Helpers.GetAccessToken(appSettings.clientId, appSettings.clientSecret, appSettings.authorizationEndpoint);
            //detect datacenters
            Uri uri = new Uri($"https://management.azure.com/subscriptions/{appSettings.subscriptionId}/providers?api-version=2016-09-01");

            // Create the request
            var httpWebRequest = (HttpWebRequest)WebRequest.Create(uri);
            httpWebRequest.Method = "get";
            httpWebRequest.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
            httpWebRequest.Headers[HttpRequestHeader.UserAgent] = "AzurePowershell/v3.6.0.0 PSVersion/v5.1.14393.693";
            httpWebRequest.Headers[HttpRequestHeader.Host] = "management.azure.com";

            var response = httpWebRequest.GetResponseAsync().Result;
            string providersInfo;
            using (var responseStream = new StreamReader(response.GetResponseStream()))
            {
                providersInfo = responseStream.ReadToEnd();
            }
            return JObject.Parse(providersInfo);
        }
    }
}
