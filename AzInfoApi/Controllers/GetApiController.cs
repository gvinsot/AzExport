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
using AzInfoApi.helpers;

namespace AzInfoApi.Controllers
{
    [Route("api")]
    [Produces("application/json")]
    public class GetApiController : Controller
    {
        private IMemoryCache _cache;
        public GetApiController(IMemoryCache memoryCache)
        {
            _cache = memoryCache;
        }


        // GET api/values
        [Route("all")]
        [HttpGet]
        public IEnumerable<ApiInfo> Get([FromQuery] string datacenter, [FromQuery] string apiVersion, [FromQuery] string provider)
        {
            List<ApiInfo> items = null;

            bool cacheAvailable = _cache.TryGetValue("ApiInfoList", out items);

            if (!cacheAvailable)
            {
                if (!System.IO.File.Exists("getOperations.json"))
                    Post("43211234");

                // Key not in cache, so get data.
                using (var fs = System.IO.File.OpenText("getOperations.json"))
                {
                    items = Newtonsoft.Json.JsonSerializer.Create().Deserialize(fs,typeof(List<ApiInfo>)) as List<ApiInfo>;
                }
                // Set cache options.
                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    // Keep in cache for this time, reset time if accessed.
                    .SetSlidingExpiration(TimeSpan.FromDays(14));
                // Save data in cache.
                _cache.Set("ApiInfoList", items, cacheEntryOptions);
            }

            IEnumerable<ApiInfo> result = items;

            if(datacenter!=null)
            {
                 result = result.Where(el => el.DataCenter == datacenter);
            }
            if (apiVersion != null)
            {
                result = result.Where(el => el.ApiVersion == apiVersion);
            }
            if (provider != null)
            {
                result = result.Where(el => el.Provider == provider);
            }

            return result;
        }

        [HttpGet]
        [Route("versions")]
        public List<string> GetDistinctVersion()
        {
            var all = Get(null, null, null);

            var result = all.Distinct(LambdaEqualityComparer.Create<ApiInfo, string>(a => a.ApiVersion.ToLower())).Select(el => el.ApiVersion).ToList();

            return result;
        }

        [HttpGet]
        [Route("datacenters")]
        public List<string> GetDistinctDataCenters()
        {
            var all = Get(null, null, null);

            var result = all.Distinct(LambdaEqualityComparer.Create<ApiInfo, string>(a => a.DataCenter)).Select(el => el.DataCenter).ToList();

            return result;
        }

        [HttpGet]
        [Route("providers")]
        public List<string> GetDistinctProviders()
        {
            var all = Get(null, null, null);

            var result = all.Distinct(LambdaEqualityComparer.Create<ApiInfo, string>(a => a.Provider.ToLower())).Select(el => el.Provider).ToList();

            return result;
        }

        [HttpGet]
        [Route("resources")]
        public List<string> GetDistinctResource([FromQuery] string provider)
        {
            var all = Get(null, null, provider);

            var result = all.Distinct(LambdaEqualityComparer.Create<ApiInfo, string>(a => a.ResourceType.ToLower())).Select(el => el.Provider+"/"+el.ResourceType).ToList();

            return result;
        }

        [Route("update")]
        [HttpPost]
        public async void Post([FromQuery] string key)
        {
            if (key != "43211234")
                return;

            string ExtractPath = "extracted";

            var file = await DownloadSwaggerDefinitions();

            if (Directory.Exists(ExtractPath))
                Directory.Delete(ExtractPath, true);
            //unzip
            ZipFile.ExtractToDirectory(file, ExtractPath);
            

            //list all json files
            var files = Directory.GetFiles(ExtractPath, "*.json", SearchOption.AllDirectories);

            //extract all get uris
            var result = ExtractAllGetProviders(files);

            var sortedResult = result.OrderBy(el => el.Operation).ToList();
            List<ApiInfo> elementsToAdd = new List<ApiInfo>();
            var providersInfo = GetProvidersInformation();
           
            var providers = providersInfo.value.Values<dynamic>() as IEnumerable<dynamic>;

            providers.AsParallel().ForAll(provider =>
            {
                var splitted = provider.id.ToString().Split(new char[] { '/' }) as IEnumerable<string>;
                string providerName = splitted.Last().ToLower();
                dynamic providersResourceTypes = provider.resourceTypes;
                var resourceTypes = providersResourceTypes.Values<dynamic>() as IEnumerable<dynamic>;
                foreach (var resourceTypeInfo in resourceTypes)
                {
                    var resourceType = resourceTypeInfo.resourceType.ToString().ToLower();
                    var apiVersions = resourceTypeInfo.apiVersions.Values<dynamic>() as IEnumerable<dynamic>;
                    foreach (var apiVersion in apiVersions)
                    {
                        lock (elementsToAdd)
                        {
                            var toUpdate = sortedResult.Where(el => el.Provider.ToLower() == providerName&& el.ResourceType.ToLower() == resourceType && el.ApiVersion.ToLower() == apiVersion.ToString().ToLower()).ToList();
                            var dataCenters = resourceTypeInfo.locations.Values<dynamic>() as IEnumerable<dynamic>;


                            if (toUpdate.Count == 0)
                                toUpdate.Add(new ApiInfo() { ApiVersion = apiVersion.ToString(), Provider = providerName, ResourceType = resourceType, Verb = "Other", Operation = "¤¤¤¤" });

                            foreach (var el in toUpdate)
                            {
                               // sortedResult.Remove(el);
                                foreach(var dataCenter in dataCenters)
                                {
                                    elementsToAdd.Add(new ApiInfo()
                                    {
                                        ApiVersion = el.ApiVersion,
                                        DataCenter = dataCenter.ToString(),
                                        Operation=el.Operation,
                                        //OperationDetails=el.OperationDetails,
                                        Provider=el.Provider,
                                        ResourceType=el.ResourceType,
                                        Verb=el.Verb
                                    });
                                }
                            }
                        }
                    }
                }
            });

            sortedResult.AddRange(elementsToAdd);
            sortedResult=sortedResult.OrderBy(el => el.Operation).ToList();

                //save result
                using (var fs = System.IO.File.CreateText("getOperations.json"))
            {
                Newtonsoft.Json.JsonSerializer.Create().Serialize(fs, sortedResult);
            }
            Directory.Delete(ExtractPath, true);
        }

        private static async Task<string> DownloadSwaggerDefinitions()
        {
            //Chilkat.Http();


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

        private static List<ApiInfo> ExtractAllGetProviders(string[] files)
        {
            var result = new List<ApiInfo>(1000);
            files.AsParallel().ForAll(file =>
            {
                var text = System.IO.File.ReadAllText(file);

                var parts = file.Split(new char[] { '\\' });
                var apiVersion = parts[Math.Max(parts.Length - 3, 0)];

                if (!apiVersion.Contains("-"))
                    return;

                Regex regex = new Regex(@"""([^""])*""[\s]*:[\s]*{[\s]*""get"":[\s]*{");
                //"([^"])*"[\s]*:[\s]*{[\s]*"get":[\s]*{
                var matches = regex.Matches(text);

                lock (result)
                {
                    foreach (var match in matches)
                    {
                        var value = match.ToString().Split(new char[] { '\"' }, StringSplitOptions.RemoveEmptyEntries)[0];
                        if (value.Contains("$"))
                            continue;
                        var splitted = value.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                        var index = splitted.FindIndex(el => el.Contains('.'));
                        if (index == -1 || (index+1)>=splitted.Count)
                            continue;
                        var provider = splitted[index];
                        var resourceType = splitted[index+1];

                        var appliestoRg = value.Contains("{resourceGroupName}");

                        result.Add(new ApiInfo() { ApiVersion = apiVersion, Provider = provider, ResourceType=resourceType, Operation = value });
                    }
                }
            });
            return result;
        }

        private static dynamic GetProvidersInformation()
        {
            string tenantId = "1af78158-a0d2-4287-afe9-02272cded0b6";
            string authorizationEndpoint = $"https://login.microsoftonline.com/{tenantId}/";
            string clientId = "22973c0e-7792-4313-8d3b-3910a3c67498";
            string clientSecret = "AOyplubQuKPQWoIn4fvQmIlf+lOCjkWQALR2Z5srkOY=";
            string subscriptionId = "318e1b85-66c8-401b-bb24-2ede0d8778b0";
            var token = Helpers.GetAccessToken(clientId, clientSecret, authorizationEndpoint);
            //detect datacenters
            Uri uri = new Uri($"https://management.azure.com/subscriptions/{subscriptionId}/providers?api-version=2016-09-01");

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
