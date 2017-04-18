using AzExport;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzImport
{
    class Program
    {
        static void Main(string[] args)
        {
            Helpers.LoadEmbbededAssembly("AzExport.Microsoft.IdentityModel.Clients.ActiveDirectory.dll");
            Helpers.LoadEmbbededAssembly("AzExport.Microsoft.IdentityModel.Clients.ActiveDirectory.Platform.dll");
            Helpers.LoadEmbbededAssembly("AzExport.Microsoft.WindowsAzure.Configuration.dll");
            Helpers.LoadEmbbededAssembly("AzExport.Newtonsoft.Json.dll");

            string sourcePath = ".\\";
            string clientId = ConfigurationManager.AppSettings["AAD_ApplicationId"];
            string clientSecret = ConfigurationManager.AppSettings["AAD_ApplicationSecret"];
            string destinationSubscriptionId = ConfigurationManager.AppSettings["DestinationSubscriptionId"];
            string sourceSubscriptionId = ConfigurationManager.AppSettings["SourceSubscriptionId"];
            string authorizationEndpoint = "https://login.microsoftonline.com/" + ConfigurationManager.AppSettings["AAD_TenantId"] + "/";
            string resourceGroupName = null;

            if (args.Count() % 2 != 0)
            {
                OutputHelp();
                return;
            }

            for (int i = 0; i < args.Count(); i += 2)
            {
                string arg = args[i];
                string value = args[i + 1];
                switch (arg)
                {
                    case "-ClientId":
                        clientId = value;
                        break;
                    case "-ClientSecret":
                        clientId = value;
                        break;
                    case "-SourceSubscriptionId":
                        sourceSubscriptionId = value;
                        break;
                    case "-DestinationSubscriptionId":
                        destinationSubscriptionId = value;
                        break;
                    case "-AuthorizationEndpoint":
                        authorizationEndpoint = value;
                        break;
                    case "-ResourceGroupName":
                        resourceGroupName = value;
                        break;
                    case "-SourcePath":
                        sourcePath = value;
                        break;

                }
            }


            if (String.IsNullOrEmpty(clientId) || String.IsNullOrEmpty(clientSecret) || String.IsNullOrEmpty(destinationSubscriptionId) || String.IsNullOrEmpty(authorizationEndpoint))
            {
                OutputHelp();
                return;
            }

            AzResourceGroupImporter importer = new AzResourceGroupImporter(clientId, clientSecret, authorizationEndpoint);

            importer.ImportResourceGroup(sourcePath, destinationSubscriptionId);

            Console.WriteLine("Resource group imported.... press a enter to exit.");
            Console.ReadLine();
        }

        static void OutputHelp()
        {
            Console.WriteLine("Bad Arguments");
            Console.WriteLine("Usage:");
            Console.WriteLine("AzImport -ClientId <client id> -ClientSecret <client secret> -AuthorizationEndpoint <https://login.microsoftonline.com/{Azure AD tenant ID}/> -SubscriptionId <subscription id> [-DownloadPath <D:\\temp\\>] [-ZipResult true]");
        }
    }
}
