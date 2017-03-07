using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;

namespace AppPakCodeGenerator
{
    public partial class AppUnPak : IDisposable
    {
        private List<ResInfo> ResList = new List<ResInfo>();
        private String AppPakConfigXml = String.Empty;
        List<NativeDllInfo> DeleteWhenFinishedList = new List<NativeDllInfo>();
        public bool EncryptFiles { get; set; }
        public bool CompressFiles { get; set; }
        public List<String> NativeDlls = new List<string>();
        public List<String> Native32Dlls = new List<string>();
        public List<String> Native64Dlls = new List<string>();
        public List<String> AssemblyDlls = new List<string>();
        public string AppConfig = String.Empty;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetDllDirectory(string lpPathName);

        enum LoadLibraryFlags : uint
        {
            DONT_RESOLVE_DLL_REFERENCES = 0x00000001,
            LOAD_IGNORE_CODE_AUTHZ_LEVEL = 0x00000010,
            LOAD_LIBRARY_AS_DATAFILE = 0x00000002,
            LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE = 0x00000040,
            LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020,
            LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008
        }

        public AppUnPak()
        {
            Trace.WriteLine("AppUnPak Started");

            DbProviderFactoryRepository repository = new DbProviderFactoryRepository(new SqlCe40ProviderFactoryDescription());

            foreach (DataRow dr in DbProviderFactories.GetFactoryClasses().Rows)
                Trace.WriteLine(dr[0] + ">>" + dr[3]);

            Trace.WriteLine("Finding Resources");
            FindResources();

            // Load native DLL's into memory.
            Trace.WriteLine("Extracting Native DLLs");
            LoadNativeDLLs();

            if (!String.IsNullOrEmpty(AppConfig))
            {
                Trace.WriteLine("Loading App.Config");
                AppDomain.CurrentDomain.SetupInformation.SetConfigurationBytes(System.Text.UTF8Encoding.UTF8.GetBytes(AppConfig));
            }

            Trace.WriteLine("AppUnPak hooking into AppDomain");
            AppDomain.CurrentDomain.AssemblyLoad += new AssemblyLoadEventHandler(CurrentDomain_AssemblyLoad);
            AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(CurrentDomain_AssemblyResolve);
        }

        public void Dispose()
        {
            // Delete any files that we created on disk
            bool bFileUnloaded = true;
            while (bFileUnloaded)
            {
                bFileUnloaded = false;
                foreach (NativeDllInfo fileInfo in DeleteWhenFinishedList)
                {
                    if (!String.IsNullOrEmpty(fileInfo.FileName) && File.Exists(fileInfo.FileName))
                    {
                        try
                        {
                            if (fileInfo.hModule != IntPtr.Zero)
                            {
                                Trace.WriteLine("Attempting to free library :" + fileInfo.FileName);
                                FreeLibrary(fileInfo.hModule);
                                fileInfo.hModule = IntPtr.Zero;
                            }

                            Trace.WriteLine("Attempting to delete file :" + fileInfo.FileName);
                            File.Delete(fileInfo.FileName);
                            fileInfo.FileName = String.Empty;   
                            bFileUnloaded = true;
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine(ex.Message);
                        }
                    }
                }
            }
        }

        private void FindResources()
        {
            Trace.WriteLine("AppUnPak Find Resources");
            string[] resourceStrings = Assembly.GetEntryAssembly().GetManifestResourceNames();

            foreach (String resourceBaseName in resourceStrings)
            {
                String resourceName = resourceBaseName.Replace(".resources", "");
                if (resourceName.ToLower().Contains(".licenses"))
                    continue;
                if (resourceName.ToLower().Contains(".dtd"))
                    continue;

                ResourceManager resourceManager = new ResourceManager(resourceName, Assembly.GetEntryAssembly());
                ResourceSet resources = resourceManager.GetResourceSet(CultureInfo.CurrentCulture, true, true);

                bool bProcess = false;
                try
                {
                    foreach (DictionaryEntry entry in resources)
                    {
                        if (entry.Key.ToString().ToLower().Trim().Contains("apppakconfig"))
                        {
                            bProcess = true;
                            break;
                        }
                    }
                }
                catch
                { }
                if (bProcess == false)
                    continue;

                foreach (DictionaryEntry enumerator in resources)
                {
                    ResInfo ri = new ResInfo();
                    ri.ResourceName = resourceName;
                    ri.ResourceKey = enumerator.Key.ToString();
                    ResList.Add(ri);
                    Trace.WriteLine("AppUnPak Find Resources - Found " + resourceName + " " + enumerator.Key.ToString());

                    if (ri.ResourceKey == "App.Config")
                    {
                        AppConfig = resources.GetString("App.Config");
                        Trace.WriteLine("AppUnPak Find Resources - Found App.config");
                    }

                    if (ri.ResourceKey == "AppPakResources.AppPakConfig")
                    {
                        AppPakConfigXml = resources.GetString("AppPakResources.AppPakConfig");
                        Trace.WriteLine("AppUnPak Find Resources - Found AppPakResources.AppPakConfig");

                        // Parse Config file here
                    }
                }
            }
            ResList.Sort(delegate(ResInfo p1, ResInfo p2)
            {
                return p1.ResourceKey.CompareTo(p2.ResourceKey);
            });
        }

        public static string TempDllPath = "";
        private void LoadNativeDLLs()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), ResList[0].ResourceName);
            tempDir = Path.Combine(tempDir, DateTime.Now.ToString("ddMMyyyyhhmm"));
            Directory.CreateDirectory(tempDir);
            SetDllDirectory(tempDir);
            TempDllPath = tempDir;

            Trace.WriteLine("Loading Native Dlls");
            foreach (ResInfo ri in ResList)
            {
                // Load DLL into memory.
                bool bLoadDll = false;
                if (is64BitOperatingSystem)
                    bLoadDll = ri.ResourceKey.ToLower().StartsWith("x64native/");
                else
                    bLoadDll = ri.ResourceKey.ToLower().StartsWith("x86native/");

                if (!bLoadDll)
                    bLoadDll = ri.ResourceKey.ToLower().StartsWith("native/");

                if (bLoadDll)
                {
                    NativeDllInfo dll = new NativeDllInfo();
                    dll.FileName = Path.Combine(TempDllPath, Path.GetFileName(ri.ResourceKey));

                    Trace.WriteLine("Extracting " + dll.FileName);
                    byte[] buf = GetDLLByteArray(ri);
                    if (buf != null)
                    {
                        File.WriteAllBytes(dll.FileName, buf);
                        Trace.WriteLine("AppUnPak Extracted " + dll.FileName);
                        DeleteWhenFinishedList.Add(dll);
                    }
                }
            }

            foreach (NativeDllInfo dll in DeleteWhenFinishedList)
            {
                try
                {
                    if (File.Exists(dll.FileName) == false) continue;

                    // Do not load any other files.
                    if ((dll.FileName.ToLower().EndsWith(".dll") == false) && (dll.FileName.ToLower().EndsWith(".exe") == false))
                        continue;

                    // Do not load VC Runtime.
                    if (dll.FileName.ToLower().Contains("msvcr")) continue;

                    Trace.WriteLine("Loading " + dll.FileName);
                    dll.hModule = LoadLibrary(dll.FileName);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex.Message);
                }
            }
        }

        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            // Get the Name of the AssemblyFile
            var assemblyName = new AssemblyName(args.Name);
            var dllName = assemblyName.Name + ".dll";
            foreach (ResInfo ri in ResList)
            {
                if (ri.ResourceKey.ToLower().Trim().EndsWith(dllName.ToLower().Trim()))
                {
                    Trace.WriteLine("AppUnPak Domain.Resolved assembly from resources " + ri.ResourceName);
                    return LoadAssembly(ri);
                }
            }

            Trace.WriteLine("AppPak Resolving " + args.Name);
            string[] parts = args.Name.Split(',');
            string Name = parts[0] + ".dll";
            Name = Name.ToLower().Trim();

            foreach (ResInfo ri in ResList)
            {
                if (ri.ResourceKey.ToLower().Trim().EndsWith(Name.ToLower().Trim()))
                {
                    Trace.WriteLine("AppUnPak Domain.Resolved assembly from resources " + ri.ResourceName);
                    return LoadAssembly(ri);
                }
            }

            Trace.WriteLine("Assembly not found in resources");
            throw new NotImplementedException();
        }

        /// <summary>
        /// Load Assembly from resource by resource name
        /// </summary>
        /// <param name="Name"></param>
        /// <returns></returns>
        public Assembly LoadAssembly(string Name)
        {
            foreach (ResInfo ri in ResList)
            {
                if (ri.ResourceKey.ToLower().Trim().EndsWith(Name.ToLower().Trim()))
                {
                    Trace.WriteLine("AppUnPak Domain.Resolved assembly from resources " + ri.ResourceName);
                    return LoadAssembly(ri);
                }
            }
            return null;
        }

        private Assembly LoadAssembly(ResInfo ri)
        {
            Trace.WriteLine("Loading Assembly : " + ri.ResourceKey + " from resource :" + ri.ResourceName);

            byte[] buf = GetDLLByteArray(ri);
            if (buf != null)
            {
                if (ri.ResourceKey.ToLower().Contains("system.data.sqlserverce"))
                {
                    string fileName = Path.Combine(TempDllPath, ri.ResourceKey.Replace("Assembly/", ""));
                    File.WriteAllBytes(fileName, buf);
                    Assembly loadedAsm = Assembly.LoadFile(fileName);
                    return loadedAsm;
                }
                else
                {
                    Assembly loadedAsm = Assembly.Load(buf);
                    return loadedAsm;
                }
            }
            else
                return null;
        }

        private byte[] GetDLLByteArray(ResInfo ri)
        {
            ResourceManager resourceManager = new ResourceManager(ri.ResourceName, Assembly.GetEntryAssembly());
            ResourceSet resources = resourceManager.GetResourceSet(CultureInfo.CurrentCulture, true, true);
            byte[] buf = (byte[])resources.GetObject(ri.ResourceKey);

            MemoryStream ms2 = new MemoryStream(buf);
            // if (CompressFiles)
            {
                MemoryStream ms = new MemoryStream(buf);
                DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress);
                ms2 = new MemoryStream();

                int bufSize = 1024 * 64;
                int bytesRead = 0;
                byte[] decodedBuf = new byte[bufSize];
                do
                {
                    bytesRead = ds.Read(decodedBuf, 0, bufSize);
                    ms2.Write(decodedBuf, 0, bytesRead);
                }
                while (bytesRead > 0);
            }
            return ms2.ToArray();
        }

        static void CurrentDomain_AssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            Trace.WriteLine("Assembly '" + args.LoadedAssembly.FullName + "' Loaded");
        }

        public static MachineType GetDllMachineType(string dllPath)
        {
            //see http://www.microsoft.com/whdc/system/platform/firmware/PECOFF.mspx
            //offset to PE header is always at 0x3C
            //PE header starts with "PE\0\0" =  0x50 0x45 0x00 0x00
            //followed by 2-byte machine type field (see document above for enum)
            FileStream fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read);
            BinaryReader br = new BinaryReader(fs);
            fs.Seek(0x3c, SeekOrigin.Begin);
            Int32 peOffset = br.ReadInt32();
            fs.Seek(peOffset, SeekOrigin.Begin);
            UInt32 peHead = br.ReadUInt32();
            if (peHead != 0x00004550) // "PE\0\0", little-endian
                throw new Exception("Can't find PE header");
            MachineType machineType = (MachineType)br.ReadUInt16();
            br.Close();
            fs.Close();
            return machineType;
        }

        public enum MachineType : ushort
        {
            IMAGE_FILE_MACHINE_UNKNOWN = 0x0,
            IMAGE_FILE_MACHINE_AM33 = 0x1d3,
            IMAGE_FILE_MACHINE_AMD64 = 0x8664,
            IMAGE_FILE_MACHINE_ARM = 0x1c0,
            IMAGE_FILE_MACHINE_EBC = 0xebc,
            IMAGE_FILE_MACHINE_I386 = 0x14c,
            IMAGE_FILE_MACHINE_IA64 = 0x200,
            IMAGE_FILE_MACHINE_M32R = 0x9041,
            IMAGE_FILE_MACHINE_MIPS16 = 0x266,
            IMAGE_FILE_MACHINE_MIPSFPU = 0x366,
            IMAGE_FILE_MACHINE_MIPSFPU16 = 0x466,
            IMAGE_FILE_MACHINE_POWERPC = 0x1f0,
            IMAGE_FILE_MACHINE_POWERPCFP = 0x1f1,
            IMAGE_FILE_MACHINE_R4000 = 0x166,
            IMAGE_FILE_MACHINE_SH3 = 0x1a2,
            IMAGE_FILE_MACHINE_SH3DSP = 0x1a3,
            IMAGE_FILE_MACHINE_SH4 = 0x1a6,
            IMAGE_FILE_MACHINE_SH5 = 0x1a8,
            IMAGE_FILE_MACHINE_THUMB = 0x1c2,
            IMAGE_FILE_MACHINE_WCEMIPSV2 = 0x169,
        }

        public static bool? UnmanagedDllIs64Bit(string dllPath)
        {
            switch (GetDllMachineType(dllPath))
            {
                case MachineType.IMAGE_FILE_MACHINE_AMD64:
                case MachineType.IMAGE_FILE_MACHINE_IA64:
                    return true;
                case MachineType.IMAGE_FILE_MACHINE_I386:
                    return false;
                default:
                    return null;
            }
        }

        static bool is64BitProcess = (IntPtr.Size == 8);
        public static bool is64BitOperatingSystem = is64BitProcess || InternalCheckIsWow64();

        public static bool InternalCheckIsWow64()
        {
            if (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") != "x86")
                return true;
            else
                return false;
        }
    }

    internal class ResInfo
    {
        public String ResourceName { get; set; }
        public String ResourceKey { get; set; }
        public override string ToString()
        {
            return String.Format("{0} ({1})", ResourceName, ResourceKey);
        }
    }

    internal class NativeDllInfo
    {
        public String FileName { get; set; }
        public IntPtr hModule { get; set; }
        public bool bAssembly { get; set; }
    }

    public class DbProviderFactoryRepository
    {
        /// <summary>
        /// The table containing all the data.
        /// </summary>
        private DataTable dbProviderFactoryTable;

        /// <summary>
        /// Name of the configuration element.
        /// </summary>
        private const string DbProviderFactoriesElement = "DbProviderFactories";

        /// <summary>
        /// Initialize the repository.
        /// </summary>
        public DbProviderFactoryRepository()
        {
            OpenTable();
        }

        public DbProviderFactoryRepository(DbProviderFactoryDescription provider)
            : this()
        {
            Trace.WriteLine("DbProviderFactoryRepository Started");
            Add(provider);
        }

        /// <summary>
        /// Opens the table.
        /// </summary>
        private void OpenTable()
        {
            // Open the configuration.
            var dataConfiguration = ConfigurationManager.GetSection("system.data") as System.Data.DataSet;
            if (dataConfiguration == null)
                throw new InvalidOperationException("Unable to open 'System.Data' from the configuration");

            // Open the provider table.
            if (dataConfiguration.Tables.Contains(DbProviderFactoriesElement))
                dbProviderFactoryTable = dataConfiguration.Tables[DbProviderFactoriesElement];
            else
                throw new InvalidOperationException("Unable to open the '" + DbProviderFactoriesElement + "' table");
        }

        /// <summary>
        /// Adds the specified provider.
        /// </summary>
        /// <param name="provider">The provider.</param>
        public void Add(DbProviderFactoryDescription provider)
        {
            Trace.WriteLine("DbProviderFactoryRepository.Add " + provider.Type);
            Delete(provider);
            List<DbProviderFactoryDescription> deleteList = new List<DbProviderFactoryDescription>();
            foreach (DbProviderFactoryDescription factory in this.GetAll())
            {
                if (factory.Invariant.ToLower().Contains("sqlserverce"))
                    deleteList.Add(factory);
            }
            foreach (DbProviderFactoryDescription factory in deleteList)
            {
                Trace.WriteLine("Removing " + factory.Type);
                this.Delete(factory);
            }
            dbProviderFactoryTable.Rows.Add(provider.Name, provider.Description, provider.Invariant, provider.Type);
        }

        /// <summary>
        /// Deletes the specified provider if present.
        /// </summary>
        /// <param name="provider">The provider.</param>
        public void Delete(DbProviderFactoryDescription provider)
        {
            List<DataRow> deleteList = new List<DataRow>();
            foreach (DataRow dr in dbProviderFactoryTable.Rows)
            {
                if (Convert.ToString(dr[2]) == provider.Invariant)
                    deleteList.Add(dr);
            }
            foreach (DataRow dr in deleteList)
                dbProviderFactoryTable.Rows.Remove(dr);
        }

        /// <summary>
        /// Gets all providers.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<DbProviderFactoryDescription> GetAll()
        {
            List<DbProviderFactoryDescription> list = new List<DbProviderFactoryDescription>();
            foreach (DataRow dr in dbProviderFactoryTable.Rows)
                list.Add(new DbProviderFactoryDescription(dr));
            return list;
        }

        /// <summary>
        /// Get provider by invariant.
        /// </summary>
        /// <param name="invariant"></param>
        /// <returns></returns>
        public DbProviderFactoryDescription GetByInvariant(string invariant)
        {
            foreach (DataRow dr in dbProviderFactoryTable.Rows)
            {
                if (Convert.ToString(dr[2]) == invariant)
                    return new DbProviderFactoryDescription(dr);
            }
            return null;
        }
    }

    public class DbProviderFactoryDescription
    {
        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        /// <value>The name.</value>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the invariant.
        /// </summary>
        /// <value>The invariant.</value>
        public string Invariant { get; set; }

        /// <summary>
        /// Gets or sets the description.
        /// </summary>
        /// <value>The description.</value>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the type.
        /// </summary>
        /// <value>The type.</value>
        public string Type { get; set; }

        /// <summary>
        /// Initialize the description.
        /// </summary>
        public DbProviderFactoryDescription()
        {

        }

        /// <summary>
        /// Initialize the description.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="description"></param>
        /// <param name="invariant"></param>
        /// <param name="type"></param>
        public DbProviderFactoryDescription(string name, string description, string invariant, string type)
        {
            this.Name = name;
            this.Description = description;
            this.Invariant = invariant;
            this.Type = type;
        }

        /// <summary>
        /// Initialize the description based on a row.
        /// </summary>
        /// <param name="row">The row.</param>
        internal DbProviderFactoryDescription(DataRow row)
        {
            this.Name = row[0] != null ? row[0].ToString() : null;
            this.Description = row[1] != null ? row[1].ToString() : null;
            this.Invariant = row[2] != null ? row[2].ToString() : null;
            this.Type = row[3] != null ? row[3].ToString() : null;
        }
    }

    /// <summary>
    /// Db Provider Description for Sql CE 3.5
    /// </summary>
    public class SqlCe35ProviderFactoryDescription : DbProviderFactoryDescription
    {
        public const string ProviderName = "Microsoft SQL Server Compact Data Provider";
        public const string ProviderInvariant = "System.Data.SqlServerCe.3.5";
        public const string ProviderDescription = ".NET Framework Data Provider for Microsoft SQL Server Compact";
        public const string ProviderType = "System.Data.SqlServerCe.SqlCeProviderFactory,System.Data.SqlServerCe, Version=3.5.1.0, Culture=neutral, PublicKeyToken=89845dcd8080cc91";

        /// <summary>
        /// Initialize the description.
        /// </summary>
        public SqlCe35ProviderFactoryDescription()
            : base(ProviderName, ProviderDescription, ProviderInvariant, ProviderType)
        {
        }
    }

    /// <summary>
    /// Db Provider Description for Sql CE 4.0
    /// </summary>
    public class SqlCe40ProviderFactoryDescription : DbProviderFactoryDescription
    {
        public const string ProviderName = "Microsoft SQL Server Compact Data Provider";
        public const string ProviderInvariant = "System.Data.SqlServerCe.4.0";
        public const string ProviderDescription = ".NET Framework Data Provider for Microsoft SQL Server Compact";
        public const string ProviderType = "System.Data.SqlServerCe.SqlCeProviderFactory,System.Data.SqlServerCe, Version=4.0.0.0, Culture=neutral, PublicKeyToken=89845dcd8080cc91";

        /// <summary>
        /// Initialize the description.
        /// </summary>
        public SqlCe40ProviderFactoryDescription()
            : base(ProviderName, ProviderDescription, ProviderInvariant, ProviderType)
        {
        }
    }

    public static class SqlCeUpgrade
    {
        //public static void EnsureVersion40(this System.Data.SqlServerCe.SqlCeEngine engine, string filename)
        //{
        //    SQLCEVersion fileversion = DetermineVersion(filename);
        //    if (fileversion == SQLCEVersion.SQLCE20)
        //        throw new ApplicationException("Unable to upgrade from 2.0 to 4.0");

        //    if (SQLCEVersion.SQLCE40 > fileversion)
        //    {
        //        engine.Upgrade();
        //    }
        //}

        public enum SQLCEVersion
        {
            SQLCE20 = 0,
            SQLCE30 = 1,
            SQLCE35 = 2,
            SQLCE40 = 3
        }
        public static SQLCEVersion DetermineVersion(string filename)
        {
            var versionDictionary = new Dictionary<int, SQLCEVersion> 
        { 
            { 0x73616261, SQLCEVersion.SQLCE20 }, 
            { 0x002dd714, SQLCEVersion.SQLCE30},
            { 0x00357b9d, SQLCEVersion.SQLCE35},
            { 0x003d0900, SQLCEVersion.SQLCE40}
        };
            int versionLONGWORD = 0;
            try
            {
                using (var fs = new FileStream(filename, FileMode.Open))
                {
                    fs.Seek(16, SeekOrigin.Begin);
                    using (BinaryReader reader = new BinaryReader(fs))
                    {
                        versionLONGWORD = reader.ReadInt32();
                    }
                }
            }
            catch
            {
                throw;
            }
            if (versionDictionary.ContainsKey(versionLONGWORD))
            {
                return versionDictionary[versionLONGWORD];
            }
            else
            {
                throw new ApplicationException("Unable to determine database file version");
            }
        }
    }
}
