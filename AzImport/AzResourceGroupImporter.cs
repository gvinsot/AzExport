using AzExport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzImport
{
    public class AzResourceGroupImporter
    {
        private string _clientId = null;
        private string _clientSecret = null;
        private string _authorizationEndpoint = null;
        private string _managementApi = null;
        private string _accessToken = null;

        public AzResourceGroupImporter(string clientId, string clientSecret, string authorizationEndpoint, string managementApi)
        {
            _clientId = clientId;
            _clientSecret = clientSecret;
            _authorizationEndpoint = authorizationEndpoint;
            _managementApi = managementApi;
            _accessToken = Helpers.GetAccessToken(_clientId, _clientSecret, _authorizationEndpoint, _managementApi);
        }


        public void ImportResourceGroup(string rgExportFilesPath, string destinationSubscriptionId)
        {
            
        }
    }
}
