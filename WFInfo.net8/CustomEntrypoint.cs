using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Serilog;
using Serilog.Events;
using Tesseract;
using ILogger = Serilog.ILogger;

namespace WFInfo;

public class CustomEntrypoint
{
    private static readonly ILogger Logger = Log.Logger.ForContext<CustomEntrypoint>();
    
    private const string liblept = "leptonica-1.82.0";
    private const string libtesseract = "tesseract50";
    private const string tesseract_version_folder = "tesseract5";

    private static readonly string[] ListOfDlls =
    [
        @"\x86\" + libtesseract + ".dll",
        @"\x86\" + liblept      + ".dll",
        @"\x64\" + libtesseract + ".dll",
        @"\x64\" + liblept      + ".dll",
        @"\Tesseract.dll"
    ];

    private static readonly string[] ListOfChecksums =
    [
        "a87ba6ac613b8ecb5ed033e57b871e6f", //  x86/tesseract50
        "e62f9ef3dd31df439fa2a37793b035db", //  x86/leptonica-1.82.0
        "446370b590a3c14e0fda0a2029b8e6fa", //  x64/tesseract50
        "2813455700fb7c1bc09738ca56ae7da7", //  x64/leptonica-1.82.0
        "528d4d1eb0e07cfe1370b592da6f49fd"  //  Tesseract
    ];

    private static readonly string appPath =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\WFInfo";

    private static readonly string libs_hotlink_prefix = "https://raw.githubusercontent.com/WFCD/WFinfo/libs";
    private static readonly string tesseract_hotlink_prefix = libs_hotlink_prefix + @"/" + libtesseract + @"/";
    private static string tesseract_hotlink_platform_specific_prefix;
    private static readonly string app_data_tesseract_catalog = appPath + @"\" + tesseract_version_folder;

    public static readonly string appdata_tessdata_folder = appPath + @"\tessdata";

    private static readonly InitialDialogue dialogue = new InitialDialogue();
    public static CancellationTokenSource stopDownloadTask;
    private static string build_version = Assembly.GetExecutingAssembly().GetName().Version.ToString();

    private static void cleanLegacyTesseractIfNeeded()
    {
        string[] legacyDllNames =
        [
            @"\x86\libtesseract400.dll",
            @"\x86\liblept1760.dll",
            @"\x64\libtesseract400.dll",
            @"\x64\liblept1760.dll"
        ];
        foreach (string legacyDdlName in legacyDllNames)
        {
            var pathToCheck = app_data_tesseract_catalog + legacyDdlName;
            if (File.Exists(pathToCheck))
            {
                Logger.Debug("Cleaning legacy leftover. file={File}", legacyDdlName);
                File.Delete(pathToCheck);
            }
        }
    }

    // [STAThread]
    public static async Task Run(AppDomain currentDomain)
    {
        currentDomain.UnhandledException += MyHandler;

        var configuration = ConfigurationBuilder().Build();
        var logger = CreateLogger(configuration);
        
        Directory.CreateDirectory(appPath);

        string thisprocessname = Process.GetCurrentProcess().ProcessName;
        
        logger.Information("Starting WFInfo V{Version}", build_version);
        
        string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        if (Process.GetProcesses().Count(p => p.ProcessName == thisprocessname) > 1)
        {
            using (StreamWriter sw = File.AppendText(appPath + @"\debug.log"))
            {
                sw.WriteLineAsync("[" + DateTime.UtcNow + "]   Duplicate process found - start canceled. Version: " +
                                  version);
            }

            MessageBox.Show("Another instance of WFInfo is already running, close it and try again",
                "WFInfo V" + version);
            return;
        }

        Directory.CreateDirectory(app_data_tesseract_catalog);
        Directory.CreateDirectory(app_data_tesseract_catalog + @"\x86");
        Directory.CreateDirectory(app_data_tesseract_catalog + @"\x64");
        Directory.CreateDirectory(appdata_tessdata_folder);

        cleanLegacyTesseractIfNeeded();
        await CollectDebugInfo();
        tesseract_hotlink_platform_specific_prefix = tesseract_hotlink_prefix;

        // Refresh traineddata structure
        // This is temporary, to be removed in half year from now
        if (File.Exists(appdata_tessdata_folder + @"\engbest.traineddata"))
        {
            // To avoid conflicts for folks who like to experiment...
            if (File.Exists(appdata_tessdata_folder + @"\en.traineddata"))
            {
                File.Delete(appdata_tessdata_folder + @"\en.traineddata");
            }

            File.Move(appdata_tessdata_folder + @"\engbest.traineddata", appdata_tessdata_folder + @"\en.traineddata");
        }

        int filesNeeded = 0;
        for (int i = 0; i < ListOfDlls.Length; i++)
        {
            string dll = ListOfDlls[i];
            string path = app_data_tesseract_catalog + dll;
            string md5 = ListOfChecksums[i];
            if (!File.Exists(path) || GetMD5hash(path) != md5)
                filesNeeded++;
        }

        if (filesNeeded > 0)
        {
            dialogue.SetFilesNeed(filesNeeded);
            stopDownloadTask = new CancellationTokenSource();
            Task.Run(() =>
            {
                try
                {
                    RefreshTesseractDlls(stopDownloadTask.Token);
                }
                catch (Exception ex)
                {
                    if (stopDownloadTask.IsCancellationRequested)
                    {
                        dialogue.Dispatcher.Invoke(() => { dialogue.Close(); });
                    }
                    else
                    {
                        using StreamWriter sw = File.AppendText(appPath + @"\debug.log");
                        sw.WriteLineAsync(
                            "--------------------------------------------------------------------------------------------");
                        sw.WriteLineAsync(
                            "--------------------------------------------------------------------------------------------");
                        sw.WriteLineAsync("[" + DateTime.UtcNow + "]   ERROR DURING INITIAL LOAD");
                        sw.WriteLineAsync("[" + DateTime.UtcNow + "]   " + ex.ToString());
                    }
                }
            }, stopDownloadTask.Token);
            dialogue.ShowDialog();
        }

        if (stopDownloadTask is not { IsCancellationRequested: true })
        {
            currentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve_Tesseract;
            currentDomain.AssemblyResolve += OnResolveAssembly;
            TesseractEnviornment.CustomSearchPath = app_data_tesseract_catalog;
        }
    }

    static void MyHandler(object sender, UnhandledExceptionEventArgs args)
    {
        Exception e = (Exception)args.ExceptionObject;
        AddLog("MyHandler caught: "    + e.Message);
        AddLog("Runtime terminating: " + args.IsTerminating);
        AddLog(e.StackTrace);
        AddLog(e.InnerException.Message);
        AddLog(e.InnerException.StackTrace);
    }

    public static void AddLog(string argm)
    {
        //write to the debug file, includes version and UTCtime
        Debug.WriteLine(argm);
        Directory.CreateDirectory(appPath);
        using StreamWriter sw = File.AppendText(appPath + @"\debug.log");
        sw.WriteLineAsync($"[{DateTime.UtcNow} - Still in custom entrypoint]   {argm}");
    }

    public static WebClient createNewWebClient()
    {
        WebProxy proxy = null;
        string proxy_string = Environment.GetEnvironmentVariable("http_proxy");
        if (proxy_string != null)
        {
            proxy = new WebProxy(new Uri(proxy_string));
        }

        WebClient webClient = new WebClient() { Proxy = proxy };
        webClient.Headers.Add("User-Agent", "WFInfo/" + build_version);
        return webClient;
    }

    private static async Task CollectDebugInfo()
    {
        await using StreamWriter sw = File.AppendText(appPath + @"\debug.log");
        await sw.WriteLineAsync(
            "--------------------------------------------------------------------------------------------------------------------------------------------");

        try
        {
            ManagementObjectSearcher mos = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_Processor");
            foreach (ManagementObject mo in mos.Get().OfType<ManagementObject>())
            {
                sw.WriteLineAsync("[" + DateTime.UtcNow + "] CPU model is " + mo["Name"]);
            }
        }
        catch (Exception e)
        {
            await sw.WriteLineAsync("[" + DateTime.UtcNow + "] Unable to fetch CPU model due to:" + e);
        }

        //Log OS version
        await sw.WriteLineAsync("[" + DateTime.UtcNow + $"] Detected Windows version: {Environment.OSVersion}");

        //Log 64 bit application
        await sw.WriteLineAsync("[" + DateTime.UtcNow + $"] 64bit application: {Environment.Is64BitProcess}");
        
        //Log .net Version
        await sw.WriteLineAsync("[" + DateTime.UtcNow +
                                $"] Detected .net version: {Environment.Version.ToString()}");
        
        //Log C++ x64 runtimes 14.29
        using (RegistryKey ndpKey = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry32)
                                               .OpenSubKey("Installer\\Dependencies"))
        {
            try
            {
                foreach (var item in ndpKey.GetSubKeyNames()) // VC,redist.x64,amd64,14.30,bundle
                {
                    if (item.Contains("VC,redist.x64,amd64"))
                    {
                        await sw.WriteLineAsync(
                            "[" + DateTime.UtcNow + $"] {ndpKey.OpenSubKey(item).GetValue("DisplayName")}");
                    }
                }
            }
            catch (Exception e)
            {
                await sw.WriteLineAsync("[" + DateTime.UtcNow + $"] Unable to fetch x64 runtime due to: {e}");
            }
        }

        //Log C++ x86 runtimes 14.29
        using (RegistryKey ndpKey = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry32)
                                               .OpenSubKey("Installer\\Dependencies"))
        {
            try
            {
                foreach (var item in ndpKey.GetSubKeyNames()) // VC,redist.x86,x86,14.30,bundle
                {
                    if (item.Contains("VC,redist.x86,x86"))
                    {
                        await sw.WriteLineAsync(
                            "[" + DateTime.UtcNow + $"] {ndpKey.OpenSubKey(item).GetValue("DisplayName")}");
                    }
                }
            }
            catch (Exception e)
            {
                await sw.WriteLineAsync("[" + DateTime.UtcNow + $"] Unable to fetch x86 runtime due to: {e}");
            }
        }
    }

    public static string GetMD5hash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = MD5.HashData(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    public static string GetMD5hashByURL(string url)
    {
        Debug.WriteLine(url);
        WebClient webClient = createNewWebClient();
        byte[] stream = webClient.DownloadData(url);
        byte[] hash = MD5.HashData(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static void DownloadProgressCallback(object sender, DownloadProgressChangedEventArgs e)
    {
        // Displays the operation identifier, and the transfer progress.
        dialogue.Dispatcher.Invoke(() => { dialogue.UpdatePercentage(e.ProgressPercentage); });
    }

    private static async void RefreshTesseractDlls(CancellationToken token)
    {
        WebClient webClient = createNewWebClient();
        webClient.DownloadProgressChanged += DownloadProgressCallback;
        token.Register(webClient.CancelAsync);

        for (int i = 0; i < ListOfDlls.Length; i++)
        {
            if (token.IsCancellationRequested)
                break;
            string dll = ListOfDlls[i];
            string path = app_data_tesseract_catalog + dll;
            string md5 = ListOfChecksums[i];
            if (!File.Exists(path) || GetMD5hash(path) != md5)
            {
                if (File.Exists(path))
                    File.Delete(path);

                if (token.IsCancellationRequested)
                    break;
                bool success = false;
                try
                {
                    if (Directory.Exists("lib") && File.Exists("lib" + dll))
                    {
                        File.Copy("lib" + dll, app_data_tesseract_catalog + dll);
                        success = true;
                    }
                }
                catch (Exception ex)
                {
                    await using StreamWriter sw = File.AppendText(appPath + @"\debug.log");
                    await sw.WriteLineAsync("[" + DateTime.UtcNow + "]   " + dll + " couldn't be moved");
                    await sw.WriteLineAsync("[" + DateTime.UtcNow + "]   " + ex.ToString());
                }

                if (token.IsCancellationRequested)
                    break;

                if (!success)
                {
                    try
                    {
                        if (dll != @"\Tesseract.dll")
                        {
                            await webClient.DownloadFileTaskAsync(
                                tesseract_hotlink_platform_specific_prefix + dll.Replace("\\", "/"),
                                app_data_tesseract_catalog                 + dll);
                        }
                        else
                        {
                            await webClient.DownloadFileTaskAsync(tesseract_hotlink_prefix + dll.Replace("\\", "/"),
                                app_data_tesseract_catalog                                 + dll);
                        }
                    }
                    catch (Exception) when (stopDownloadTask.Token.IsCancellationRequested)
                    {
                    }
                }

                dialogue.Dispatcher.Invoke(() => { dialogue.FileComplete(); });
            }
        }

        webClient.Dispose();

        dialogue.Dispatcher.Invoke(() => { dialogue.Close(); });
    }

    private static Assembly CurrentDomain_AssemblyResolve_Tesseract(object sender, ResolveEventArgs args)
    {
        string probingPath = Path.Combine(appPath, tesseract_version_folder);
        string assyName = new AssemblyName(args.Name).Name;

        string newPath = Path.Combine(probingPath, assyName);
        if (!newPath.EndsWith(".dll"))
            newPath += ".dll";

        if (File.Exists(newPath))
            return Assembly.Load(newPath);

        return null;
    }

    // From: https://docs.microsoft.com/en-us/dotnet/framework/migration-guide/how-to-determine-which-versions-are-installed

    private static Assembly OnResolveAssembly(object sender, ResolveEventArgs args)
    {
        Assembly executingAssembly = Assembly.GetExecutingAssembly();
        AssemblyName assemblyName = new AssemblyName(args.Name);

        string path = assemblyName.Name + ".dll";
        if (assemblyName.CultureInfo.Equals(CultureInfo.InvariantCulture) == false)
            path = $@"{assemblyName.CultureInfo}\{path}";

        using Stream stream = executingAssembly.GetManifestResourceStream(path);
        if (stream == null)
            return null;

        byte[] assemblyRawBytes = new byte[stream.Length];
        stream.Read(assemblyRawBytes, 0, assemblyRawBytes.Length);
        return Assembly.Load(assemblyRawBytes);
    }

    private static ILogger CreateLogger(IConfiguration configuration)
    {
        const string defaultTemplate =
            "[{Timestamp:HH:mm:ss} T:{ThreadId} {Level:u3}] {Message:lj} {SourceContext}{NewLine}{Exception}";
        LogEventLevel level;

        #if DEBUG
        level = LogEventLevel.Debug;
        #else
        level = LogEventLevel.Information;
        #endif
        
        // Apply the config to the logger
        Log.Logger = new LoggerConfiguration()
                     .ReadFrom.Configuration(configuration)
                     .Enrich.WithThreadId()
                     .Enrich.FromLogContext()
#if DEBUG
                     .WriteTo.File(
                         restrictedToMinimumLevel: LogEventLevel.Verbose,
                         outputTemplate: defaultTemplate,
                         path:Path.Combine(appPath, "wf_info_debug.log.txt"))
                     .WriteTo.Debug(outputTemplate: defaultTemplate)
#endif
                     .CreateLogger();
        AppDomain.CurrentDomain.ProcessExit += static (_, _) => Log.CloseAndFlush();
        return Log.Logger;
    }

    private static IConfigurationBuilder ConfigurationBuilder()
    {
#if RELEASE
        const string envName = "Production";
#else
        const string envName = "Development";
#endif
        // Create our configuration sources
        return new ConfigurationBuilder()
               // Add environment variables
               .AddEnvironmentVariables()
               // Set base path for Json files as the startup location of the application
               .SetBasePath(Directory.GetCurrentDirectory())
               // Add application settings json files
               .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
               .AddJsonFile($"appsettings.{envName}.json", optional: true, reloadOnChange: false);
    }
}