using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzExport
{
    public class ProviderInformation
    {
        public string Name;
        public string Namespace;
        public string ApiVersion;
        public List<string> ReadOperations = new List<string>();
    }
}
