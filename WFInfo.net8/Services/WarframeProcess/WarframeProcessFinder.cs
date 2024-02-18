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
    private const string ProcessName = "Warframe.x64";

    private static readonly ILogger Logger = Log.Logger.ForContext<WarframeProcessFinder>();

    public Process? Warframe { get; private set; }

    public HandleRef HandleRef => IsRunning() ? new HandleRef(Warframe, Warframe!.MainWindowHandle) : new HandleRef();
    public bool GameIsStreamed => Warframe?.MainWindowTitle.Contains("GeForce NOW") ?? false;
    public event ProcessChangedArgs? OnProcessChanged;

    public bool IsRunning()
    {
        if (Warframe is null || Warframe.HasExited)
        {
            Warframe = Process.GetProcessesByName(ProcessName).FirstOrDefault();
            if (Warframe is null)
                return false;
        }

        return Warframe is { HasExited: false };
    }

    private async Task FindProcess()
    {
        // Check if process is already found
        await CheckWarframeAlreadyRunning();

        foreach (var process in Process.GetProcesses())
        {
            if (process is { ProcessName: "Warframe.x64", MainWindowTitle: "Warframe" })
            {
                Warframe = process;

                var socketIsAlive = await mediator.Send(new WebSocketAliveStatusRequest(DateTime.UtcNow));

                if (socketIsAlive.IsAlive)
                    Logger.Debug("Socket was open in verify warframe");

                await mediator.Publish(new WebSocketSetStatus("in game"));
                Logger.Debug("Found Warframe Process: ID - {Id}, MainTitle - {Title}, Process Name - {Name}", process.Id, process.MainWindowTitle, process.ProcessName);

                //try and catch any UAC related issues
                try
                {
                    _ = Warframe.HasExited;
                }
                catch (System.ComponentModel.Win32Exception e)
                {
                    Warframe = null;

                    Logger.Error(e, "Failed to get Warframe process");

                    await mediator.Publish(new UpdateStatus("Restart Warframe without admin privileges", StatusSeverity.Error));

                    // Substitute process for debug purposes
                    if (settings.Debug)
                    {
                        Logger.Debug("Substituting Warframe process with WFInfo process for debug purposes");
                        Warframe = Process.GetCurrentProcess();
                    }
                }

                OnProcessChanged?.Invoke(Warframe);
                return;
            }

            if (!process.MainWindowTitle.Contains("Warframe") || !process.MainWindowTitle.Contains("GeForce NOW"))
                continue;

            await mediator.Publish(new GnfWarningShow(true));
            Logger.Debug(
                "GFN -- Found Warframe Process: ID - {ProcessId}, MainTitle - {MainTitle}, Process Name - {ProcessName}",
                process.Id, process.MainWindowTitle, process.ProcessName);

            Warframe = process;

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
                Warframe = Process.GetCurrentProcess();
            }

            OnProcessChanged?.Invoke(Warframe);
        }
    }

    private async ValueTask CheckWarframeAlreadyRunning()
    {
        if (Warframe is null)
            return;

        // Current process is still good
        if (!Warframe.HasExited)
            return;

        // Reset warframe process variables, and reset LogCapture so new game process gets noticed
        await mediator.Publish(new LogCaptureState(false));
        Warframe.Dispose();
        Warframe = null;

        if (settings.Auto)
            await mediator.Publish(new LogCaptureState(true));
    }

    private async Task CheckUacDiscrepancy()
    {
        try
        {
            _ = Warframe.HasExited;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Warframe = null;

            Logger.Error(e, "Failed to get Warframe process");
            await mediator.Publish(new UpdateStatus("Restart Warframe without admin privileges, or WFInfo with admin privileges", StatusSeverity.Error));

            // Substitute process for debug purposes
            if (settings.Debug)
            {
                Logger.Debug("Substituting Warframe process with WFInfo process for debug purposes");
                Warframe = Process.GetCurrentProcess();
            }

            if (Warframe is null)
                return;
        }

        OnProcessChanged?.Invoke(Warframe);
    }
}
