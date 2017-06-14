using AzImportExportLibrary;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace AzImport
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                string sourcePath = null;
                string clientId = ConfigurationManager.AppSettings["AAD_ApplicationId"];
                string clientSecret = ConfigurationManager.AppSettings["AAD_ApplicationSecret"];
                string tenantId = ConfigurationManager.AppSettings["AAD_TenantId"];
                string destinationSubscriptionId = ConfigurationManager.AppSettings["DestinationSubscriptionId"];
                string sourceSubscriptionId = ConfigurationManager.AppSettings["SourceSubscriptionId"];
                string authorizationEndpoint = ConfigurationManager.AppSettings["AuthorizationEndpoint"];
                string managementApi = ConfigurationManager.AppSettings["ManagementApi"];
                string sourceResourceGroupName = ConfigurationManager.AppSettings["SourceResourceGroupName"];
                string destinationResourceGroupName = ConfigurationManager.AppSettings["DestinationResourceGroupName"];
                string defaultResourcePassword = ConfigurationManager.AppSettings["DefaultResourcePassword"];

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
                        case "-SourceResourceGroupName":
                            sourceResourceGroupName = value;
                            break;
                        case "-DestinationResourceGroupName":
                            destinationResourceGroupName = value;
                            break;
                        case "-SourcePath":
                            sourcePath = value;
                            //root directory containing the subscriptions folder
                            break;
                        case "-DefaultResourcePassword":
                            defaultResourcePassword = value;
                            //root directory containing the subscriptions folder
                            break;

                    }
                }


                if (String.IsNullOrEmpty(clientId) || String.IsNullOrEmpty(clientSecret) || String.IsNullOrEmpty(destinationSubscriptionId) || String.IsNullOrEmpty(authorizationEndpoint))
                {
                    OutputHelp();
                    return;
                }



                AzSubscriptionImport retriever = new AzSubscriptionImport(clientId, clientSecret, tenantId, defaultResourcePassword, authorizationEndpoint, managementApi, sourcePath);

                retriever.ImportAllResourceGroups(sourceSubscriptionId, destinationSubscriptionId);

                Console.WriteLine("IMPORTS COMPLETED.... press a enter to exit.");

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
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
