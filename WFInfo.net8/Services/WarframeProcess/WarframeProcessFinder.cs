using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;
using WFInfo.Settings;

namespace WFInfo.Services.WarframeProcess;

public sealed class WarframeProcessFinder : IProcessFinder
{
    private static readonly ILogger Logger = Log.Logger.ForContext<WarframeProcessFinder>();

    public Process? Warframe
    {
        get
        {
            FindProcess();
            return _warframe;
        }
    }

    public HandleRef HandleRef => IsRunning ? new HandleRef(Warframe, Warframe.MainWindowHandle) : new HandleRef();
    public bool IsRunning => Warframe is { HasExited: false };
    public bool GameIsStreamed => Warframe?.MainWindowTitle.Contains("GeForce NOW") ?? false;
    public event ProcessChangedArgs OnProcessChanged;

    private Process? _warframe;

    private readonly ApplicationSettings _settings;

    public WarframeProcessFinder(ApplicationSettings settings)
    {
        _settings = settings;
    }

    private void FindProcess()
    {
        // process already found
        if (_warframe != null)
        {
            if (_warframe.HasExited)
            {
                //reset warframe process variables, and reset LogCapture so new game process gets noticed
                Main.DataBase.DisableLogCapture();
                _warframe.Dispose();
                _warframe = null;

                if (_settings.Auto)
                    Main.DataBase.EnableLogCapture();
            }
            else return; // Current process is still good
        }

        foreach (Process process in Process.GetProcesses())
        {
            if (process is { ProcessName: "Warframe.x64", MainWindowTitle: "Warframe" })
            {
                _warframe = process;

                if (Main.DataBase.GetSocketAliveStatus())
                    Debug.WriteLine("Socket was open in verify warframe");
                Task.Run(async () => { await Main.DataBase.SetWebsocketStatus("in game"); });
                Logger.Debug("Found Warframe Process: ID - " + process.Id + ", MainTitle - " + process.MainWindowTitle +
                            ", Process Name - "             + process.ProcessName);

                //try and catch any UAC related issues
                try
                {
                    _ = _warframe.HasExited;
                    OnProcessChanged?.Invoke(_warframe);
                    return;
                }
                catch (System.ComponentModel.Win32Exception e)
                {
                    _warframe = null;

                    Logger.Error(e, "Failed to get Warframe process");
                    Main.StatusUpdate("Restart Warframe without admin privileges", 1);

                    // Substitute process for debug purposes
                    if (_settings.Debug)
                    {
                        Logger.Debug("Substituting Warframe process with WFInfo process for debug purposes");
                        _warframe = Process.GetCurrentProcess();
                    }

                    OnProcessChanged?.Invoke(_warframe);
                    return;
                }
            }
            else if (process.MainWindowTitle.Contains("Warframe") && process.MainWindowTitle.Contains("GeForce NOW"))
            {
                Main.RunOnUIThread(Main.SpawnGFNWarning);
                Logger.Debug("GFN -- Found Warframe Process: ID - " + process.Id          + ", MainTitle - " +
                            process.MainWindowTitle                + ", Process Name - " + process.ProcessName);

                _warframe = process;

                //try and catch any UAC related issues
                try
                {
                    bool _ = _warframe.HasExited;
                    OnProcessChanged?.Invoke(_warframe);
                    return;
                }
                catch (System.ComponentModel.Win32Exception e)
                {
                    _warframe = null;

                    Logger.Error(e, "Failed to get Warframe process");
                    Main.StatusUpdate("Restart Warframe without admin privileges, or WFInfo with admin privileges", 1);

                    // Substitute process for debug purposes
                    if (_settings.Debug)
                    {
                        Logger.Debug("Substituting Warframe process with WFInfo process for debug purposes");
                        _warframe = Process.GetCurrentProcess();
                    }

                    if (_warframe is null)
                        return;

                    OnProcessChanged?.Invoke(_warframe);
                    return;
                }
            }
        }

        if (!_settings.Debug)
        {
            Logger.Debug("Didn't detect Warframe process");
            Main.StatusUpdate("Unable to Detect Warframe Process", 1);
        }
        else
        {
            // Substitute process for debug purposes
            if (_settings.Debug)
            {
                Logger.Debug("Substituting Warframe process with WFInfo process for debug purposes");
                _warframe = Process.GetCurrentProcess();
            }

            OnProcessChanged?.Invoke(_warframe);
        }
    }
}