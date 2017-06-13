using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AzImportExportLibrary
{
    public class AzResourceGroupExport
    {
        private ImportExportConfiguration _config;

        private Dictionary<string, ProviderInformation> _providersInformation;
        private List<JobModel> _runningJobs = new List<JobModel>();

        public AzResourceGroupExport(ImportExportConfiguration config, Dictionary<string, ProviderInformation> providersInformation)
        {
            _config = config;
            _providersInformation = providersInformation;
        }

        public Dictionary<string, dynamic> ExportResourceGroup(string subscriptionId, string rgName, ImportExportConfiguration config)
        {
            Dictionary<string, dynamic> result = new Dictionary<string, dynamic>();
            dynamic resourceGroupResult = Helpers.GetAzureResource(result, $"/subscriptions/{subscriptionId}/resourcegroups", config, config.ProvidersVersion).value;
            ExtractResourceGroup(resourceGroupResult, subscriptionId, result);

            return result;
        }


        internal void ExtractResourceGroup(dynamic rg, string subscriptionId, Dictionary<string, dynamic>  result )
        {
            try
            {
                string providersUrl = $"/subscriptions/{subscriptionId}/providers";
                dynamic resourcesResult = Helpers.GetAzureResource(result, rg.id.Value + "/resources", _config, _config.ProvidersVersion);
                var resources = resourcesResult.value.Values<dynamic>() as IEnumerable<dynamic>;

                System.Console.WriteLine(resources.Count() + " resources in " + rg.name);

                // Export template
                Helpers.GetAzureResource(result, rg.id.Value + "/exportTemplate", _config, _config.ProvidersVersion, "POST", "{\"resources\":[\"*\"]}");
                //

                resources.AsParallel().ForAll(res =>
                {
                    string resourceId = res.id.ToString();
                    try
                    {
                        string apiVersion = _config.ProvidersVersion;
                        string resourceTypeKey = (providersUrl + "/" + res.type).ToLower();
                        ProviderInformation info = null;

                        if (_providersInformation.ContainsKey(resourceTypeKey))
                        {
                            info = _providersInformation[resourceTypeKey];
                            apiVersion = info.ApiVersion;
                        }

                        Helpers.GetAzureResource(result, resourceId, _config, apiVersion);

                        if (info != null)
                        {
                            foreach (var readOperation in info.ReadOperations)
                            {
                                try
                                {
                                    Helpers.GetAzureResource(result, resourceId + readOperation, _config, apiVersion);
                                }
                                catch (Exception ex)
                                {
                                    Trace.TraceWarning("Warning: Cannot export " + resourceId + readOperation + " : " + ex.Message);
                                    //maybe parameter was expected...
                                }
                            }
                        }

                        ExportSpecificConfigurations(_config.SaveToDisk, res, _config.AccessToken, result, apiVersion, res.type.ToString().ToLower());

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }

                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error retrieving " + rg.id.Value + "/resources : " + ex.Message);
            }
        }

        private void ExportSpecificConfigurations(bool saveToDisk, dynamic res, string accessToken, Dictionary<string, dynamic> result, string apiVersion, string switchValue)
        {
            string resId = res.id.ToString();

            switch (switchValue)
            {

                case "microsoft.web/sites": // Code specific for web apps                   
                    Helpers.GetAzureResource(result, resId + "/config/appsettings/list", _config, apiVersion, "POST");
                    Helpers.GetAzureResource(result, resId + "/config/authsettings/list", _config, apiVersion, "POST");
                    Helpers.GetAzureResource(result, resId + "/config/connectionstrings/list", _config, apiVersion, "POST");
                    Helpers.GetAzureResource(result, resId + "/config/logs", _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/config/metadata/list", _config, apiVersion, "POST");
                    Helpers.GetAzureResource(result, resId + "/config/publishingcredentials/list", _config, apiVersion, "POST");
                    Helpers.GetAzureResource(result, resId + "/config/slotConfigNames", _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/config/web", _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/hostNameBindings", _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/hybridconnection", _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/premieraddons", _config, "2015-08-01");
                    Helpers.GetAzureResource(result, resId + "/slots", _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/usages", _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/sourcecontrols/web", _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/virtualNetworkConnections", _config, apiVersion);
                    break;
                case "microsoft.web/serverfarms":
                    Helpers.GetAzureResource(result, resId + "/virtualNetworkConnections", _config, apiVersion);
                    break;

                case "microsoft.devices/iothubs":

                    //TODO : industrialize by creating one container per IoTHub
                    string containerSasUri = ConfigurationManager.AppSettings["ExistingStorageAccountContainerWriteAccess"];
                    if (!String.IsNullOrEmpty(containerSasUri))
                    {
                        var job = Helpers.GetAzureResource(result, resId + "/exportDevices", _config, apiVersion, "POST", "ExportBlobContainerUri=" + Uri.EscapeDataString(containerSasUri));
                        lock (_runningJobs)
                        {
                            _runningJobs.Add(new JobModel()
                            {
                                JobType = JobTypes.IoTDevicesExport,
                                JobRequestUrl = _config.ManagementApiUrl + resId + "/jobs/" + job.jobId + "?api-version=" + apiVersion,
                                JobResultOutput = containerSasUri.Split('?').First() + "/devices.txt",
                                ExportedResourceId = resId + "/exportDevices"
                            });
                        }
                    }
                    break;
                case "microsoft.compute/virtualmachines":
                    Helpers.GetAzureResource(result, resId + "/InstanceView", _config, apiVersion);
                    break;
                case "microsoft.network/networksecuritygroups":
                    Helpers.GetAzureResource(result, resId + "/securityRules", _config, apiVersion);
                    break;
                case "microsoft.network/virtualnetworks":
                    Helpers.GetAzureResource(result, resId + "/subnets", _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/virtualNetworkPeerings", _config, apiVersion);
                    break;
                case "microsoft.operationalinsights/workspaces":
                    //GetAzureResource(result, resId + "/linkedServices",  _config, apiVersion);

                    //TODO : loop on workspaces to retrieve workspace configuration                    
                    break;
                case "microsoft.visualstudio/account":
                    //TODO : loop on accounts to retrieve additionnal configuration
                    break;
                case "microsoft.logic/workflows":
                    Helpers.GetAzureResource(result, resId + "/runs", _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/triggers", _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/versions", _config, apiVersion);
                    break;
                case "microsoft.streamanalytics/streamingjobs":
                    Helpers.GetAzureResource(result, resId + "/functions", _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/inputs", _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/outputs", _config, apiVersion);
                    break;
                case "microsoft.datafactory/datafactories":
                    Helpers.GetAzureResource(result, resId + "/datasets", _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/gateways", _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/hubs", _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/linkedservices", _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/datapipelines", _config, apiVersion);
                    break;
                case "microsoft.machineLearning/commitmentplans":
                    Helpers.GetAzureResource(result, resId + "/commitmentAssociations", _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/usageHistory", _config, apiVersion);
                    break;
                case "microsoft.machinelearning/webservices":
                    Helpers.GetAzureResource(result, resId + "/listKeys", _config, apiVersion);
                    break;
                case "microsoft.recoveryservices/vaults":
                    Helpers.GetAzureResource(result, resId + "/backupPolicies", _config, apiVersion);
                    // GetAzureResource(result, resId + "/backupProtectionContainers",  _config, apiVersion);
                    //GetAzureResource(result, resId + "/backupJobsExport",  _config, apiVersion);
                    Helpers.GetAzureResource(result, resId + "/backupEngines", _config, apiVersion);
                    break;

                //TODO : Add here other specific resources you want to save

                default:
                    break;
            }
        }

        public void EnsureAllJobsTerminated()
        {
            if (_config.SaveToDisk && _runningJobs.Count > 0)
            {
                _runningJobs.AsParallel().ForAll(job =>
                {
                    try
                    {
                        switch (job.JobType)
                        {
                            case JobTypes.IoTDevicesExport:
                                var jobState = Helpers.GetRemoteJsonObject(job.JobRequestUrl, _config.ProvidersVersion);
                                var startTime = DateTime.Now;
                                while (jobState.status != "completed" && (DateTime.Now - startTime) < TimeSpan.FromMinutes(2))
                                {
                                    Thread.Sleep(1000);
                                }
                                string resultDevices = (new WebClient()).DownloadString(job.JobResultOutput);
                                Helpers.SaveResultToFile(_config.RootFilePath, job.ExportedResourceId, resultDevices, "result");
                                break;

                            //TODO : Add other asynchronous job to check
                            default:
                                break;
                        }

                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("Error retrieving job output for " + job.ExportedResourceId + " : " + ex.Message);
                    }
                });
            }
        }
    }
}
