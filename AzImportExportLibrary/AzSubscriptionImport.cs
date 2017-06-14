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
    public class AzSubscriptionImport
    {
        private string _authorizationEndpoint = null;
        private string _clientId = null;
        private string _clientSecret = null;
        private string _tenantId = null;

        ImportExportConfiguration _config = new ImportExportConfiguration();

        public AzSubscriptionImport(string clientId, string clientSecret, string tenantId, string authorizationEndpoint= "https://login.microsoftonline.com/", string managementApi = "https://management.azure.com/", string downloadPath= null)
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

        public void ImportAllResourceGroups(string sourceSubscriptionId, string destinationSubscriptionId, bool unzipSource = false)
        {
            dynamic resourceGroupsResult = Helpers.GetAzureResourceFromDisk($"/subscriptions/{sourceSubscriptionId}/resourcegroups", _config, _config.ProvidersVersion).value;
            var resourceGroups = resourceGroupsResult.Values<dynamic>() as IEnumerable<dynamic>;

            int countImports = Directory.EnumerateDirectories($"{_config.RootFilePath}\\subscriptions\\{sourceSubscriptionId}\\resourceGroups").Count();
            Console.WriteLine("SUBSCRIPTION CONTAINS " + resourceGroups.Count() + " RESOURCE GROUPS, "+ countImports+" WILL BE IMPORTED");


            AzResourceGroupImport importer = new AzResourceGroupImport(_config);
            resourceGroups.AsParallel().ForAll(rg =>
            {
                importer.ImportResourceGroup(rg, destinationSubscriptionId, rg.name.Value);
            });
        }

        public void ImportResourceGroupsAdditionalData(string sourceSubscriptionId, string destinationSubscriptionId, bool unzipSource = false)
        {
            dynamic resourceGroupsResult = Helpers.GetAzureResourceFromDisk($"/subscriptions/{sourceSubscriptionId}/resourcegroups", _config, _config.ProvidersVersion).value;
            var resourceGroups = resourceGroupsResult.Values<dynamic>() as IEnumerable<dynamic>;

            Console.WriteLine(resourceGroups.Count() + " resources groups");
            AzResourceGroupImport importer = new AzResourceGroupImport(_config);
            resourceGroups.AsParallel().ForAll(rg =>
            {
                importer.ImportResourceGroupAdditionalData(rg, destinationSubscriptionId, rg.name.Value);
            });
        }

        
    }
}
