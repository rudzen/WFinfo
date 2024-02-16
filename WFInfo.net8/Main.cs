using System.Drawing;
using System.IO;
using System.Windows.Input;
using AutoUpdaterDotNET;
using System.Windows;
using System.Windows.Forms;
using Mediator;
using WebSocketSharp;
using WFInfo.Settings;
using WFInfo.Services.WarframeProcess;
using WFInfo.Services.WindowInfo;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WFInfo.Domain;
using WFInfo.Extensions;
using WFInfo.Services;
using WFInfo.Services.OpticalCharacterRecognition;
using Application = System.Windows.Application;

namespace WFInfo;

public class Main
    : INotificationHandler<StartLoggedInTimer>,
        INotificationHandler<OverlayUpdate>,
        INotificationHandler<OverlayUpdateData>
{
    private enum ScreenshotType
    {
        NORMAL,
        SNAPIT,
        MASTERIT
    }

    private static readonly ILogger Logger = Log.ForContext<Main>();

    private static readonly TimeSpan TimeTillAfk = TimeSpan.FromMinutes(7);

    public static Data DataBase { get; private set; }
    public static RewardWindow RewardWindow { get; set; }
    public static SettingsWindow SettingsWindow { get; private set; }
    public static AutoCount AutoCount { get; set; }
    public static ErrorDialogue ErrorDialogue { get; set; }
    public static FullscreenReminder FullscreenReminder { get; set; }
    public static GFNWarning GfnWarning { get; set; }
    public static UpdateDialogue Update { get; set; }
    public static SnapItOverlay SnapItOverlayWindow { get; private set; }
    public static SearchIt SearchIt { get; set; }
    public static Login Login { get; set; }
    public static ListingHelper ListingHelper { get; set; } = new ListingHelper();
    private static bool UserAway { get; set; }
    private static string LastMarketStatus { get; set; } = "invisible";
    private static string LastMarketStatusB4AFK { get; set; } = "invisible";

    public static Overlay[] Overlays { get; set; }

    private DateTime _latestActive;

    // ReSharper disable once NotAccessedField.Local
    private System.Threading.Timer _timer;

    // Instance services
    private readonly ApplicationSettings _settings;
    private readonly IProcessFinder _process;
    private readonly IWindowInfoService _windowInfo;
    private readonly IEncryptedDataService _encryptedDataService;
    private readonly IRewardSelector _rewardSelector;
    private readonly IMediator _mediator;

    // hack, should not be here, but stuff is too intertwined for now
    // also, the auto updater needs this to allow for event to be used
    private readonly IServiceProvider _sp;

    public Main(IServiceProvider sp)
    {
        _sp = sp;
        Login = sp.GetRequiredService<Login>();

        _settings = sp.GetRequiredService<ApplicationSettings>();
        _process = sp.GetRequiredService<IProcessFinder>();
        _windowInfo = sp.GetRequiredService<IWindowInfoService>();
        _encryptedDataService = sp.GetRequiredService<IEncryptedDataService>();
        _rewardSelector = sp.GetRequiredService<IRewardSelector>();
        _mediator = sp.GetRequiredService<IMediator>();

        DataBase = sp.GetRequiredService<Data>();
        RewardWindow = sp.GetRequiredService<RewardWindow>();
        SettingsWindow = sp.GetRequiredService<SettingsWindow>();
        AutoCount = sp.GetRequiredService<AutoCount>();
        SearchIt = sp.GetRequiredService<SearchIt>();

        Application.Current.Dispatcher.InvokeIfRequired(() =>
        {
            SnapItOverlayWindow = new SnapItOverlay(_windowInfo);
            Overlays = [new(_settings), new(_settings), new(_settings), new(_settings)];

            AutoUpdater.CheckForUpdateEvent += AutoUpdaterOnCheckForUpdateEvent;
            AutoUpdater.Start("https://github.com/WFCD/WFinfo/releases/latest/download/update.xml");
        });

        StartMessage();

        Task.Factory.StartNew(ThreadedDataLoad);
    }

    private void AutoUpdaterOnCheckForUpdateEvent(UpdateInfoEventArgs args)
    {
        Update = new UpdateDialogue(args, _sp);
    }

    private async Task ThreadedDataLoad()
    {
        try
        {
            await _mediator.Publish(new UpdateStatus("Initializing OCR engine...", 0));
            OCR.Init(_sp, Overlays);

            await _mediator.Publish(new UpdateStatus("Updating Databases...", 0));
            await DataBase.Update();

            if (_settings.Auto)
                DataBase.EnableLogCapture();

            var validJwt = await DataBase.IsJWTvalid();
            if (validJwt)
                await Handle(new StartLoggedInTimer(string.Empty), CancellationToken.None);

            await _mediator.Publish(new UpdateStatus("WFInfo Initialization Complete", 0));
            Logger.Debug("WFInfo has launched successfully");
            FinishedLoading();

            if (_encryptedDataService.JWT != null) // if token is loaded in, connect to websocket
            {
                var result = await DataBase.OpenWebSocket();
                Logger.Debug("Logging into websocket success: {Result}", result);
            }
        }
        catch (Exception ex)
        {
            var message = ex.ToString().Contains("invalid_grant")
                ? "System time out of sync with server\nResync system clock in windows settings"
                : "Launch Failure - Please Restart";
            Logger.Error(ex, "Failed to initialize WFInfo. Message: {Message}", message);
            await _mediator.Publish(new UpdateStatus(message, StatusSeverity.Error));
            RunOnUIThread(() =>
            {
                _ = new ErrorDialogue(DateTime.Now, 0);
            });
        }
    }

    private async void TimeoutCheck()
    {
        if (!await DataBase.IsJWTvalid().ConfigureAwait(true) || _process.GameIsStreamed)
            return;

        var now = DateTime.UtcNow;
        Logger.Debug("Checking if the user has been inactive. Now={Now}, lastActive={LastActive}", now, _latestActive);

        if (!_process.IsRunning && LastMarketStatus != "invisible")
        {
            //set user offline if Warframe has closed but no new game was found
            Logger.Debug("Warframe was detected as closed");
            //reset warframe process variables, and reset LogCapture so new game process gets noticed
            DataBase.DisableLogCapture();
            if (_settings.Auto)
                DataBase.EnableLogCapture();

            await Task.Run(async () =>
            {
                if (!await DataBase.IsJWTvalid().ConfigureAwait(true))
                    return;
                //IDE0058 - computed value is never used.  Ever. Consider changing the return signature of SetWebsocketStatus to void instead
                await DataBase.SetWebsocketStatus("invisible").ConfigureAwait(false);
                await _mediator.Publish(new UpdateStatus("WFM status set offline, Warframe was closed", 0));
            }).ConfigureAwait(false);
        }
        else
            switch (UserAway)
            {
                case true when _latestActive > now:
                {
                    Logger.Debug("User has returned. Last Status was: {LastMarketStatusB4AFK}", LastMarketStatusB4AFK);

                    UserAway = false;
                    if (LastMarketStatusB4AFK != "invisible")
                    {
                        await DataBase.SetWebsocketStatus(LastMarketStatusB4AFK).ConfigureAwait(ConfigureAwaitOptions.None);
                        var user = DataBase.inGameName.IsNullOrEmpty() ? "user" : DataBase.inGameName;
                        await _mediator.Publish(new UpdateStatus($"Welcome back {user}, restored as {LastMarketStatusB4AFK}", 0));
                    }
                    else
                    {
                        await _mediator.Publish(new UpdateStatus("Welcome back user", 0));
                    }

                    break;
                }
                case false when _latestActive <= now:
                {
                    //set users offline if afk for longer than set timer
                    LastMarketStatusB4AFK = LastMarketStatus;
                    Logger.Debug("User is now away - Storing last known user status as {Status}", LastMarketStatusB4AFK);

                    UserAway = true;
                    if (LastMarketStatus != "invisible")
                    {
                        await Task.Run(async () =>
                        {
                            await DataBase.SetWebsocketStatus("invisible").ConfigureAwait(false);
                            await _mediator.Publish(new UpdateStatus($"User has been inactive for {TimeTillAfk} minutes", 0));
                        }).ConfigureAwait(ConfigureAwaitOptions.None);
                    }

                    break;
                }
                case true:
                    Logger.Debug("User is away - no status change needed.  Last known status was: {LastMarketStatusB4AFK}",
                        LastMarketStatusB4AFK);
                    break;
                default:
                    Logger.Debug("User is active - no status change needed");
                    break;
            }
    }

    public static void RunOnUIThread(Action act)
    {
        MainWindow.INSTANCE.Dispatcher.Invoke(act);
    }

    private static void StartMessage()
    {
        Directory.CreateDirectory(ApplicationConstants.AppPath);
        Directory.CreateDirectory(ApplicationConstants.AppPathDebug);
        Logger.Debug("   STARTING WFINFO {BuildVersion} at {DateTime}", ApplicationConstants.MajorBuildVersion, DateTime.UtcNow);
    }

    /// <summary>
    /// Sets the status on the main window
    /// </summary>
    /// <param name="message">The string to be displayed</param>
    /// <param name="severity">0 = normal, 1 = red, 2 = orange, 3 =yellow</param>
    public static void StatusUpdate(string message, StatusSeverity severity)
    {
        MainWindow.INSTANCE.Dispatcher.Invoke(() =>
        {
            MainWindow.INSTANCE.ChangeStatus(message, severity);
        });
    }

    private async Task ActivationKeyPressed(object key)
    {
        Logger.Information(
            "User key press. key={Key},delete={Delete},snapit={SnapitKey}:{SnapPressed},searchit={SearchitKey}:{SearchItPressed},masterit={MasteritKey}:{MasteritPressed},debug={DebugKey}:{DebugPressed}",
            key,
            Keyboard.IsKeyDown(Key.Delete),
            _settings.SnapitModifierKey, Keyboard.IsKeyDown(_settings.SnapitModifierKey),
            _settings.SearchItModifierKey, Keyboard.IsKeyDown(_settings.SearchItModifierKey),
            _settings.MasterItModifierKey, Keyboard.IsKeyDown(_settings.MasterItModifierKey),
            _settings.DebugModifierKey, Keyboard.IsKeyDown(_settings.DebugModifierKey)
        );

        if (Keyboard.IsKeyDown(Key.Delete))
        {
            //Close all overlays if hotkey + delete is held down
            foreach (Window overlay in App.Current.Windows)
            {
                // TODO (rudzen) : this is a hack, we should not be checking for the type NAME of the window

                if (overlay.GetType().ToString() == "WFInfo.Overlay")
                {
                    overlay.Hide();
                }
            }

            await _mediator.Publish(new UpdateStatus("Overlays dismissed", StatusSeverity.Error));
            return;
        }

        if (_settings.Debug && Keyboard.IsKeyDown(_settings.DebugModifierKey) &&
            Keyboard.IsKeyDown(_settings.SnapitModifierKey))
        {
            //snapit debug
            Logger.Information("Loading screenshot from file for snapit");
            await _mediator.Publish(new UpdateStatus("Offline testing with screenshot for snapit", 0));
            await LoadScreenshot(ScreenshotType.SNAPIT);
        }
        else if (_settings.Debug && Keyboard.IsKeyDown(_settings.DebugModifierKey) &&
                 Keyboard.IsKeyDown(_settings.MasterItModifierKey))
        {
            //master debug
            Logger.Information("Loading screenshot from file for masterit");
            await _mediator.Publish(new UpdateStatus("Offline testing with screenshot for masterit", 0));
            await LoadScreenshot(ScreenshotType.MASTERIT);
        }
        else if (_settings.Debug && Keyboard.IsKeyDown(_settings.DebugModifierKey))
        {
            //normal debug
            Logger.Information("Loading screenshot from file");
            await _mediator.Publish(new UpdateStatus("Offline testing with screenshot", 0));
            await LoadScreenshot(ScreenshotType.NORMAL);
        }
        else if (Keyboard.IsKeyDown(_settings.SnapitModifierKey))
        {
            // Snap-it
            Logger.Information("Starting snap it");
            await _mediator.Publish(new UpdateStatus("Starting snap it"));
            await OCR.SnapScreenshot();
        }
        else if (Keyboard.IsKeyDown(_settings.SearchItModifierKey))
        {
            //Searchit
            Logger.Information("Starting search it");
            await _mediator.Publish(new UpdateStatus("Starting search it", 0));
            SearchIt.Start(() => _encryptedDataService.IsJwtLoggedIn());
        }
        else if (Keyboard.IsKeyDown(_settings.MasterItModifierKey))
        {
            //masterit
            Logger.Information("Starting master it");
            await _mediator.Publish(new UpdateStatus("Starting master it", 0));
            using var bigScreenshot = await OCR.CaptureScreenshot();
            OCR.ProcessProfileScreen(bigScreenshot);
        }
        else if (_settings.Debug || _process.IsRunning)
        {
            await OCR.ProcessRewardScreen();
        }
    }

    public async void OnMouseAction(MouseButton key)
    {
        _latestActive = DateTime.UtcNow.Add(TimeTillAfk);

        if (_settings.ActivationMouseButton != null && key == _settings.ActivationMouseButton)
        {
            //check if user pressed activation key
            if (SearchIt.IsInUse)
            {
                //if key is pressed and searchbox is active then rederect keystokes to it.
                if (Keyboard.IsKeyDown(Key.Escape))
                {
                    // close it if esc is used.
                    SearchIt.Finish();
                    return;
                }

                SearchIt.searchField.Focus();
                return;
            }

            await ActivationKeyPressed(key);
        }
        else if (key == MouseButton.Left
                 && Overlay.RewardsDisplaying
                 && _process is { Warframe.HasExited: false, GameIsStreamed: false })
        {
            if (_settings.Display != Display.Overlay
                && _settings is { AutoList: false, AutoCSV: false, AutoCount: false })
            {
                //only "naturally" set to false on overlay disappearing and/or specific log message with auto-list enabled
                Overlay.RewardsDisplaying = false;
                return;
            }

            await Task.Run(() =>
            {
                var lastClick = System.Windows.Forms.Cursor.Position;
                var uiScaling = OCR.UiScaling;
                var displayed = OCR.NumberOfRewardsDisplayed;
                var index = _rewardSelector.GetSelectedReward(ref lastClick, in uiScaling, displayed);
                Logger.Debug("Reward chosen. index={Index}", index);
                if (index == -1)
                    return;
                ListingHelper.SelectedRewardIndex = (short)index;
            });
        }
    }

    public async void OnKeyAction(Key key)
    {
        _latestActive = DateTime.UtcNow.Add(TimeTillAfk);

        // close the snapit overlay when *any* key is pressed down
        if (SnapItOverlayWindow.isEnabled && KeyInterop.KeyFromVirtualKey((int)key) != Key.None)
        {
            SnapItOverlayWindow.CloseOverlay();
            await _mediator.Publish(new UpdateStatus("Closed snapit", 0));
            return;
        }

        if (SearchIt.IsInUse)
        {
            //if key is pressed and searchbox is active then rederect keystokes to it.
            if (key == Key.Escape)
            {
                // close it if esc is used.
                SearchIt.Finish();
                return;
            }

            SearchIt.searchField.Focus();
            return;
        }


        if (key == _settings.ActivationKeyKey)
        {
            //check if user pressed activation key

            await ActivationKeyPressed(key);
        }
    }

    // timestamp is the time to look for, and gap is the threshold of seconds different
    public static void SpawnErrorPopup(DateTime timeStamp, int gap = 30)
    {
        ErrorDialogue = new ErrorDialogue(timeStamp, gap);
    }

    public static void SpawnFullscreenReminder()
    {
        FullscreenReminder = new FullscreenReminder();
    }

    public static void SpawnGFNWarning()
    {
        GfnWarning = new GFNWarning();
    }

    private async Task LoadScreenshot(ScreenshotType type)
    {
        // Using WinForms for the openFileDialog because it's simpler and much easier
        using var openFileDialog = new OpenFileDialog();
        openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        openFileDialog.Filter = "image files (*.png)|*.png|All files (*.*)|*.*";
        openFileDialog.FilterIndex = 2;
        openFileDialog.RestoreDirectory = true;
        openFileDialog.Multiselect = true;

        if (openFileDialog.ShowDialog() == DialogResult.OK)
        {
            await Task.Run(async
                () =>
            {
                try
                {
                    // TODO: This
                    foreach (var file in openFileDialog.FileNames)
                    {
                        switch (type)
                        {
                            case ScreenshotType.NORMAL:
                            {
                                Logger.Debug("Testing file. name={File}", file);

                                //Get the path of specified file
                                var image = new Bitmap(file);
                                _windowInfo.UseImage(image);
                                await OCR.ProcessRewardScreen(image);
                                break;
                            }
                            case ScreenshotType.SNAPIT:
                            {
                                Logger.Debug("Testing snapit on file. name={File}", file);

                                var image = new Bitmap(file);
                                _windowInfo.UseImage(image);
                                await OCR.ProcessSnapIt(image, image, new System.Drawing.Point(0, 0));
                                break;
                            }
                            case ScreenshotType.MASTERIT:
                            {
                                Logger.Debug("Testing masterit on file. name={File}", file);

                                var image = new Bitmap(file);
                                _windowInfo.UseImage(image);
                                OCR.ProcessProfileScreen(image);
                                break;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Failed to load image");
                    await _mediator.Publish(new UpdateStatus("Failed to load image", StatusSeverity.Error));
                }
            });
        }
        else
        {
            await _mediator.Publish(new UpdateStatus("Failed to load image", StatusSeverity.Error));
            if (type == ScreenshotType.NORMAL)
            {
                OCR.ProcessingActive.GetAndSet(false);
            }
        }
    }

    private static void FinishedLoading()
    {
        MainWindow.INSTANCE.Dispatcher.Invoke(() =>
        {
            MainWindow.INSTANCE.FinishedLoading();
        });
    }

    public static void UpdateMarketStatus(string msg)
    {
        Logger.Debug("New market status received: {Msg}", msg);
        if (!UserAway)
        {
            // AFK system only cares about a status that the user set
            LastMarketStatus = msg;
            Logger.Debug("User is not away. last known market status will be: {LastMarketStatus}", LastMarketStatus);
        }

        MainWindow.INSTANCE.Dispatcher.Invoke(() =>
        {
            MainWindow.INSTANCE.UpdateMarketStatus(msg);
        });
    }

    public static void SignOut()
    {
        MainWindow.INSTANCE.Dispatcher.Invoke(() =>
        {
            MainWindow.INSTANCE.SignOut();
        });
    }

    /// <summary>
    /// Switch to logged in mode for warfrane.market systems
    /// </summary>
    /// <param name="startLoggedInTimer"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async ValueTask Handle(StartLoggedInTimer startLoggedInTimer, CancellationToken cancellationToken)
    {
        //this is bullshit, but I couldn't call it in login.xaml.cs because it doesn't properly get to the main window
        // MainWindow.INSTANCE.Dispatcher.Invoke(() => { MainWindow.INSTANCE.LoggedIn(); });

        // (rudzen). yes, it was bullshit. this should be fine however
        await _mediator.Publish(new LoggedIn(startLoggedInTimer.Email), cancellationToken);

        // start the AFK timer
        _latestActive = DateTime.UtcNow.AddMinutes(1);
        _timer = new System.Threading.Timer((e) =>
        {
            TimeoutCheck();
        }, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }

    public ValueTask Handle(OverlayUpdate overlayUpdate, CancellationToken cancellationToken)
    {
        Logger.Debug("Updating overlay. index={Index}, type={Type}", overlayUpdate.Index, overlayUpdate.Tyoe);
        Application.Current.Dispatcher.InvokeIfRequired(() =>
        {
            var overlay = Overlays[overlayUpdate.Index];
            if (overlayUpdate.Tyoe == OverlayUpdateType.Owned)
                overlay.BestOwnedChoice();
            else if (overlayUpdate.Tyoe == OverlayUpdateType.Ducat)
                overlay.BestDucatChoice();
            else if (overlayUpdate.Tyoe == OverlayUpdateType.Plat)
                overlay.BestPlatChoice();
        });

        return ValueTask.CompletedTask;
    }

    public ValueTask Handle(OverlayUpdateData overlayUpdateData, CancellationToken cancellationToken)
    {
        Application.Current.Dispatcher.InvokeIfRequired(() =>
        {
            Overlay.RewardsDisplaying = true;
            var overlay = Overlays[overlayUpdateData.Index];
            overlay.LoadTextData(
                name: overlayUpdateData.CorrectName,
                plat: overlayUpdateData.Plat,
                primeSetPlat: overlayUpdateData.PrimeSetPlat,
                ducats: overlayUpdateData.Ducats,
                volume: overlayUpdateData.Volume,
                vaulted: overlayUpdateData.Vaulted,
                mastered: overlayUpdateData.Mastered,
                owned: $"{overlayUpdateData.PartsOwned} / {overlayUpdateData.PartsCount}",
                detected: string.Empty,
                hideRewardInfo: overlayUpdateData.HideRewardInfo,
                showWarningTriangle: false
            );
            overlay.Resize(overlayUpdateData.OverWid);
            overlay.Display(
                x: overlayUpdateData.Position.X,
                y: overlayUpdateData.Position.Y,
                wait: _settings.Delay
            );
        });

        return ValueTask.CompletedTask;
    }
}
