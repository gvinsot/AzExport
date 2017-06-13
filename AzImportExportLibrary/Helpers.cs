using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;

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


namespace AzImportExportLibrary
{
    public static class Helpers
    {

        public static string GetAccessToken(string clientID, string applicationSecret, string authorizationEndpoint, string managementApi)
        {
            var authenticationContext = new AuthenticationContext(authorizationEndpoint,false);
            var credential = new ClientCredential(clientId: clientID, clientSecret: applicationSecret);
            var result = authenticationContext.AcquireTokenAsync(resource: "https://management.core.windows.net/", clientCredential: credential).Result;

            if (result == null)
            {
                throw new InvalidOperationException("Failed to obtain the Authorization token");
            }

            string token = result.AccessToken;

            return token;
        }

        public static dynamic GetAzureResourceFromDisk(string resourceId, ImportExportConfiguration config, string apiVersion = null)
        {
            try
            {
                string sourceFile = config.RootFilePath+"\\"+ resourceId.Replace("/","\\")+ (apiVersion == null ? "" : ("-" + apiVersion))+".json";
                string resultString = File.ReadAllText(sourceFile);

                dynamic result = JObject.Parse(resultString);
                
                return result;
            }
            catch (Exception ex)
            {
                return null;
            }
        }
        public static void PutAzureResource(string resourceId, ImportExportConfiguration config, dynamic toSend, string apiVersion = null, string method = "PUT" )
        {
            if (apiVersion == null)
                apiVersion = config.ProvidersVersion;
            string jsonToPost = JsonConvert.SerializeObject(toSend);
            Dictionary <string, dynamic> results = new Dictionary<string, dynamic>();
            GetAzureResource(results, resourceId, config, apiVersion, method, jsonToPost);
        }

        public static dynamic GetAzureResource(Dictionary<string, dynamic> results, string resourceId, ImportExportConfiguration config, string apiVersion = null, string method = "GET", string postContent = "")
        {
            Uri uri = new Uri(config.ManagementApiUrl + resourceId + (apiVersion == null ? "" : ("?api-version=" + apiVersion)));
            try
            {
                // Create the request
                var httpWebRequest = (HttpWebRequest)WebRequest.Create(uri);
                httpWebRequest.Method = method;
                httpWebRequest.Headers.Add(HttpRequestHeader.Authorization, "Bearer " + config.AccessToken);
                httpWebRequest.UserAgent = "AzurePowershell/v3.6.0.0 PSVersion/v5.1.14393.693";
                //httpWebRequest.UserAgent = "Microsoft.Azure.Management.Compute.ComputeManagementClient/10.0.0.0 AzurePowershell/v1.0.0.0";
                //httpWebRequest.Host = "management.azure.com";

                if (method == "POST" || method == "PUT")
                {
                    httpWebRequest.ContentType = "application/json; charset=utf-8";
                    httpWebRequest.ContentLength = postContent.Length;
                    using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                    {
                        streamWriter.Write(postContent);
                    }
                }
                // Get the response
                HttpWebResponse httpResponse = null;
                httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();

                string resultString = null;
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    resultString = streamReader.ReadToEnd();
                }

                // Prettify                
                resultString = Helpers.JsonPrettify(resultString);

                // Save to disk
                if (config.SaveToDisk)
                {
                    Helpers.SaveResultToFile(config.RootFilePath, resourceId, resultString, apiVersion);
                }
                dynamic result = null;
                try
                {
                    result = JObject.Parse(resultString);

                    // Add result to result resources dictionary
                    lock (results)
                    {
                        results.Add(resourceId, result);
                    }
                }
                catch
                {
                    //ignore this error because sometime Azure returns bad json
                }

                return result;
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message + " : " + uri.ToString(), ex);
            }
        }

        public static string JsonPrettify(string json)
        {
            try
            {
                using (var stringReader = new StringReader(json))
                using (var stringWriter = new StringWriter())
                {
                    var jsonReader = new JsonTextReader(stringReader);
                    var jsonWriter = new JsonTextWriter(stringWriter) { Formatting = Formatting.Indented };
                    jsonWriter.WriteToken(jsonReader);
                    return stringWriter.ToString();
                }
            }
            catch
            {
                //ignore if we cannot prettify
                return json;
            }
        }

        public static void ZipResult(string directoryName, string rootDirectory)
        {
            string zipFile = Path.Combine(rootDirectory, directoryName + ".zip");
            string folderToZip = Path.Combine(rootDirectory, directoryName + "\\");
            if (File.Exists(zipFile))
                File.Delete(zipFile);
            ZipFile.CreateFromDirectory(folderToZip, zipFile);
            Directory.Delete(folderToZip, true);
        }

        public static dynamic GetRemoteJsonObject(string uri, string token=null)
        {
            var client = new WebClient();
            if (token != null)
            {
                client.Headers.Add("Authorization", "Bearer " + token);
            }
            string result = client.DownloadString(uri);
            return JObject.Parse(result);
        }

        public static void SaveResultToFile(string rootPath, string resourceId, string resultString, string apiVersion = "NA")
        {
            string filePath = rootPath + resourceId.Replace("/", "\\").Replace("?", "_").Replace(" ", "_") + "-" + apiVersion + ".json";

            string fileDir = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(fileDir))
                Directory.CreateDirectory(fileDir);

            File.WriteAllText(filePath, resultString);
        }

        public static dynamic GetAzureResourceAutoFindVersion(Dictionary<string, dynamic> results, string resourceId, Dictionary<string, ProviderInformation> resourcesInformations, ImportExportConfiguration config , string method = "GET", string postContent = "")
        {
            try
            {
                string apiVersion = null;
                if (resourcesInformations.ContainsKey(resourceId.ToLower()))
                    apiVersion = resourcesInformations[resourceId.ToLower()].ApiVersion;

                return Helpers.GetAzureResource(results, resourceId, config, apiVersion, method, postContent);
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Could not retrieve {resourceId}, ERROR :{ex.Message}");
                return null;
            }
        }

        public static void LoadEmbbededAssembly(string resourceName)
        {
            AppDomain.CurrentDomain.AssemblyResolve +=
              (sender, args) =>
              {
                  var an = new AssemblyName(args.Name);
                  if ("AzExport."+an.Name + ".dll" == resourceName)
                  {
                      Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
                      if (stream != null)
                      {
                          using (stream)
                          {
                              byte[] data = new byte[stream.Length];
                              stream.Read(data, 0, data.Length);
                              return Assembly.Load(data);
                          }
                      }
                  }
                  return null;
              };
        }

    }
}
