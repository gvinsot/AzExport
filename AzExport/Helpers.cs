using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.IO.Compression;
using System.Net;



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


namespace AzExport
{
    public static class Helpers
    {

        public static string GetAccessToken(string clientID, string applicationSecret, string authorizationEndpoint)
        {
            var authenticationContext = new AuthenticationContext(authorizationEndpoint);
            var credential = new ClientCredential(clientId: clientID, clientSecret: applicationSecret);
            var result = authenticationContext.AcquireTokenAsync(resource: "https://management.core.windows.net/", clientCredential: credential).Result;

            if (result == null)
            {
                throw new InvalidOperationException("Failed to obtain the JWT token");
            }

            string token = result.AccessToken;

            return token;
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
    }
}
