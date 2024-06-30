﻿using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Serilog;
using Tesseract;
using WFInfo.Extensions;
using WFInfo.Services;
using ILogger = Serilog.ILogger;

namespace WFInfo;

public sealed class CustomEntrypoint
{
    private static readonly ILogger Logger = Log.Logger.ForContext<CustomEntrypoint>();

    private const string liblept = "leptonica-1.82.0";
    private const string libtesseract = "tesseract50";
    private const string tesseract_version_folder = "tesseract5";

    private const string libs_hotlink_prefix = "https://raw.githubusercontent.com/WFCD/WFinfo/libs";
    private const string tesseract_hotlink_prefix = libs_hotlink_prefix + "/" + libtesseract + "/";

    private static string[] ListOfDlls =>
    [
        @"\x86\" + libtesseract + ".dll",
        @"\x86\" + liblept + ".dll",
        @"\x64\" + libtesseract + ".dll",
        @"\x64\" + liblept + ".dll",
        @"\Tesseract.dll"
    ];

    private static string[] ListOfChecksums =>
    [
        "a87ba6ac613b8ecb5ed033e57b871e6f", //  x86/tesseract50
        "e62f9ef3dd31df439fa2a37793b035db", //  x86/leptonica-1.82.0
        "446370b590a3c14e0fda0a2029b8e6fa", //  x64/tesseract50
        "2813455700fb7c1bc09738ca56ae7da7", //  x64/leptonica-1.82.0
        "528d4d1eb0e07cfe1370b592da6f49fd"  //  Tesseract
    ];

    private static string tesseract_hotlink_platform_specific_prefix;
    private static string app_data_tesseract_catalog => Path.Combine(ApplicationConstants.AppPath, tesseract_version_folder);

    public static string appdata_tessdata_folder => Path.Combine(ApplicationConstants.AppPath, "tessdata");

    private static InitialDialogue? _dialogue;

    public static CancellationTokenSource StopDownloadTask { get; private set; }

    public static async Task Run(AppDomain currentDomain, IServiceProvider sp)
    {
        currentDomain.UnhandledException += MyHandler;

        Logger.Information("Starting WFInfo V{Version}", ApplicationConstants.BuildVersion);
        Directory.CreateDirectory(ApplicationConstants.AppPath);
        Directory.CreateDirectory(ApplicationConstants.AppPathDebug);

        if (DetectInstance())
            return;

        CreateRequiredDirectories();
        CleanLegacyTesseractIfNeeded();
        CollectDebugInfo();

        tesseract_hotlink_platform_specific_prefix = tesseract_hotlink_prefix;

        // Refresh trained data structure
        // This is temporary, to be removed in half year from now
        if (File.Exists(Path.Combine(appdata_tessdata_folder, "engbest.traineddata")))
        {
            // To avoid conflicts for folks who like to experiment...
            if (File.Exists(Path.Combine(appdata_tessdata_folder, "en.traineddata")))
                File.Delete(Path.Combine(appdata_tessdata_folder, "en.traineddata"));

            File.Move(Path.Combine(appdata_tessdata_folder, "engbest.traineddata"), Path.Combine(appdata_tessdata_folder, "en.traineddata"));
        }

        await EnsureRequiredFilesExists(sp);

        if (StopDownloadTask is not { IsCancellationRequested: true })
        {
            currentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve_Tesseract;
            currentDomain.AssemblyResolve += OnResolveAssembly;
            TesseractEnviornment.CustomSearchPath = app_data_tesseract_catalog;
        }
    }

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
            if (!File.Exists(pathToCheck))
                continue;

            Logger.Debug("Cleaning legacy leftover. file={File}", legacyDdlName);
            File.Delete(pathToCheck);
        }
    }

    private static async Task EnsureRequiredFilesExists(IServiceProvider sp)
    {
        var hasher = sp.GetRequiredService<IHasherService>();

        var filesNeeded = 0;
        for (var i = 0; i < ListOfDlls.Length; i++)
        {
            var path = $"{app_data_tesseract_catalog}{ListOfDlls[i]}";

            if (!File.Exists(path))
            {
                filesNeeded++;
                continue;
            }

            var md5 = ListOfChecksums[i];

            if (hasher.GetMD5hash(path) != md5)
                filesNeeded++;
        }

        if (filesNeeded <= 0)
            return;

        StopDownloadTask = new CancellationTokenSource();
        _dialogue = sp.GetRequiredService<InitialDialogue>();
        _dialogue.SetFilesNeed(filesNeeded);

        try
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("WFInfo");
            await RefreshTesseractDlls(httpClient, hasher, StopDownloadTask.Token);
        }
        catch (Exception ex)
        {
            if (StopDownloadTask.IsCancellationRequested)
                _dialogue.Dispatcher.Invoke(() =>
                {
                    _dialogue.Close();
                });
            else
                Logger.Error(ex, "Error during initial load");
        }

        _dialogue.ShowDialog();
    }

    private static void CreateRequiredDirectories()
    {
        Directory.CreateDirectory(ApplicationConstants.AppPath);
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

        Logger.Debug("Duplicate process found - start canceled. version={Version}", ApplicationConstants.BuildVersion);

        var caption = $"WFInfo V{ApplicationConstants.BuildVersion}";
        MessageBox.Show("Another instance of WFInfo is already running, close it and try again", caption);

        return true;
    }

    private static void MyHandler(object sender, UnhandledExceptionEventArgs args)
    {
        var e = (Exception)args.ExceptionObject;
        Logger.Error(e, "Unhandled exception. isTerminating={IsTerminating}", args.IsTerminating);
    }

    private static void CollectDebugInfo()
    {
        Logger.Debug(
            "-- [info] -> -------------------------------------------------------------------------------------------------------------------------------");

        try
        {
            var mos = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_Processor");
            var managementObjects = mos.Get().OfType<ManagementObject>();
            foreach (var mo in managementObjects)
                Logger.Debug("CPU model is {Name}", mo["Name"]);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Unable to fetch CPU model");
        }

        Logger.Debug("Detected Windows version: {OS}", Environment.OSVersion);
        Logger.Debug("64-bit application: {Is64BitProcess}", Environment.Is64BitProcess);
        Logger.Debug(".NET version: {Version}", Environment.Version);
        Logger.Debug("Hardware acceleration enabled: {Hw}", Vector.IsHardwareAccelerated);

        //Log C++ x64 runtimes 14.29
        using (var ndpKey = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry32)
                                       .OpenSubKey("Installer\\Dependencies"))
        {
            try
            {
                // VC,redist.x64,amd64,14.30,bundle
                foreach (var item in ndpKey.GetSubKeyNames())
                {
                    if (item.Contains("VC,redist.x64,amd64"))
                        Logger.Debug("Detected x64 runtime: {DisplayName}",
                            ndpKey.OpenSubKey(item).GetValue("DisplayName"));
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
                // VC,redist.x86,x86,14.30,bundle
                foreach (var item in ndpKey.GetSubKeyNames())
                {
                    if (item.Contains("VC,redist.x86,x86"))
                        Logger.Debug("Detected x86 runtime: {DisplayName}",
                            ndpKey.OpenSubKey(item).GetValue("DisplayName"));
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Unable to fetch x86 runtime");
            }
        }

        Logger.Debug(
            "-- <- [info] -------------------------------------------------------------------------------------------------------------------------------");
    }

    private static void DownloadProgressCallback(object sender, DownloadProgressChangedEventArgs e)
    {
        // Displays the operation identifier, and the transfer progress.
        _dialogue?.Dispatcher.Invoke(() =>
        {
            _dialogue.UpdatePercentage(e.ProgressPercentage);
        });
    }

    private static async Task RefreshTesseractDlls(
        HttpClient httpClient,
        IHasherService hasherService,
        CancellationToken token)
    {
        for (var i = 0; i < ListOfDlls.Length; i++)
        {
            if (token.IsCancellationRequested)
                break;
            var dll = ListOfDlls[i];
            var path = app_data_tesseract_catalog + dll;
            var md5 = ListOfChecksums[i];
            var fileExists = File.Exists(path);

            switch (fileExists)
            {
                case true when hasherService.GetMD5hash(path) == md5:
                    continue;
                case true:
                    File.Delete(path);
                    break;
            }

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
                        var destination = Path.Combine(app_data_tesseract_catalog, dll);
                        File.Copy(file, destination);
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
                        var url = tesseract_hotlink_platform_specific_prefix + dll.Replace("\\", "/");
                        var file = app_data_tesseract_catalog + dll;
                        await httpClient.DownloadFile(url, file);
                    }
                    else
                    {
                        var url = tesseract_hotlink_prefix + dll.Replace("\\", "/");
                        var file = app_data_tesseract_catalog + dll;
                        await httpClient.DownloadFile(url, file);
                    }
                }
                catch (Exception e) when (StopDownloadTask.Token.IsCancellationRequested)
                {
                    Logger.Error(e, "Download canceled. file={Dll}", dll);
                }
            }

            _dialogue?.Dispatcher.Invoke(() =>
            {
                _dialogue.FileComplete();
            });
        }

        _dialogue?.Dispatcher.Invoke(() =>
        {
            _dialogue.Close();
        });
    }

    private static Assembly CurrentDomain_AssemblyResolve_Tesseract(object sender, ResolveEventArgs args)
    {
        var probingPath = Path.Combine(ApplicationConstants.AppPath, tesseract_version_folder);
        var assyName = new AssemblyName(args.Name).Name;

        if (assyName is null)
            return null!;

        var newPath = Path.Combine(probingPath, assyName);

        if (!Path.HasExtension(newPath))
            newPath += ".dll";

        return File.Exists(newPath) ? Assembly.Load(newPath) : null!;
    }

    // From: https://docs.microsoft.com/en-us/dotnet/framework/migration-guide/how-to-determine-which-versions-are-installed

    private static Assembly OnResolveAssembly(object sender, ResolveEventArgs args)
    {
        var executingAssembly = Assembly.GetExecutingAssembly();
        var assemblyName = new AssemblyName(args.Name);

        var path = $"{assemblyName.Name}.dll";
        if (!assemblyName.CultureInfo!.Equals(CultureInfo.InvariantCulture))
            path = $@"{assemblyName.CultureInfo}\{path}";

        using var stream = executingAssembly.GetManifestResourceStream(path);
        if (stream is null)
            return null!;

        var assemblyRawBytes = new byte[stream.Length];
        var read = stream.Read(assemblyRawBytes, 0, assemblyRawBytes.Length);

        return read != assemblyRawBytes.Length ? null! : Assembly.Load(assemblyRawBytes);
    }
}