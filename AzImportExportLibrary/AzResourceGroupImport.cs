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
            try
            {
                Helpers.PutAzureResource($"/subscriptions/{destinationSubscriptionId}/resourcegroups/{destinationResourceGroupName}", _config, createResourceGroup);
                Console.WriteLine("CREATED RESOURCE GROUP " + createResourceGroup.name);
            }
            catch
            {
                //may be already existing and throw a conflict error
            }


            dynamic exportedTemplate = Helpers.GetAzureResourceFromDisk(sourceRg.id.Value + "/exportTemplate", _config, _config.ProvidersVersion);

            if (exportedTemplate == null)
                return;

            dynamic importRequest = new ExpandoObject();
            importRequest.properties = new ExpandoObject();
            importRequest.properties.template = exportedTemplate.template;
            importRequest.properties.mode = "incremental";
            string timestamp = DateTime.Now.Year.ToString() + DateTime.Now.Month + DateTime.Now.Day + DateTime.Now.Hour + DateTime.Now.Minute;

            Console.WriteLine("IMPORT OF TEMPLATE FOR " + sourceRg.name);
            var jobUrl = $"/subscriptions/{destinationSubscriptionId}/resourcegroups/{destinationResourceGroupName}/providers/Microsoft.Resources/deployments/azimport{timestamp}";

            dynamic runningJob = Helpers.PutAzureResource(jobUrl, _config, importRequest);

            #region wait for import to complete
            DateTime startTime = DateTime.Now;
            dynamic importJobState;
            bool timeout = false;
            do
            {
                Thread.Sleep(5000);
                importJobState = Helpers.GetRemoteJsonObject(_config.ManagementApiUrl + jobUrl+"?api-version="+_config.ProvidersVersion, _config.AccessToken);
                timeout = (DateTime.Now - startTime) > TimeSpan.FromMinutes(180);
            }
            while (importJobState.properties.provisioningState.Value != "Succeeded" && !timeout);
            #endregion

            Console.WriteLine("SUCCESSFULLY IMPORTED TEMPLATE FOR " + sourceRg.name);

            ImportResourceGroupAdditionalData(sourceRg, destinationSubscriptionId, destinationResourceGroupName);

            Console.WriteLine("FINISHED IMPORT OF " + sourceRg.name);
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

                try
                {
                    try
                    {
                        var resultObject = Helpers.PutAzureResource(resourceId, _config, resourceObject, apiVersion, "POST");
                        Console.WriteLine("INJECTED : " + resourceId.ToLower().Split(new string[] { "/resourcegroups/" }, StringSplitOptions.None)[1]);
                    }
                    catch
                    {
                        var resultObject = Helpers.PutAzureResource(resourceId, _config, resourceObject, apiVersion, "PUT");
                        Console.WriteLine("INJECTED : " + resourceId.ToLower().Split(new string[] { "/resourcegroups/" }, StringSplitOptions.None)[1]);
                    }
                }
                catch(Exception ex)
                {
                    Console.WriteLine("NOT INJECTED : "+ resourceId.ToLower().Split(new string[] { "/resourcegroups/" }, StringSplitOptions.None)[1]);
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
