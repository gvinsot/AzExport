using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AzInfoApi.Models
{
    public class AppSettings
    {
        public AppSettings(string prefix="AZINFO")
        {
            providersApiVersion = Environment.GetEnvironmentVariable(prefix + "_PROVIDERSAPIVERSION");
            tenantId = Environment.GetEnvironmentVariable(prefix+"_TENANTID");
            authorizationEndpoint = Environment.GetEnvironmentVariable(prefix+"_AUTHORIZATIONENDPOINT");
            managementApi = Environment.GetEnvironmentVariable(prefix+"_MANAGEMENTAPI");
            clientId = Environment.GetEnvironmentVariable(prefix+"_CLIENTID");
            clientSecret = Environment.GetEnvironmentVariable(prefix+"_CLIENTSECRET");
            subscriptionId = Environment.GetEnvironmentVariable(prefix+"_SUBSCRIPTIONID");
        }
        public string providersApiVersion;
        public string tenantId;
        public string authorizationEndpoint;
        public string managementApi;        
        public string clientId;
        public string clientSecret;
        public string subscriptionId;
    }
}
