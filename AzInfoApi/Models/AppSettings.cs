using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AzInfoApi.Models
{
    public class AppSettings
    {
        public string updateKey = Environment.GetEnvironmentVariable("AZINFO_UPDATEKEY");
        public string tenantId = Environment.GetEnvironmentVariable("AZINFO_TENANTID");
        public string authorizationEndpoint= "https://login.microsoftonline.com/"+ Environment.GetEnvironmentVariable("AZINFO_TENANTID") + "/";
        public string clientId= Environment.GetEnvironmentVariable("AZINFO_CLIENTID");
        public string clientSecret = Environment.GetEnvironmentVariable("AZINFO_CLIENTSECRET");
        public string subscriptionId= Environment.GetEnvironmentVariable("AZINFO_SUBSCRIPTIONID");
    }
}
