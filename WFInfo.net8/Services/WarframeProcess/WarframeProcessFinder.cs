using System.Diagnostics;
using System.Runtime.InteropServices;
using Mediator;
using Serilog;
using WFInfo.Domain;
using WFInfo.Settings;

namespace WFInfo.Services.WarframeProcess;

public sealed class WarframeProcessFinder(
    ApplicationSettings settings,
    IMediator mediator)
    : IProcessFinder
{
    private static readonly ILogger Logger = Log.Logger.ForContext<WarframeProcessFinder>();

    private Process? _warframe;

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

    private async Task FindProcess()
    {
        // Check if process is already found
        await CheckWarframeAlreadyRunning();

        foreach (var process in Process.GetProcesses())
        {
            if (process is { ProcessName: "Warframe.x64", MainWindowTitle: "Warframe" })
            {
                _warframe = process;

                var socketIsAlive = await mediator.Send(new WebSocketAliveStatusRequest(DateTime.UtcNow));

                if (socketIsAlive.IsAlive)
                    Logger.Debug("Socket was open in verify warframe");

                await mediator.Publish(new WebSocketSetStatus("in game"));
                Logger.Debug("Found Warframe Process: ID - {Id}, MainTitle - {Title}, Process Name - {Name}", process.Id, process.MainWindowTitle, process.ProcessName);

                //try and catch any UAC related issues
                try
                {
                    _ = _warframe.HasExited;
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
                }

                OnProcessChanged?.Invoke(_warframe);
                return;
            }

            if (!process.MainWindowTitle.Contains("Warframe") || !process.MainWindowTitle.Contains("GeForce NOW"))
                continue;

            await mediator.Publish(new GnfWarningShow(true));
            Logger.Debug(
                "GFN -- Found Warframe Process: ID - {ProcessId}, MainTitle - {MainTitle}, Process Name - {ProcessName}",
                process.Id, process.MainWindowTitle, process.ProcessName);

            _warframe = process;

            // Try and catch any UAC related issues
            await CheckUacDiscrepancy();
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

    private async ValueTask CheckWarframeAlreadyRunning()
    {
        if (_warframe is null)
            return;

        // Current process is still good
        if (!_warframe.HasExited)
            return;

        // Reset warframe process variables, and reset LogCapture so new game process gets noticed
        await mediator.Publish(new LogCaptureState(false));
        _warframe.Dispose();
        _warframe = null;

        if (settings.Auto)
            await mediator.Publish(new LogCaptureState(true));
    }

    private async Task CheckUacDiscrepancy()
    {
        try
        {
            _ = _warframe.HasExited;
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
        }

        OnProcessChanged?.Invoke(_warframe);
    }
}
