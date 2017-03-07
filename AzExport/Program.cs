﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;

/*
 * Sample Code is provided for the purpose of illustration only and is not intended to be used in a production environment. 
 * THIS SAMPLE CODE AND ANY RELATED INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESSED OR IMPLIED, 
 * INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
 * We grant You a nonexclusive, royalty-free right to use and modify the Sample Code and to reproduce and distribute the object code form 
 * of the Sample Code, provided that. You agree: (i) to not use Our name, logo, or trademarks to market Your software product in which the 
 * Sample Code is embedded; (ii) to include a valid copyright notice on Your software product in which the Sample Code is embedded; 
 * and (iii) to indemnify, hold harmless, and defend Us and Our suppliers from and against any claims or lawsuits, including attorneys’ fees, 
 * that arise or result from the use or distribution of the Sample Code
 */

namespace AzJson
{
    class Program
    {
        
        static void Main(string[] args)
        {
            string downloadPath = ".\\";
            bool zipResult = false;
            bool providersMode = false;
            string clientId = ConfigurationManager.AppSettings["AAD_ApplicationId"];
            string clientSecret = ConfigurationManager.AppSettings["AAD_ApplicationSecret"];
            string subscriptionId = ConfigurationManager.AppSettings["SubscriptionId"];
            string authorizationEndpoint = "https://login.microsoftonline.com/"+ ConfigurationManager.AppSettings["AAD_TenantId"] + "/";

            if (args.Count() % 2 != 0)
            {
                OutputHelp();
                return;
            }

            for(int i=0;i<args.Count();i+=2)
            {
                string arg = args[i];
                string value = args[i + 1];
                switch(arg)
                {
                    case "-ClientId":
                        clientId = value;
                        break;
                    case "-ClientSecret":
                        clientId = value;
                        break;
                    case "-AuthorizationEndpoint":
                        authorizationEndpoint = value;
                        break;
                    case "-SubscriptionId":
                        subscriptionId = value;
                        break;
                    case "-DownloadPath":
                        downloadPath = value;
                        break;
                    case "-ZipResult":
                        zipResult = bool.TrueString.ToLower() == value.ToLower();
                        break;
                    case "-AddProviders":
                        providersMode = bool.TrueString.ToLower() == value.ToLower();
                        break;
                }
            }


            if (String.IsNullOrEmpty(clientId) || String.IsNullOrEmpty(clientSecret) || String.IsNullOrEmpty(subscriptionId) || String.IsNullOrEmpty(authorizationEndpoint))
            {
                OutputHelp();
                return;
            }

            AzSubRetriever retriever = new AzSubRetriever(clientId, clientSecret, authorizationEndpoint);
            if (downloadPath != null)
            {
                retriever.FileDownloadPath = downloadPath;
            }

            retriever.RetrieveAllResourcesViaRG(subscriptionId, true, zipResult); 

            Console.WriteLine("All data retrieved.... press a enter to exit.");
            Console.ReadLine();
        }

        static void OutputHelp()
        {
            Console.WriteLine("Bad Arguments");
            Console.WriteLine("Usage:");
            Console.WriteLine("AzJson -ClientId <client id> -ClientSecret <client secret> -AuthorizationEndpoint <https://login.microsoftonline.com/{Azure AD tenant ID}/> -SubscriptionId <subscription id> [-DownloadPath <D:\\temp\\>] [-ZipResult true]");
        }


    }
}
