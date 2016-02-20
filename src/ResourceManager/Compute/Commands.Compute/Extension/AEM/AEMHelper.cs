﻿using Microsoft.Azure.Commands.Common.Authentication.Models;
using Microsoft.Azure.Commands.Compute.StorageServices;
using Microsoft.Azure.Management.Compute.Models;
using Microsoft.Azure.Management.Storage;
using Microsoft.Azure.Management.Storage.Models;
using Microsoft.WindowsAzure.Commands.Sync.Download;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Shared.Protocol;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.Commands.Compute.Extension.AEM
{
    internal class AEMHelper
    {
        private Action<ErrorRecord> _ErrorAction = null;
        private Action<string> _VerboseAction = null;
        private Action<string> _WarningAction = null;
        private PSHostUserInterface _UI = null;
        private Dictionary<string, StorageAccount> _StorageCache = new Dictionary<string, StorageAccount>(StringComparer.InvariantCultureIgnoreCase);
        private Dictionary<string, string> _StorageKeyCache = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
        private StorageManagementClient _StorageClient;
        private AzureSubscription _Subscription;

        public AEMHelper(Action<ErrorRecord> errorAction, Action<string> verboseAction, Action<string> warningAction,
            PSHostUserInterface ui, StorageManagementClient storageClient, AzureSubscription subscription)
        {
            this._ErrorAction = errorAction;
            this._VerboseAction = verboseAction;
            this._WarningAction = warningAction;
            this._UI = ui;
            this._StorageClient = storageClient;
            this._Subscription = subscription;
        }

        internal string GetStorageAccountFromUri(string uri)
        {
            var match = Regex.Match(new Uri(uri).Host, "(.*?)\\..*");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            else
            {
                WriteError("Could not determine storage account for OS disk. Please contact support");
                throw new ArgumentException("Could not determine storage account for OS disk. Please contact support");
            }
        }

        internal StorageAccount GetStorageAccountFromCache(string accountName)
        {
            if (_StorageCache.ContainsKey(accountName))
            {
                return _StorageCache[accountName];
            }

            var listResponse = this._StorageClient.StorageAccounts.List();
            var account = listResponse.First(accTemp => accTemp.Name.Equals(accountName, StringComparison.InvariantCultureIgnoreCase));

            _StorageCache.Add(account.Name, account);

            return account;
        }
        internal string GetResourceGroupFromId(string id)
        {
            var matcher = new Regex("/subscriptions/([^/]+)/resourceGroups/([^/]+)/providers/(\\w+)");
            var result = matcher.Match(id);
            if (!result.Success || result.Groups == null || result.Groups.Count < 3)
            {
                throw new InvalidOperationException(string.Format("Cannot find resource group name and storage account name from resource identity {0}", id));
            }

            return result.Groups[2].Value;
        }

        internal bool IsPremiumStorageAccount(string accountName)
        {
            return IsPremiumStorageAccount(this.GetStorageAccountFromCache(accountName));
        }

        internal int? GetDiskSizeGbFromBlobUri(string sBlobUri)
        {
            var storageClient = new StorageManagementClient();
            var blobMatch = Regex.Match(sBlobUri, "https?://(\\S*?)\\..*?/(.*)");
            if (!blobMatch.Success)
            {
                WriteError("Blob URI of disk does not match known pattern {0}", sBlobUri);
                throw new ArgumentException("Blob URI of disk does not match known pattern");
            }
            var accountName = blobMatch.Groups[1].Value;

            BlobUri blobUri;
            if (BlobUri.TryParseUri(new Uri(sBlobUri), out blobUri))
            {
                try
                {
                    var account = this.GetStorageAccountFromCache(accountName);
                    var resGroupName = this.GetResourceGroupFromId(account.Id);
                    StorageCredentialsFactory storageCredentialsFactory = new StorageCredentialsFactory(resGroupName,
                        this._StorageClient, this._Subscription);
                    StorageCredentials sc = storageCredentialsFactory.Create(blobUri);
                    CloudStorageAccount cloudStorageAccount = new CloudStorageAccount(sc, true);
                    CloudBlobClient blobClient = cloudStorageAccount.CreateCloudBlobClient();
                    CloudBlobContainer blobContainer = blobClient.GetContainerReference(blobUri.BlobContainerName);
                    var cloudBlob = blobContainer.GetPageBlobReference(blobUri.BlobName);
                    var sasToken = cloudBlob.GetSharedAccessSignature(
                        new SharedAccessBlobPolicy()
                        {
                            SharedAccessExpiryTime = DateTime.UtcNow.AddHours(24.0),
                            Permissions = SharedAccessBlobPermissions.Read
                        });
                    cloudBlob.FetchAttributes();

                    //var key = this.GetAzureStorageKeyFromCache(accountName);
                    //var account = this.GetStorageAccountFromCache(accountName);
                    //var credentials = new StorageCredentials(account.Name.ToLower(), key);
                    //var client = new CloudBlobClient(account.PrimaryEndpoints.Blob, credentials);
                    //var cloudref = blobClient.GetBlobReferenceFromServer(sc.TransformUri(new Uri(sBlobUri)));
                    return (int?)(cloudBlob.Properties.Length / (1024 * 1024 * 1024));
                }
                catch (Exception)
                {
                    this.WriteWarning("Could not determine OS Disk size.");
                }
            }

            return null;
        }

        internal AzureSLA GetVMSLA(VirtualMachine virtualMachine)
        {
            var result = new AzureSLA();
            result.HasSLA = false;
            switch (virtualMachine.HardwareProfile.VmSize)
            {
                case "Standard_DS1":
                    result.HasSLA = true;
                    result.IOPS = "3200";
                    result.TP = "32";
                    break;
                case "Standard_DS2":
                    result.HasSLA = true;
                    result.IOPS = "6400";
                    result.TP = "64";
                    break;
                case "Standard_DS3":
                    result.HasSLA = true;
                    result.IOPS = "12800";
                    result.TP = "128";
                    break;
                case "Standard_DS4":
                    result.HasSLA = true;
                    result.IOPS = "25600";
                    result.TP = "256";
                    break;
                case "Standard_DS11":
                    result.HasSLA = true;
                    result.IOPS = "6400";
                    result.TP = "64";
                    break;
                case "Standard_DS12":
                    result.HasSLA = true;
                    result.IOPS = "12800";
                    result.TP = "128";
                    break;
                case "Standard_DS13":
                    result.HasSLA = true;
                    result.IOPS = "25600";
                    result.TP = "256";
                    break;
                case "Standard_DS14":
                    result.HasSLA = true;
                    result.IOPS = "50000";
                    result.TP = "512";
                    break;
                case "Standard_GS1":
                    result.HasSLA = true;
                    result.IOPS = "5000";
                    result.TP = "125";
                    break;
                case "Standard_GS2":
                    result.HasSLA = true;
                    result.IOPS = "10000";
                    result.TP = "250";
                    break;
                case "Standard_GS3":
                    result.HasSLA = true;
                    result.IOPS = "20000";
                    result.TP = "500";
                    break;
                case "Standard_GS4":
                    result.HasSLA = true;
                    result.IOPS = "40000";
                    result.TP = "1000";
                    break;
                case "Standard_GS5":
                    result.HasSLA = true;
                    result.IOPS = "80000";
                    result.TP = "2000";
                    break;
                default:
                    break;
            }

            return result;
        }



        internal string GetAzureStorageKeyFromCache(string accountName)
        {
            if (_StorageKeyCache.ContainsKey(accountName))
            {
                return _StorageKeyCache[accountName];
            }

            var account = this.GetStorageAccountFromCache(accountName);
            var resourceGroup = this.GetResourceGroupFromId(account.Id);
            var keys = this._StorageClient.StorageAccounts.ListKeys(resourceGroup, account.Name);

            _StorageKeyCache.Add(account.Name, keys.StorageAccountKeys.Key1);

            return keys.StorageAccountKeys.Key1;
        }

        internal string GetCoreEndpoint(string storageAccountName)
        {
            var storage = this.GetStorageAccountFromCache(storageAccountName);
            //var tableendpoint = storage.PrimaryEndpoints.Table.Host;
            var blobendpoint = storage.PrimaryEndpoints.Blob.Host;

            //var tableMatch = Regex.Match(tableendpoint, ".*?\\.table\\.(.*)");
            //if (tableMatch.Success)
            //{
            //    return tableMatch.Groups[1].Value;
            //}

            var blobMatch = Regex.Match(blobendpoint, ".*?\\.blob\\.(.*)");
            if (blobMatch.Success)
            {
                return blobMatch.Groups[1].Value;
            }

            WriteWarning("Could not extract endpoint information from Azure Storage Account. Using default {0}", AEMExtensionConstants.AzureEndpoint);
            return AEMExtensionConstants.AzureEndpoint;
        }

        internal string GetAzureSAPTableEndpoint(StorageAccount storage)
        {
            return storage.PrimaryEndpoints.Table.ToString();
        }

        internal bool IsPremiumStorageAccount(StorageAccount account)
        {
            if (account.AccountType.HasValue)
            {
                return (account.AccountType.Value.ToString().StartsWith("Premium"));
            }

            WriteError("No AccountType for storage account {0} found", account.Name);
            throw new ArgumentException("No AccountType for storage account found");
        }

        internal AzureSLA GetDiskSLA(OSDisk osdisk)
        {
            return this.GetDiskSLA(osdisk.DiskSizeGB, osdisk.Vhd.Uri);
        }

        internal AzureSLA GetDiskSLA(DataDisk datadisk)
        {
            return this.GetDiskSLA(datadisk.DiskSizeGB, datadisk.Vhd.Uri);
        }

        internal AzureSLA GetDiskSLA(int? diskSize, string vhdUri)
        {
            if (!diskSize.HasValue)
            {
                diskSize = this.GetDiskSizeGbFromBlobUri(vhdUri);
            }
            if (!diskSize.HasValue)
            {
                this.WriteWarning("OS Disk size is empty and could not be determined. Assuming P10.");
                diskSize = 127;
            }

            AzureSLA sla = new AzureSLA();
            if (diskSize > 0 && diskSize < 129)
            {
                // P10
                sla.IOPS = "500";
                sla.TP = "100";
            }
            else if (diskSize > 0 && diskSize < 513)
            {
                // P20
                sla.IOPS = "2300";
                sla.TP = "150";
            }
            else if (diskSize > 0 && diskSize < 1025)
            {
                // P30
                sla.IOPS = "5000";
                sla.TP = "200";
            }
            else
            {
                WriteError("Unkown disk size for Premium Storage - {0}", diskSize);
                throw new ArgumentException("Unkown disk size for Premium Storage");
            }

            return sla;
        }

        internal void WriteHost(string message, params string[] args)
        {
            this.WriteHost(message, newLine: true, foregroundColor: null, args: args);
        }

        internal void WriteHost(string message, bool newLine)
        {
            this.WriteHost(message, newLine: newLine, foregroundColor: null);
        }
        internal void WriteHost(string message, bool newLine, params string[] args)
        {
            this.WriteHost(message, newLine: newLine, foregroundColor: null, args: args);
        }

        internal void WriteHost(string message, ConsoleColor foregroundColor)
        {
            this.WriteHost(message, newLine: true, foregroundColor: foregroundColor);
        }

        internal void WriteHost(string message, bool newLine, ConsoleColor? foregroundColor, params string[] args)
        {
            Trace.WriteLine("WriteHost:" + String.Format(message, args));

            try
            {
                this.WriteVerbose(message, args);
                var fColor = foregroundColor != null ? foregroundColor.Value : this._UI.RawUI.ForegroundColor;
                var bgColor = this._UI.RawUI.BackgroundColor;

                if (newLine)
                {
                    this._UI.WriteLine(fColor, bgColor, String.Format(message, args));
                }
                else
                {
                    this._UI.Write(fColor, bgColor, String.Format(message, args));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception while trying to write to UI: " + ex.Message);
            }
        }

        internal void WriteVerbose(string message, params object[] args)
        {
            Trace.WriteLine("WriteVerbose:" + String.Format(message, args));

            try
            {
                this._VerboseAction(String.Format(message, args));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception while trying to write to UI: " + ex.Message);
            }
        }

        internal void WriteError(string message, params object[] args)
        {
            Trace.WriteLine("WriteError:" + String.Format(message, args));

            try
            {
                this._ErrorAction(new ErrorRecord(new Exception(String.Format(message, args)), "Error", ErrorCategory.NotSpecified, null));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception while trying to write to UI: " + ex.Message);
            }
        }

        internal void WriteWarning(string message, params object[] args)
        {
            Trace.WriteLine("WriteWarning:" + String.Format(message, args));

            try
            {
                this._WarningAction(String.Format(message, args));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception while trying to write to UI: " + ex.Message);
            }
        }

        internal VirtualMachineExtension GetExtension(VirtualMachine vm, string type, string publisher)
        {
            if (vm.Resources != null)
            {
                return vm.Resources.FirstOrDefault(ext =>
                   ext.VirtualMachineExtensionType.Equals(type)
                   && ext.Publisher.Equals(publisher));
            }
            return null;
        }

        internal VirtualMachineExtensionInstanceView GetExtension(VirtualMachine vm, VirtualMachineInstanceView vmStatus, string type, string publisher)
        {
            var ext = this.GetExtension(vm, type, publisher);
            if (ext == null)
            {
                return null;
            }

            if (vmStatus.Extensions == null)
            {
                return null;
            }
            return vmStatus.Extensions.FirstOrDefault(extSt => extSt.Name.Equals(ext.Name));
        }

        internal void CheckMonProp(string CheckMessage, string PropertyName, JObject Properties, string ExpectedValue, AEMTestResult parentResult, bool checkExistance = false)
        {
            var value = GetMonPropertyValue(PropertyName, Properties);
            WriteHost(CheckMessage + "...", false);

            if (!String.IsNullOrEmpty(value) && checkExistance)
            {
                parentResult.PartialResults.Add(new AEMTestResult(CheckMessage, true));
                WriteHost("OK ", ConsoleColor.Green);
            }

            if ((!String.IsNullOrEmpty(value) && String.IsNullOrEmpty(ExpectedValue)) || (value == ExpectedValue))
            {
                parentResult.PartialResults.Add(new AEMTestResult(CheckMessage, true));
                WriteHost("OK ", ConsoleColor.Green);
            }
            else
            {
                parentResult.PartialResults.Add(new AEMTestResult(CheckMessage, false));
                WriteHost("NOT OK ", ConsoleColor.Red);
            }
        }

        internal string GetMonPropertyValue(string PropertyName, JObject Properties)
        {
            if (Properties == null)
            {
                return null;
            }
            if (Properties["cfg"] == null)
            {
                return null;
            }

            var set = Properties["cfg"].FirstOrDefault((tok) =>
            {
                JValue jval = (tok["key"] as JValue);
                if (jval != null && jval.Value != null)
                {
                    return jval.Value.Equals(PropertyName);
                }

                return false;
            });
            if (set == null)
            {
                return null;
            }
            if (set["value"] == null)
            {
                return null;
            }

            if ((set["value"] as JValue) != null)
            {
                return (set["value"] as JValue).Value as string;
            }
            return null;

        }

        internal bool CheckWADConfiguration(System.Xml.XmlDocument CurrentConfig)
        {
            if ((CurrentConfig == null)
            || (CurrentConfig.SelectSingleNode("/WadCfg/DiagnosticMonitorConfiguration") == null)
            || (int.Parse(CurrentConfig.SelectSingleNode("/WadCfg/DiagnosticMonitorConfiguration/@overallQuotaInMB").Value) < 4096)
            || (CurrentConfig.SelectSingleNode("/WadCfg/DiagnosticMonitorConfiguration/PerformanceCounters") == null)
            || (!CurrentConfig.SelectSingleNode("/WadCfg/DiagnosticMonitorConfiguration/PerformanceCounters/@scheduledTransferPeriod").
                    Value.Equals("PT1M", StringComparison.InvariantCultureIgnoreCase))
            || (CurrentConfig.SelectSingleNode("/WadCfg/DiagnosticMonitorConfiguration/PerformanceCounters/PerformanceCounterConfiguration") == null))
            {
                return false;
            }

            return true;
        }

        internal ServiceProperties GetStorageAnalytics(string storageAccountName)
        {
            var key = this.GetAzureStorageKeyFromCache(storageAccountName);
            var credentials = new StorageCredentials(storageAccountName, key);
            var cloudStorageAccount = new CloudStorageAccount(credentials, true);
            return cloudStorageAccount.CreateCloudBlobClient().GetServiceProperties();
        }

        internal bool CheckStorageAnalytics(string storageAccountName, ServiceProperties currentConfig)
        {
            if ((currentConfig == null)
                || (currentConfig.Logging == null)
                || ((currentConfig.Logging.LoggingOperations & LoggingOperations.All) != LoggingOperations.All)
                || (currentConfig.MinuteMetrics == null)
                || (currentConfig.MinuteMetrics.MetricsLevel <= 0)
                || (currentConfig.MinuteMetrics.RetentionDays < 0))
            {
                WriteVerbose("Storage account {0} does not have the required metrics enabled", storageAccountName);
                return false;
            }

            WriteVerbose("Storage account {0} has required metrics enabled", storageAccountName);
            return true;
        }

        internal bool CheckTableAndContent(string StorageAccountName, string TableName, string FilterString, string WaitChar, bool UseNewTableNames, int TimeoutinMinutes = 15)
        {
            var tableExists = false;
            StorageAccount account = null;
            if (!String.IsNullOrEmpty(StorageAccountName))
            {
                account = this.GetStorageAccountFromCache(StorageAccountName);
            }
            if (account != null)
            {
                var endpoint = this.GetCoreEndpoint(StorageAccountName);
                //var keys = this.GetAzureStorageKeyFromCache(StorageAccountName);
                //var context = New - AzureStorageContext - StorageAccountName $StorageAccountName - StorageAccountKey $keys - Endpoint $endpoint
                var key = this.GetAzureStorageKeyFromCache(StorageAccountName);
                var credentials = new StorageCredentials(StorageAccountName, key);
                var cloudStorageAccount = new CloudStorageAccount(credentials, endpoint, true);
                var tableClient = cloudStorageAccount.CreateCloudTableClient();
                var checkStart = DateTime.Now;
                var wait = true;
                CloudTable table = null;
                if (UseNewTableNames)
                {
                    try
                    {
                        table = tableClient.ListTables().FirstOrDefault(tab => tab.Name.StartsWith("WADMetricsPT1M"));
                    }
                    catch { } //#table name should be sorted 
                }
                else
                {
                    try
                    {
                        table = tableClient.GetTableReference(TableName);
                    }
                    catch { }
                }

                while (wait)
                {
                    if (table != null && table.Exists())
                    {
                        TableQuery query = new TableQuery();
                        query.FilterString = FilterString;
                        var results = table.ExecuteQuery(query);
                        if (results.Count() > 0)
                        {
                            tableExists = true;
                            break;
                        }
                    }

                    WriteHost(WaitChar, newLine: false);
                    Thread.Sleep(5000);
                    if (UseNewTableNames)
                    {
                        try
                        {
                            table = tableClient.ListTables().FirstOrDefault(tab => tab.Name.StartsWith("WADMetricsPT1M"));
                        }
                        catch { } //#table name should be sorted 
                    }
                    else
                    {
                        try
                        {
                            table = tableClient.GetTableReference(TableName);
                        }
                        catch { }
                    }

                    wait = ((DateTime.Now) - checkStart).TotalMinutes < TimeoutinMinutes;
                }
            }
            return tableExists;
        }

        //internal bool WaitforTable(CloudTableClient tableClient, string tableName, string waitChar, DateTime checkStart, int timeout)
        //{
        //    int minRowCount = 3;
        //    var tableExists = false;
        //    var wait = true;
        //    var table = tableClient.GetTableReference(tableName);
        //    do
        //    {
        //        if (table.Exists())
        //        {
        //            var schemaTableResult = table.ExecuteQuery(new 
        //                TableQuery() { SelectColumns = new List<string>() { AEMExtensionConstants.SchemasTablePhysicalTableName } });

        //            if (schemaTableResult.Count() >= minRowCount)
        //            {
        //                wait = false;
        //                tableExists = true;
        //            }
        //        }
        //        else
        //        {
        //            WriteHost(waitChar, newLine: false);
        //            Thread.Sleep(5000);
        //            wait = ((DateTime.Now) - checkStart).TotalMinutes < timeout;
        //        }
        //    } while (wait);

        //    return tableExists;
        //}

        internal bool CheckDiagnosticsTable(string storageAccountName, string resId, string host, string waitChar, string osType, int TimeoutinMinutes = 15)
        {
            var tableExists = true;
            StorageAccount account = null;
            if (!String.IsNullOrEmpty(storageAccountName))
            {
                account = this.GetStorageAccountFromCache(storageAccountName);
            }
            if (account != null)
            {
                var endpoint = this.GetCoreEndpoint(storageAccountName);
                var key = this.GetAzureStorageKeyFromCache(storageAccountName);
                var credentials = new StorageCredentials(storageAccountName, key);
                var cloudStorageAccount = new CloudStorageAccount(credentials, endpoint, true);
                var tableClient = cloudStorageAccount.CreateCloudTableClient();
                var checkStart = DateTime.Now;
                var searchTime = DateTime.UtcNow.AddMinutes(-5);

                //if (!this.WaitforTable(tableClient, AEMExtensionConstants.SchemasTable, waitChar, checkStart, TimeoutinMinutes))
                //{
                //    this.WriteVerbose("Table SchemaTable not found");
                //    return false;
                //}

                //var table = tableClient.GetTableReference(AEMExtensionConstants.SchemasTable);
                //var schemaTableResult = table.ExecuteQuery(new TableQuery() { SelectColumns = new List<string>() { AEMExtensionConstants.SchemasTablePhysicalTableName } });
                //tableExists = schemaTableResult.Count() > 0;

                foreach (var tableName in AEMExtensionConstants.WADTablesV2[osType])
                {
                    var query = TableQuery.CombineFilters(
                        TableQuery.GenerateFilterCondition("DeploymentId", QueryComparisons.Equal, resId),
                        TableOperators.And,
                        TableQuery.CombineFilters(
                            TableQuery.GenerateFilterCondition("Host", QueryComparisons.Equal, host),
                            TableOperators.And,
                            TableQuery.GenerateFilterConditionForDate("Timestamp", QueryComparisons.GreaterThanOrEqual, searchTime)));


                    var perfCounterTable = tableClient.GetTableReference(tableName);

                    bool wait = true;
                    while (wait)
                    {
                        var results = perfCounterTable.ExecuteQuery(new TableQuery() { FilterString = query });
                        if (results.Count() > 0)
                        {
                            tableExists &= true;
                            break;
                        }
                        else
                        {
                            WriteHost(waitChar, newLine: false);
                            Thread.Sleep(5000);
                        }
                        wait = ((DateTime.Now) - checkStart).TotalMinutes < TimeoutinMinutes;
                    }
                    if (!wait)
                    {
                        WriteVerbose("PerfCounter Table " + tableName + " not found");
                        tableExists = false;
                        break;
                    }
                }
            }
            return tableExists;
        }
    }
}
