# AzExport
Export Azure Subscriptions

This software helps you export a full Azure subscription configuration.

The purpose is to retrieve a view equivalent (or more detailed) to the resource tree visible in [resources.azure.com](https://resources.azure.com/)
It exports the full ARM descriptions doing Rest API Calls.

Be carefull it is not complete, it does a global export but may need adjustment on specific resources (search fo TODO in the code)

***Usage:***

        AzExport -ClientId <client id> -ClientSecret <client secret>
			     -SubscriptionId <subscription id> 
			    [-DownloadPath <D:\temp\>] 
			    [-ZipResult true]

You can also edit the App.Config file to input Client Id, Client Secret, SubscriptionId and AuthorizationEnpoint.
The program provides following output on console :
 
And generates files with same pattern like on resources.azure.com

![alt tag](https://gvbackup.blob.core.windows.net/public/AzExportScreenshot.PNG)

The principle of this program is to explore all resources through Azure Rest API calls.
The full Azure Api documentation can be found here : [https://docs.microsoft.com/en-us/rest/api/](https://docs.microsoft.com/en-us/rest/api/)
The swagger definitions of Azure Apis are here : [https://github.com/Azure/azure-rest-api-specs](https://github.com/Azure/azure-rest-api-specs)


[DOWNLOAD NOW](https://github.com/gvinsot/AzExport/releases)


