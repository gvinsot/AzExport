using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AzInfoApi.Models
{
    public class ApiInfo
    {
        public string Verb = "get";
        public string ApiVersion;
        public string Operation;
        public string Provider;
        public string ResourceType;
        public string DataCenter;
    }
}
