using AzImportExportLibrary;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzImportExportLibrary
{
    public class AzResourceGroupImport
    {
        ImportExportConfiguration _config;


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
            }
            catch
            {
                //may be already existing
            }

                 
            dynamic exportedTemplate = Helpers.GetAzureResourceFromDisk(sourceRg.id.Value + "/exportTemplate", _config, _config.ProvidersVersion);

            if (exportedTemplate == null)
                return;

            var uri = $"/subscriptions/{destinationSubscriptionId}/resourcegroups/{destinationResourceGroupName}/providers/Microsoft.Resources/deployments/azimport";

            IList<string> parameters = (exportedTemplate.template.parameters as JObject).Properties().Select(p => p.Name).ToList();
            string lastValue = "na";
            foreach (var parameterName in parameters)
            {
                var parameterJObject = exportedTemplate.template.parameters[parameterName];
                string value = parameterName.Split('_')[1];

                if (parameterJObject.defaultValue == null)
                {                    
                    parameterJObject.defaultValue = parameterName.Split('_')[1];                    
                }
                if (parameterName == "config_web_name")
                {
                    parameterJObject.defaultValue = lastValue+"/web";
                }
                lastValue = value;
            }
            dynamic importRequest = new ExpandoObject();
            importRequest.properties = new ExpandoObject();
            importRequest.properties.template = exportedTemplate.template;
            importRequest.properties.mode = "incremental";

            Helpers.PutAzureResource(uri, _config, importRequest);

        }
    }
}
