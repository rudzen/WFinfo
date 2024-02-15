using System.Diagnostics;
using System.Runtime.InteropServices;
using Mediator;
using Serilog;
using WFInfo.Domain;
using WFInfo.Settings;

namespace WFInfo.Services.WarframeProcess;

public sealed class WarframeProcessFinder(ApplicationSettings settings, IPublisher mediator) : IProcessFinder
{
    private static readonly ILogger Logger = Log.Logger.ForContext<WarframeProcessFinder>();

    public Process? Warframe
    {
        get
        {
            FindProcess().GetAwaiter().GetResult(); // shit
            return _warframe;
        }
    }

    public HandleRef HandleRef => IsRunning ? new HandleRef(Warframe, Warframe.MainWindowHandle) : new HandleRef();
    public bool IsRunning => Warframe is { HasExited: false };
    public bool GameIsStreamed => Warframe?.MainWindowTitle.Contains("GeForce NOW") ?? false;
    public event ProcessChangedArgs? OnProcessChanged;

    private Process? _warframe;

    private async Task FindProcess()
    {
        // process already found
        if (_warframe != null)
        {
            // Current process is still good
            if (!_warframe.HasExited)
                return;

            //reset warframe process variables, and reset LogCapture so new game process gets noticed
            Main.DataBase.DisableLogCapture();
            _warframe.Dispose();
            _warframe = null;

            if (settings.Auto)
                Main.DataBase.EnableLogCapture();
        }

        foreach (var process in Process.GetProcesses())
        {
            if (process is { ProcessName: "Warframe.x64", MainWindowTitle: "Warframe" })
            {
                _warframe = process;

                if (Main.DataBase.GetSocketAliveStatus())
                    Logger.Debug("Socket was open in verify warframe");

                await Main.DataBase.SetWebsocketStatus("in game");
                Logger.Debug("Found Warframe Process: ID - {Id}, MainTitle - {Title}, Process Name - {Name}", process.Id, process.MainWindowTitle, process.ProcessName);

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

                    await mediator.Publish(new UpdateStatus("Restart Warframe without admin privileges", StatusSeverity.Error));

                    // Substitute process for debug purposes
                    if (settings.Debug)
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
                Logger.Debug(
                    "GFN -- Found Warframe Process: ID - {ProcessId}, MainTitle - {MainTitle}, Process Name - {ProcessName}",
                    process.Id, process.MainWindowTitle, process.ProcessName);

                _warframe = process;

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
                    await mediator.Publish(new UpdateStatus("Restart Warframe without admin privileges, or WFInfo with admin privileges", StatusSeverity.Error));

                    // Substitute process for debug purposes
                    if (settings.Debug)
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

        if (!settings.Debug)
        {
            Logger.Debug("Didn't detect Warframe process");
            await mediator.Publish(new UpdateStatus("Unable to Detect Warframe Process", StatusSeverity.Error));
        }
        else
        {
            // Substitute process for debug purposes
            if (settings.Debug)
            {
                Logger.Debug("Substituting Warframe process with WFInfo process for debug purposes");
                _warframe = Process.GetCurrentProcess();
            }

            OnProcessChanged?.Invoke(_warframe);
        }
    }
}
