using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzImportExportLibrary
{
    public class ProviderInformation
    {
        public string Name;
        public string ApiVersion;
        public List<string> ReadOperations = new List<string>();
    }
}
