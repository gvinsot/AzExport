using AzImportExportLibrary;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AzImportExportLibrary
{
    public class AzResourceGroupImport
    {
        ImportExportConfiguration _config;
        private List<JobModel> _runningJobs = new List<JobModel>();


        public AzResourceGroupImport(ImportExportConfiguration config)
        {
            _config = config;
        }


        public void ImportResourceGroup(dynamic sourceRg, string destinationSubscriptionId, string destinationResourceGroupName)
        {

            dynamic createResourceGroup = new ExpandoObject();
            createResourceGroup.location = sourceRg.location;
            string destinationResourceGroupId = $"/subscriptions/{destinationSubscriptionId}/resourcegroups/{destinationResourceGroupName}";
            try
            { 
                Helpers.PutAzureResource(destinationResourceGroupId, _config, createResourceGroup);
                Console.WriteLine("CREATED/UPDATED RESOURCE GROUP " + destinationResourceGroupName);
            }
            catch
            {
                //may be already existing and throw a conflict error
            }

            dynamic exportedTemplate = Helpers.GetAzureResourceFromDisk(sourceRg.id.Value + "/exportTemplate", _config, _config.ProvidersVersion);

            if (exportedTemplate == null)
                return;

            ApplySpecificChangesToTemplate(exportedTemplate,sourceRg.id.ToString(), destinationResourceGroupId);


            dynamic importRequest = new ExpandoObject();
            importRequest.properties = new ExpandoObject();
            importRequest.properties.template = exportedTemplate.template;
            importRequest.properties.mode = "incremental";
            string timestamp = DateTime.Now.Year.ToString() + DateTime.Now.Month + DateTime.Now.Day + DateTime.Now.Hour + DateTime.Now.Minute;

            Console.WriteLine("IMPORT OF TEMPLATE FOR " + sourceRg.name);
            var jobUrl = $"{destinationResourceGroupId}/providers/Microsoft.Resources/deployments/azimport{timestamp}";

            dynamic runningJob = Helpers.PutAzureResource(jobUrl, _config, importRequest);

            #region wait for import to complete
            DateTime startTime = DateTime.Now;
            dynamic importJobState;
            bool timeout = false;
            string provisionningState = null;
            do
            {
                Thread.Sleep(5000);
                importJobState = Helpers.GetRemoteJsonObject(_config.ManagementApiUrl + jobUrl+"?api-version="+_config.ProvidersVersion, _config.AccessToken);
                timeout = (DateTime.Now - startTime) > TimeSpan.FromMinutes(180);
                provisionningState = importJobState.properties.provisioningState.Value;
            }
            while (provisionningState != "Succeeded" && provisionningState != "Failed" && !timeout);
            #endregion

            if (provisionningState == "Succeeded")
                Console.WriteLine("SUCCESSFULLY IMPORTED TEMPLATE FOR " + sourceRg.name);
            else
                Console.WriteLine("COULD NOT IMPORT TEMPLATE FOR " + sourceRg.name + " : " + importJobState.properties?.error?.details?[0]?.message);

            ImportResourceGroupAdditionalData(sourceRg, destinationSubscriptionId, destinationResourceGroupName);

            Console.WriteLine("FINISHED IMPORT OF " + sourceRg.name);
        }

        private void ApplySpecificChangesToTemplate(dynamic exportedTemplate, string sourceRgId, string destRgId )
        {
            var parameters = (exportedTemplate.template.parameters as JObject).Properties().Select(el=>el.Name);

            foreach(var parameter in parameters)
            {
                string defaultValue = exportedTemplate.template.parameters[parameter].defaultValue.ToString();
                if (defaultValue.StartsWith(sourceRgId))
                {
                    defaultValue = defaultValue.Replace(sourceRgId, destRgId);
                    exportedTemplate.template.parameters[parameter].defaultValue = defaultValue;
                }
            }

            foreach(var resource in exportedTemplate.template.resources)
            {
                switch(resource.type.ToString())
                {
                    case "Microsoft.Network/loadBalancers":
                        if((resource.properties.inboundNatPools as JArray).Count()!=0)
                        {
                            resource.properties.inboundNatRules = new JArray();
                        }
                        break;
                    case "Microsoft.Compute/virtualMachineScaleSets":
                        resource.properties.virtualMachineProfile.osProfile.adminPassword = _config.DefaultResourcePassword;
                        break;
                    case "Microsoft.Compute/virtualMachines":
                        resource.properties.osProfile.adminPassword = _config.DefaultResourcePassword;
                        break;
                }
            }
                
        }

        public void ImportResourceGroupAdditionalData(dynamic sourceRg, string destinationSubscriptionId, string destinationResourceGroupName)
        {
            string directory = _config.RootFilePath + sourceRg.id.Value.Replace("/", "\\") + "\\providers";

            if (!Directory.Exists(directory))
                return;

            IEnumerable<string> additionalDataFiles = Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories);

            foreach (var additionnalDataFile in additionalDataFiles)
            {
                string sourceSubscriptionId = sourceRg.id.ToString().Split('/')[2];
                string resourceId = additionnalDataFile.Replace(_config.RootFilePath, "").Replace("\\", "/").Split('@')[0];
                string apiVersion = additionnalDataFile.Split(new string[] { "@", ".json" },StringSplitOptions.RemoveEmptyEntries).Last();

                var resourceObject = Helpers.GetAzureResourceFromDisk(resourceId, _config, apiVersion);

                string destinationResourceId = $"/subscriptions/{destinationSubscriptionId}/resourcegroups/{destinationResourceGroupName}/providers/" + resourceId.Split(new string[] { "/providers/" }, StringSplitOptions.None)[1];

                CleanUpResourceObject(resourceObject);

                string resourceHint = destinationResourceId.ToLower().Split(new string[] { "/resourcegroups/" }, StringSplitOptions.None)[1];
                try
                {
                    try
                    {
                        var resultObject = Helpers.PutAzureResource(destinationResourceId, _config, resourceObject, apiVersion, "POST");
                        Console.WriteLine("POST OK : " + resourceHint);
                    }
                    catch
                    {
                        var resultObject = Helpers.PutAzureResource(destinationResourceId, _config, resourceObject, apiVersion, "PUT");
                        Console.WriteLine("PUT  OK : " + resourceHint);
                    }
                }
                catch(Exception ex)
                {
                    Console.WriteLine("NOT INJECTED : "+ resourceHint);
                }
            }
        }

        public void CleanUpResourceObject(dynamic resource)
        {
            if (resource as JObject == null)
                return;

            var properties=(resource as JObject).Properties().ToList();

            foreach(var property in properties)
            {
                if(property.Name=="id" || property.Name.ToLower().Contains("stat") || property.Name.ToLower().Contains("usage"))
                {
                    ((IDictionary<string, JToken>)resource).Remove("id");
                }
                if(property.Type == JTokenType.Object)
                {
                    CleanUpResourceObject(resource[property.Name]);
                }
            }
        }

    }
}
