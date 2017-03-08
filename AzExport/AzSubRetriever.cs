using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
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

namespace AzExport
{
    public class AzSubRetriever
    {
        public string FileDownloadPath = null;
        public string AuthorizationEndpoint = null;
        public string ClientId = null;
        public string ClientSecret = null;
        public string providersVersion = "2016-09-01";

        List<JobModel> runningJobs = new List<JobModel>();

        public AzSubRetriever(string clientId, string clientSecret, string authorizationEndpoint)
        {
            ClientId = clientId;
            ClientSecret = clientSecret;
            AuthorizationEndpoint = authorizationEndpoint;
            FileDownloadPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetName().CodeBase.Replace(@"file:///", ""));
        }

        public Dictionary<string, dynamic> RetrieveAllResourcesViaRG(string subscriptionId, bool saveToDisk = true, bool zipResult = false)
        {
            Dictionary<string, dynamic> result = new Dictionary<string, dynamic>();
            string accessToken = Helpers.GetAccessToken(ClientId, ClientSecret, AuthorizationEndpoint);            
            string subscriptionResourceId = "/subscriptions/" + subscriptionId;
            
            #region retrieve resource providers

            string providersUrl = $"{subscriptionResourceId}/providers";
            dynamic providersResult = GetAzureResource(result, providersUrl, accessToken, providersVersion, saveToDisk);
            var providers = providersResult.value.Values<dynamic>() as IEnumerable<dynamic>;

            Console.WriteLine(providers.Count() + " resource providers");
           
            Dictionary<string, string> resourcesTypeApiVersion = new Dictionary<string, string>();
            foreach (var provider in providers)
            {
                string providerId = provider.id;
                dynamic providersResourceTypes = provider.resourceTypes;
                var resourceTypes = providersResourceTypes.Values<dynamic>() as IEnumerable<dynamic>; ;
                foreach (var resourceType in resourceTypes)
                {
                    string resourceTypeKey = ($"{providerId}/" + resourceType.resourceType).ToLower();
                    resourcesTypeApiVersion.Add(resourceTypeKey, (resourceType.apiVersions.Values<dynamic>() as IEnumerable<dynamic>).First().Value);
                }
            }
            Console.WriteLine(resourcesTypeApiVersion.Count() + " resource types");
            #endregion
            
            #region retrieve all resources
            dynamic resourceGroupsResult = GetAzureResource(result, $"{subscriptionResourceId}/resourcegroups", accessToken, providersVersion, saveToDisk).value;
            var resourceGroups = resourceGroupsResult.Values<dynamic>() as IEnumerable<dynamic>;

            Console.WriteLine(resourceGroups.Count() + " resources groups");

            resourceGroups.AsParallel().ForAll(rg =>
            {
                try
                {
                    dynamic resourcesResult = GetAzureResource(result, rg.id.Value + "/resources", accessToken, providersVersion, saveToDisk);
                    var resources = resourcesResult.value.Values<dynamic>() as IEnumerable<dynamic>;

                    System.Console.WriteLine(resources.Count() + " resources in " + rg.name);

                    resources.AsParallel().ForAll(res =>
                    {
                        string resourceId = res.id.ToString();
                        try
                        {
                            string apiVersion = null;

                            string resourceTypeKey = (providersUrl + "/" + res.type).ToLower();

                            if (resourcesTypeApiVersion.ContainsKey(resourceTypeKey))
                                apiVersion = resourcesTypeApiVersion[resourceTypeKey];

                            GetAzureResource(result, resourceId, accessToken, apiVersion, saveToDisk);

                            ExportSpecificConfigurations(saveToDisk, res, accessToken, result, apiVersion, res.type.ToString().ToLower());

                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }

                    });
                }
                catch(Exception ex)
                {
                    Console.WriteLine("Error retrieving " + rg.id.Value + "/resources : " + ex.Message);
                }
            });

            #endregion retrieve all resources

            #region ensure jobs are finished
            if (saveToDisk && runningJobs.Count>0)
            {
                runningJobs.AsParallel().ForAll(job =>
                {
                    try
                    {
                        switch(job.JobType)
                        {
                            case JobTypes.IoTDevicesExport:
                                var jobState = Helpers.GetRemoteJsonObject(job.JobRequestUrl, accessToken);
                                var startTime = DateTime.Now;                                                                
                                while(jobState.status!="completed" && (DateTime.Now- startTime)<TimeSpan.FromMinutes(2))
                                {
                                    Thread.Sleep(1000);
                                }
                                string resultDevices = (new WebClient()).DownloadString(job.JobResultOutput);
                                Helpers.SaveResultToFile(this.FileDownloadPath, job.ExportedResourceId, resultDevices,"result");
                                break;

                                //TODO : Add other asynchronous job to check
                            default:
                                break;
                        }

                    }
                    catch(Exception ex)
                    {
                        Console.Error.WriteLine("Error retrieving job output for " + job.ExportedResourceId + " : " + ex.Message);
                    }
                });
            }
            #endregion


            if (saveToDisk && zipResult)
            {
                Helpers.ZipResult(subscriptionId,Path.Combine(FileDownloadPath,"subscriptions"));
            }

            return result;
        }

        

        private void ExportSpecificConfigurations(bool saveToDisk, dynamic res, string accessToken, Dictionary<string, dynamic> result, string apiVersion, string switchValue)
        {
            string resId = res.id.ToString();

            switch (switchValue)
            {
                
                case "microsoft.web/sites": // Code specific for web apps                   
                    GetAzureResource(result, resId + "/config/appsettings/list", accessToken, apiVersion, saveToDisk, "POST");
                    GetAzureResource(result, resId + "/config/authsettings/list", accessToken, apiVersion, saveToDisk, "POST");                    
                    GetAzureResource(result, resId + "/config/connectionstrings/list", accessToken, apiVersion, saveToDisk, "POST");
                    GetAzureResource(result, resId + "/config/logs", accessToken, apiVersion, saveToDisk);
                    GetAzureResource(result, resId + "/config/metadata/list", accessToken, apiVersion, saveToDisk, "POST");
                    GetAzureResource(result, resId + "/config/publishingcredentials/list", accessToken, apiVersion, saveToDisk, "POST");
                    GetAzureResource(result, resId + "/config/slotConfigNames", accessToken, apiVersion, saveToDisk); 
                    GetAzureResource(result, resId + "/config/web", accessToken, apiVersion, saveToDisk);
                    GetAzureResource(result, resId + "/hostNameBindings", accessToken, apiVersion, saveToDisk);
                    GetAzureResource(result, resId + "/hybridconnection", accessToken, apiVersion, saveToDisk);
                    GetAzureResource(result, resId + "/premieraddons", accessToken, "2015-08-01", saveToDisk);
                    GetAzureResource(result, resId + "/slots", accessToken, apiVersion, saveToDisk);
                    GetAzureResource(result, resId + "/usages", accessToken, apiVersion, saveToDisk);
                    GetAzureResource(result, resId + "/sourcecontrols/web", accessToken, apiVersion, saveToDisk);
                    GetAzureResource(result, resId + "/virtualNetworkConnections", accessToken, apiVersion, saveToDisk);
                    break;
                case "microsoft.web/serverfarms":
                    GetAzureResource(result, resId + "/virtualNetworkConnections", accessToken, apiVersion, saveToDisk);
                    break;

                case "microsoft.devices/iothubs":

                    //TODO : industrialize by creating one container per IoTHub
                    string containerSasUri =ConfigurationManager.AppSettings["ExistingStorageAccountContainerWriteAccess"];
                    if (!String.IsNullOrEmpty(containerSasUri))
                    {
                        var job = GetAzureResource(result, resId + "/exportDevices", accessToken, apiVersion, saveToDisk, "POST", "ExportBlobContainerUri=" + Uri.EscapeDataString(containerSasUri));
                        runningJobs.Add(new JobModel()
                        {
                            JobType = JobTypes.IoTDevicesExport,
                            JobRequestUrl = "https://management.azure.com" + resId + "/jobs/" + job.jobId + "?api-version=" + apiVersion,
                            JobResultOutput = containerSasUri.Split('?').First() + "/devices.txt",
                            ExportedResourceId = resId + "/exportDevices"
                        });
                    }
                    break;
                case "microsoft.compute/virtualmachines":
                    GetAzureResource(result, resId + "/InstanceView", accessToken, apiVersion, saveToDisk);
                    break;
                case "microsoft.network/networksecuritygroups":
                    GetAzureResource(result, resId + "/securityRules", accessToken, apiVersion, saveToDisk);
                    break;
                case "microsoft.network/virtualnetworks":
                    GetAzureResource(result, resId + "/subnets", accessToken, apiVersion, saveToDisk);
                    GetAzureResource(result, resId + "/virtualNetworkPeerings", accessToken, apiVersion, saveToDisk);
                    break;
                case "microsoft.operationalinsights/workspaces":
                    //GetAzureResource(result, resId + "/linkedServices", accessToken, apiVersion, saveToDisk);
                    
                    //TODO : loop on workspaces to retrieve workspace configuration                    
                    break;
                case "microsoft.visualstudio/account":
                    //TODO : loop on accounts to retrieve additionnal configuration
                    break;
                case "microsoft.logic/workflows":
                    GetAzureResource(result, resId + "/runs", accessToken, apiVersion, saveToDisk);
                    GetAzureResource(result, resId + "/triggers", accessToken, apiVersion, saveToDisk);
                    GetAzureResource(result, resId + "/versions", accessToken, apiVersion, saveToDisk);
                    break;
                case "microsoft.streamanalytics/streamingjobs":
                    GetAzureResource(result, resId + "/functions", accessToken, apiVersion, saveToDisk);
                    GetAzureResource(result, resId + "/inputs", accessToken, apiVersion, saveToDisk);
                    GetAzureResource(result, resId + "/outputs", accessToken, apiVersion, saveToDisk);                    
                    break;
                case "microsoft.datafactory/datafactories":
                    GetAzureResource(result, resId + "/datasets", accessToken, apiVersion, saveToDisk);
                    GetAzureResource(result, resId + "/gateways", accessToken, apiVersion, saveToDisk);
                    GetAzureResource(result, resId + "/hubs", accessToken, apiVersion, saveToDisk);
                    GetAzureResource(result, resId + "/linkedservices", accessToken, apiVersion, saveToDisk);
                    GetAzureResource(result, resId + "/datapipelines", accessToken, apiVersion, saveToDisk);
                    break;
                case "microsoft.machineLearning/commitmentplans":
                    GetAzureResource(result, resId + "/commitmentAssociations", accessToken, apiVersion, saveToDisk);
                    GetAzureResource(result, resId + "/usageHistory", accessToken, apiVersion, saveToDisk);                    
                    break;
                case "microsoft.machinelearning/webservices":
                    GetAzureResource(result, resId + "/listKeys", accessToken, apiVersion, saveToDisk);
                    break;
                case "microsoft.recoveryservices/vaults":
                    GetAzureResource(result, resId + "/backupPolicies", accessToken, apiVersion, saveToDisk);
                   // GetAzureResource(result, resId + "/backupProtectionContainers", accessToken, apiVersion, saveToDisk);
                   //GetAzureResource(result, resId + "/backupJobsExport", accessToken, apiVersion, saveToDisk);
                    GetAzureResource(result, resId + "/backupEngines", accessToken, apiVersion, saveToDisk);
                    break;

                //TODO : Add here other specific resources you want to save

                default:
                    break;
            }
        }

        private dynamic GetAzureResource(Dictionary<string, dynamic> results, string resourceId, string token, string apiVersion = null, bool saveToDisk = true, string method = "GET", string postContent = "")
        {
            Uri uri = new Uri("https://management.azure.com" + resourceId + (apiVersion == null ? "" : ("?api-version=" + apiVersion)));
            try
            {                
                // Create the request
                var httpWebRequest = (HttpWebRequest)WebRequest.Create(uri);
                httpWebRequest.Method = method;
                httpWebRequest.Headers.Add(HttpRequestHeader.Authorization, "Bearer " + token);
                httpWebRequest.UserAgent = "Microsoft.Azure.Management.Compute.ComputeManagementClient/10.0.0.0 AzurePowershell/v1.0.0.0";
                httpWebRequest.Host = "management.azure.com";
                if (method == "POST")
                {
                    httpWebRequest.ContentType = "application/x-www-form-urlencoded";
                    httpWebRequest.ContentLength = postContent.Length;
                    using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                    {
                        streamWriter.Write(postContent);
                    }
                }
                // Get the response
                HttpWebResponse httpResponse = null;
                httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();

                string resultString = null;
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    resultString = streamReader.ReadToEnd();
                }

                // Prettify                
                resultString = Helpers.JsonPrettify(resultString);

                // Save to disk
                if (saveToDisk)
                {
                    Helpers.SaveResultToFile(this.FileDownloadPath, resourceId, resultString, apiVersion);
                }
                dynamic result = null;
                try
                {
                    result = JObject.Parse(resultString);

                    // Add result to result resources dictionary
                    lock (results)
                    {
                        results.Add(resourceId, result);
                    }
                }
                catch
                {
                    //ignore this error because sometime Azure returns bad json
                }

                return result;
            }
            catch(Exception ex)
            {
                throw new Exception(ex.Message + " : " + uri.ToString(),ex);
            }
        }

        
    }
}
