using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    private static readonly string app_data_tesseract_catalog = Path.Combine(appPath, tesseract_version_folder);

    public static readonly string appdata_tessdata_folder = Path.Combine(appPath, "tessdata");

    private static InitialDialogue? dialogue;
    
    public static CancellationTokenSource stopDownloadTask;
    private static string build_version = Assembly.GetExecutingAssembly().GetName().Version.ToString();

    private static void CleanLegacyTesseractIfNeeded()
    {
        string[] legacyDllNames =
        [
            @"\x86\libtesseract400.dll",
            @"\x86\liblept1760.dll",
            @"\x64\libtesseract400.dll",
            @"\x64\liblept1760.dll"
        ];
        foreach (var legacyDdlName in legacyDllNames)
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
    public static async Task Run(AppDomain currentDomain, IServiceProvider sp)
    {
        currentDomain.UnhandledException += MyHandler;

        // var configuration = ConfigurationBuilder().Build();
        // var logger = CreateLogger(configuration);

        Logger.Information("Starting WFInfo V{Version}", build_version);

        if (DetectInstance())
            return;

        CreateRequiredDirectories();
        CleanLegacyTesseractIfNeeded();
        CollectDebugInfo();
        
        tesseract_hotlink_platform_specific_prefix = tesseract_hotlink_prefix;

        // Refresh traineddata structure
        // This is temporary, to be removed in half year from now
        if (File.Exists(appdata_tessdata_folder + @"\engbest.traineddata"))
        {
            // To avoid conflicts for folks who like to experiment...
            if (File.Exists(appdata_tessdata_folder + @"\en.traineddata"))
                File.Delete(appdata_tessdata_folder + @"\en.traineddata");

            File.Move(appdata_tessdata_folder + @"\engbest.traineddata", appdata_tessdata_folder + @"\en.traineddata");
        }
        
        EnsureRequiredFilesExists(sp);

        if (stopDownloadTask is not { IsCancellationRequested: true })
        {
            currentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve_Tesseract;
            currentDomain.AssemblyResolve += OnResolveAssembly;
            TesseractEnviornment.CustomSearchPath = app_data_tesseract_catalog;
        }
    }

    private static void EnsureRequiredFilesExists(IServiceProvider sp)
    {
        var filesNeeded = 0;
        for (var i = 0; i < ListOfDlls.Length; i++)
        {
            var dll = ListOfDlls[i];
            var path = app_data_tesseract_catalog + dll;
            var md5 = ListOfChecksums[i];
            if (!File.Exists(path) || GetMD5hash(path) != md5)
                filesNeeded++;
        }

        if (filesNeeded <= 0)
            return;

        stopDownloadTask = new CancellationTokenSource();
        dialogue = sp.GetRequiredService<InitialDialogue>();
        dialogue.SetFilesNeed(filesNeeded);
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
                    Logger.Error(ex, "Error during initial load");
                }
            }
        }, stopDownloadTask.Token);
        dialogue.ShowDialog();
    }

    private static void CreateRequiredDirectories()
    {
        Directory.CreateDirectory(appPath);
        Directory.CreateDirectory(app_data_tesseract_catalog);
        Directory.CreateDirectory(Path.Combine(app_data_tesseract_catalog, "x86"));
        Directory.CreateDirectory(Path.Combine(app_data_tesseract_catalog, "x64"));
        Directory.CreateDirectory(appdata_tessdata_folder);
    }

    private static bool DetectInstance()
    {
        var processName = Process.GetCurrentProcess().ProcessName;

        if (Process.GetProcesses().Count(p => p.ProcessName == processName) <= 1)
            return false;

        Logger.Debug("Duplicate process found - start canceled. version={Version}", build_version);

        var caption = $"WFInfo V{build_version}";
        MessageBox.Show("Another instance of WFInfo is already running, close it and try again", caption);

        return true;
    }

    private static void MyHandler(object sender, UnhandledExceptionEventArgs args)
    {
        var e = (Exception)args.ExceptionObject;
        Logger.Error(e, "Unhandled exception. isTerminating={IsTerminating}", args.IsTerminating);
    }

    [Obsolete("Obsolete")]
    public static WebClient CreateNewWebClient()
    {
        WebProxy proxy = null;
        var proxy_string = Environment.GetEnvironmentVariable("http_proxy");
        if (proxy_string != null)
        {
            proxy = new WebProxy(new Uri(proxy_string));
        }

        var webClient = new WebClient { Proxy = proxy };
        webClient.Headers.Add("User-Agent", $"WFInfo/{build_version}");
        return webClient;
    }

    private static void CollectDebugInfo()
    {
        Logger.Debug("--------------------------------------------------------------------------------------------------------------------------------------------");
        
        try
        {
            var mos = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_Processor");
            foreach (var mo in mos.Get().OfType<ManagementObject>())
                Logger.Debug("CPU model is {Name}", mo["Name"]);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Unable to fetch CPU model");
        }

        //Log OS version
        Logger.Debug("Detected Windows version: {OS}", Environment.OSVersion);

        //Log 64 bit application
        Logger.Debug("64bit application: {Is64BitProcess}", Environment.Is64BitProcess);

        //Log .net Version
        Logger.Debug("Detected .net version: {Version}", Environment.Version);

        //Log C++ x64 runtimes 14.29
        using (var ndpKey = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry32)
                                       .OpenSubKey("Installer\\Dependencies"))
        {
            try
            {
                foreach (var item in ndpKey.GetSubKeyNames()) // VC,redist.x64,amd64,14.30,bundle
                {
                    if (item.Contains("VC,redist.x64,amd64"))
                        Logger.Debug("Detected x64 runtime: {DisplayName}", ndpKey.OpenSubKey(item).GetValue("DisplayName"));
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Unable to fetch x64 runtime");
            }
        }

        //Log C++ x86 runtimes 14.29
        using (var ndpKey = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry32)
                                       .OpenSubKey("Installer\\Dependencies"))
        {
            try
            {
                foreach (var item in ndpKey.GetSubKeyNames()) // VC,redist.x86,x86,14.30,bundle
                {
                    if (item.Contains("VC,redist.x86,x86"))
                        Logger.Debug("Detected x86 runtime: {DisplayName}", ndpKey.OpenSubKey(item).GetValue("DisplayName"));
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Unable to fetch x86 runtime");
            }
        }
    }

    public static string GetMD5hash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = MD5.HashData(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static void DownloadProgressCallback(object sender, DownloadProgressChangedEventArgs e)
    {
        // Displays the operation identifier, and the transfer progress.
        dialogue?.Dispatcher.Invoke(() => { dialogue.UpdatePercentage(e.ProgressPercentage); });
    }

    private static async Task RefreshTesseractDlls(CancellationToken token)
    {
        using var webClient = CreateNewWebClient();
        webClient.DownloadProgressChanged += DownloadProgressCallback;
        token.Register(webClient.CancelAsync);

        for (var i = 0; i < ListOfDlls.Length; i++)
        {
            if (token.IsCancellationRequested)
                break;
            var dll = ListOfDlls[i];
            var path = app_data_tesseract_catalog + dll;
            var md5 = ListOfChecksums[i];
            if (!File.Exists(path) || GetMD5hash(path) != md5)
            {
                if (File.Exists(path))
                    File.Delete(path);

                if (token.IsCancellationRequested)
                    break;
                var success = false;
                try
                {
                    if (Directory.Exists("lib"))
                    {
                        var file = $"lib{dll}";
                        if (File.Exists(file))
                        {
                            File.Copy(file, app_data_tesseract_catalog + dll);
                            success = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error moving dll. file={Dll}", dll);
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
                    catch (Exception e) when (stopDownloadTask.Token.IsCancellationRequested)
                    {
                        Logger.Error(e, "Download canceled. file={Dll}", dll);
                    }
                }

                dialogue?.Dispatcher.Invoke(() => { dialogue.FileComplete(); });
            }
        }

        dialogue?.Dispatcher.Invoke(() => { dialogue.Close(); });
    }

    private static Assembly CurrentDomain_AssemblyResolve_Tesseract(object sender, ResolveEventArgs args)
    {
        var probingPath = Path.Combine(appPath, tesseract_version_folder);
        var assyName = new AssemblyName(args.Name).Name;

        if (assyName is null)
            return null;

        var newPath = Path.Combine(probingPath, assyName);
        if (!newPath.EndsWith(".dll"))
            newPath += ".dll";

        if (File.Exists(newPath))
            return Assembly.Load(newPath);

        return null;
    }

    // From: https://docs.microsoft.com/en-us/dotnet/framework/migration-guide/how-to-determine-which-versions-are-installed

    private static Assembly OnResolveAssembly(object sender, ResolveEventArgs args)
    {
        var executingAssembly = Assembly.GetExecutingAssembly();
        var assemblyName = new AssemblyName(args.Name);

        var path = assemblyName.Name + ".dll";
        if (assemblyName.CultureInfo.Equals(CultureInfo.InvariantCulture) == false)
            path = $@"{assemblyName.CultureInfo}\{path}";

        using var stream = executingAssembly.GetManifestResourceStream(path);
        if (stream == null)
            return null;

        var assemblyRawBytes = new byte[stream.Length];
        stream.Read(assemblyRawBytes, 0, assemblyRawBytes.Length);
        return Assembly.Load(assemblyRawBytes);
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