using AzInfoApi;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;


/*
 * Sample Code is provided for the purpose of illustration only and is not intended to be used in a production environment. 
 * THIS SAMPLE CODE AND ANY RELATED INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESSED OR IMPLIED, 
 * INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
 * We grant You a nonexclusive, royalty-free right to use and modify the Sample Code and to reproduce and distribute the object code form 
 * of the Sample Code, provided that. You agree: (i) to not use Our name, logo, or trademarks to market Your software product in which the 
 * Sample Code is embedded; (ii) to include a valid copyright notice on Your software product in which the Sample Code is embedded; 
 * and (iii) to indemnify, hold harmless, and defend Us and Our suppliers from and against any claims or lawsuits, including attorneys’ fees, 
 * that arise or result from the use or distribution of the Sample Code
 */

namespace AzImportExportLibrary
{
    public class AzSubscriptionExport
    {
        private string _authorizationEndpoint = null;
        private string _clientId = null;
        private string _clientSecret = null;
        private string _tenantId = null;

        ImportExportConfiguration _config = new ImportExportConfiguration();

        public AzSubscriptionExport(string clientId, string clientSecret, string tenantId, string authorizationEndpoint= "https://login.microsoftonline.com/", string managementApi = "https://management.azure.com/", string downloadPath= null)
        {
            _clientId = clientId;
            _clientSecret = clientSecret;
            _authorizationEndpoint = authorizationEndpoint;
            _tenantId = tenantId;

            _config.ManagementApiUrl = managementApi;            
            _config.RootFilePath = downloadPath!=null? downloadPath: Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetName().CodeBase.Replace(@"file:///", ""));
            _config.AccessToken = Helpers.GetAccessToken(_clientId, _clientSecret, _authorizationEndpoint+"/"+tenantId,managementApi);
            _config.ProvidersVersion = "2016-09-01";
        }

        public Dictionary<string, dynamic> ExportAllResourceGroups(string subscriptionId, bool saveToDisk = true, bool zipResult = false)
        {
            Dictionary<string, dynamic> result = new Dictionary<string, dynamic>();
            _config.SaveToDisk = saveToDisk;
            string providersUrl = $"/subscriptions/{subscriptionId}/providers";

            Dictionary<string, ProviderInformation> providersInformation = RetrieveProvidersInformation(result, _config.AccessToken, providersUrl);


            #region retrieve all resources
            dynamic resourceGroupsResult = Helpers.GetAzureResource(result, $"/subscriptions/{subscriptionId}/resourcegroups", _config, _config.ProvidersVersion).value;
            var resourceGroups = resourceGroupsResult.Values<dynamic>() as IEnumerable<dynamic>;
            
            Helpers.GetAzureResourceAutoFindVersion(result, providersUrl+ "/Microsoft.Authorization/roleassignments", providersInformation, _config, _config.ProvidersVersion);
            Helpers.GetAzureResourceAutoFindVersion(result, providersUrl+ "/Microsoft.Authorization/roledefinitions", providersInformation, _config, _config.ProvidersVersion);
            Helpers.GetAzureResourceAutoFindVersion(result, providersUrl+ "/Microsoft.Authorization/classicadministrators", providersInformation, _config, _config.ProvidersVersion);
            Helpers.GetAzureResourceAutoFindVersion(result, providersUrl+ "/Microsoft.Authorization/permissions", providersInformation, _config, _config.ProvidersVersion);
            Helpers.GetAzureResourceAutoFindVersion(result, providersUrl+ "/Microsoft.Authorization/locks", providersInformation, _config, _config.ProvidersVersion);
            Helpers.GetAzureResourceAutoFindVersion(result, providersUrl+ "/Microsoft.Authorization/policyassignments", providersInformation, _config, _config.ProvidersVersion);
            Helpers.GetAzureResourceAutoFindVersion(result, providersUrl + "/Microsoft.Authorization/policydefinitions", providersInformation, _config, _config.ProvidersVersion);

            Console.WriteLine(resourceGroups.Count() + " resources groups");
            AzResourceGroupExport exporter = new AzResourceGroupExport(_config, providersInformation);
            resourceGroups.AsParallel().ForAll(rg =>
            {
                exporter.ExtractResourceGroup(rg, subscriptionId, result);
            });

            #endregion retrieve all resources

            exporter.EnsureAllJobsTerminated();


            if (saveToDisk && zipResult)
            {
                Helpers.ZipResult(subscriptionId, Path.Combine(_config.RootFilePath, "subscriptions"));
            }

            return result;
        }

        private Dictionary<string, ProviderInformation> RetrieveProvidersInformation(Dictionary<string, dynamic> result, string accessToken, string providersUrl)
        {
            dynamic providersResult = Helpers.GetAzureResource(result, providersUrl, _config,_config.ProvidersVersion);
            var providers = providersResult.value.Values<dynamic>() as IEnumerable<dynamic>;

            Console.WriteLine(providers.Count() + " resource providers");

            Dictionary<string, ProviderInformation> resourcesInformation = new Dictionary<string, ProviderInformation>();
            providers.AsParallel().ForAll(provider =>
            {
                string providerId = provider.id;
                dynamic providersResourceTypes = provider.resourceTypes;
                var resourceTypes = providersResourceTypes.Values<dynamic>() as IEnumerable<dynamic>; ;
                foreach (var resourceType in resourceTypes)
                {
                    string resourceTypeKey = ($"{providerId}/{resourceType.resourceType}").ToLower();
                    lock (resourcesInformation)
                    {
                        resourcesInformation.Add(resourceTypeKey, new ProviderInformation()
                        {
                            ApiVersion = (resourceType.apiVersions.Values<dynamic>() as IEnumerable<dynamic>).First().Value,
                            Name = resourceTypeKey,
                            Namespace = provider.@namespace.ToString()
                        });
                    }
                }
                try
                {
                    var providerDetails = Helpers.GetAzureResource(result, "/providers/Microsoft.Authorization/providerOperations/" + provider.@namespace.ToString(), _config, _config.ProvidersVersion+"&$expand=resourceTypes");
                    var resourceTypesDetails = providerDetails.resourceTypes.Values<dynamic>() as IEnumerable<dynamic>;

                    foreach (var resourceTypeDetail in resourceTypesDetails)
                    {
                        var operations = resourceTypeDetail.operations.Values<dynamic>() as IEnumerable<dynamic>;
                        foreach (var operation in operations)
                        {
                            var operationDetails = (operation.name.Value as string).Split('/');
                            if (operationDetails.Length > 3 && operationDetails.Last() == "read" && operationDetails[2]!="providers")
                            {
                                StringBuilder operationName = new StringBuilder();
                                for (int i = 2; i < operationDetails.Length - 1; i++)
                                {
                                    operationName.Append("/").Append(operationDetails[i]);
                                }
                                var key = providersUrl + "/" + operationDetails[0].ToLower() + "/" + operationDetails[1].ToLower();
                                if (resourcesInformation.ContainsKey(key))
                                {
                                    resourcesInformation[key].ReadOperations.Add(operationName.ToString());
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    //Trace.TraceError(ex.Message);
                    //Console.WriteLine("WARNING: Cannot retrieve operations details for: " + provider.@namespace.ToString());
                }
            });
            Console.WriteLine(resourcesInformation.Count() + " resource types");
            return resourcesInformation;
        }

        private Dictionary<string, ProviderInformation> RetrieveProvidersInformationFromApi(Dictionary<string, dynamic> result, string accessToken, string providersUrl)
        {
            var apiClient = new AzInfoApiClient();
            var apiVersions = apiClient.ApiVersionsGet();

            
            //var operations = info.value.Values<dynamic>() as IEnumerable<dynamic>;

            //TODO : use this result to have a full view of available operations per resource type

            return null;
        }

    }
}
