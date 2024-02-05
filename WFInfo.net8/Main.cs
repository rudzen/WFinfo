using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Input;
using System.Text.RegularExpressions;
using AutoUpdaterDotNET;
using System.Windows;
using System.Windows.Forms;
using WebSocketSharp;
using WFInfo.Settings;
using WFInfo.Services.WarframeProcess;
using WFInfo.Services.WindowInfo;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WFInfo.Services.OpticalCharacterRecognition;

namespace WFInfo;

public class Main
{
    private enum ScreenshotType
    {
        NORMAL,
        SNAPIT,
        MASTERIT
    }

    private static readonly ILogger Logger = Log.ForContext<Main>();

    private static readonly TimeSpan TimeTillAfk = TimeSpan.FromMinutes(7);

    public static Main INSTANCE { get; private set; }

    public static Data DataBase { get; private set; }
    public static RewardWindow Window { get; set; } = new RewardWindow();
    public static SettingsWindow SettingsWindow { get; private set; }
    public static AutoCount AutoCount { get; set; } = new AutoCount();
    public static ErrorDialogue ErrorDialogue { get; set; }
    public static FullscreenReminder FullscreenReminder { get; set; }
    public static GFNWarning GfnWarning { get; set; }
    public static UpdateDialogue Update { get; set; }
    public static SnapItOverlay SnapItOverlayWindow { get; private set; }
    public static SearchIt SearchBox { get; set; } = new SearchIt();
    public static Login Login { get; set; }
    public static ListingHelper ListingHelper { get; set; } = new ListingHelper();
    public static string BuildVersion { get; }

    // Glob
    public static CultureInfo Culture { get; } = new("en", false);

    private static bool UserAway { get; set; }
    private static string LastMarketStatus { get; set; } = "invisible";
    private static string LastMarketStatusB4AFK { get; set; } = "invisible";

    private DateTime _latestActive;
    
    // ReSharper disable once NotAccessedField.Local
    private System.Threading.Timer _timer;

    // Instance services
    private readonly ApplicationSettings _settings;
    private readonly IProcessFinder _process;
    private readonly IWindowInfoService _windowInfo;
    private readonly IEncryptedDataService _encryptedDataService;

    private readonly Overlay[] _overlays;

    // hack, should not be here, but stuff is too intertwined for now
    // also, the auto updater needs this to allow for event to be used
    private readonly IServiceProvider _sp;

    static Main()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        var splitIndex = version.LastIndexOf('.');
        BuildVersion = version[..splitIndex];
    }

    public Main(IServiceProvider sp)
    {
        _sp = sp;
        INSTANCE = this;
        Login = sp.GetRequiredService<Login>();
        SettingsWindow = sp.GetRequiredService<SettingsWindow>();

        _settings = sp.GetRequiredService<ApplicationSettings>();
        _process = sp.GetRequiredService<IProcessFinder>();
        _windowInfo = sp.GetRequiredService<IWindowInfoService>();
        _encryptedDataService = sp.GetRequiredService<IEncryptedDataService>();

        DataBase = sp.GetRequiredService<Data>();
        SnapItOverlayWindow = new SnapItOverlay(_windowInfo);

        _overlays = [new(_settings), new(_settings), new(_settings), new(_settings)];

        StartMessage();

        AutoUpdater.CheckForUpdateEvent += AutoUpdaterOnCheckForUpdateEvent;
        AutoUpdater.Start("https://github.com/WFCD/WFinfo/releases/latest/download/update.xml");

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
            // Too many dependencies?
            StatusUpdate("Initializing OCR engine...", 0);
            OCR.Init(_sp, _overlays);

            StatusUpdate("Updating Databases...", 0);
            DataBase.Update();

            if (_settings.Auto)
                DataBase.EnableLogCapture();
            
            var validJwt = await DataBase.IsJWTvalid();
            if (validJwt)
                LoggedIn();

            StatusUpdate("WFInfo Initialization Complete", 0);
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
            Logger.Error(ex, "Failed to initialize WFInfo");
            StatusUpdate(
                ex.ToString().Contains("invalid_grant")
                    ? "System time out of sync with server\nResync system clock in windows settings"
                    : "Launch Failure - Please Restart", 0);
            RunOnUIThread(() => { _ = new ErrorDialogue(DateTime.Now, 0); });
        }
    }

    private async void TimeoutCheck()
    {
        if (!await DataBase.IsJWTvalid().ConfigureAwait(true) || _process.GameIsStreamed)
            return;

        DateTime now = DateTime.UtcNow;
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
                StatusUpdate("WFM status set offline, Warframe was closed", 0);
            }).ConfigureAwait(false);
        }
        else if (UserAway && _latestActive > now)
        {
            Logger.Debug("User has returned. Last Status was: {LastMarketStatusB4AFK}", LastMarketStatusB4AFK);

            UserAway = false;
            if (LastMarketStatusB4AFK != "invisible")
            {
                await Task.Run(async () =>
                {
                    await DataBase.SetWebsocketStatus(LastMarketStatusB4AFK).ConfigureAwait(false);
                    var user = DataBase.inGameName.IsNullOrEmpty() ? "user" : DataBase.inGameName;
                    StatusUpdate($"Welcome back {user}, restored as {LastMarketStatusB4AFK}", 0);
                }).ConfigureAwait(false);
            }
            else
                StatusUpdate($"Welcome back user", 0);
        }
        else if (!UserAway && _latestActive <= now)
        {
            //set users offline if afk for longer than set timer
            LastMarketStatusB4AFK = LastMarketStatus;
            Debug.WriteLine($"User is now away - Storing last known user status as: {LastMarketStatusB4AFK}");

            UserAway = true;
            if (LastMarketStatus != "invisible")
            {
                await Task.Run(async () =>
                {
                    await DataBase.SetWebsocketStatus("invisible").ConfigureAwait(false);
                    StatusUpdate($"User has been inactive for {TimeTillAfk} minutes", 0);
                }).ConfigureAwait(false);
            }
        }
        else
        {
            if (UserAway)
                Logger.Debug("User is away - no status change needed.  Last known status was: {LastMarketStatusB4AFK}",
                    LastMarketStatusB4AFK);
            else
                Logger.Debug("User is active - no status change needed");
        }
    }

    public static void RunOnUIThread(Action act)
    {
        MainWindow.INSTANCE.Dispatcher.Invoke(act);
    }

    private static void StartMessage()
    {
        Directory.CreateDirectory(ApplicationConstants.AppPath);
        Directory.CreateDirectory(Path.Combine(ApplicationConstants.AppPath, "debug"));
        using var sw = File.AppendText($@"{ApplicationConstants.AppPath}\debug.log");
        sw.WriteLineAsync(
            "--------------------------------------------------------------------------------------------------------------------------------------------");
        sw.WriteLineAsync("   STARTING WFINFO " + BuildVersion + " at " + DateTime.UtcNow);
        sw.WriteLineAsync(
            "--------------------------------------------------------------------------------------------------------------------------------------------");
    }

    /// <summary>
    /// Sets the status on the main window
    /// </summary>
    /// <param name="message">The string to be displayed</param>
    /// <param name="severity">0 = normal, 1 = red, 2 = orange, 3 =yellow</param>
    public static void StatusUpdate(string message, int severity)
    {
        MainWindow.INSTANCE.Dispatcher.Invoke(() => { MainWindow.INSTANCE.ChangeStatus(message, severity); });
    }

    private void ActivationKeyPressed(object key)
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
                // TODO (rudzen) : this is a hack, we should not be checking for the type of the window

                // if (overlay.GetType() == typeof(Overlay))

                if (overlay.GetType().ToString() == "WFInfo.Overlay")
                {
                    overlay.Hide();
                }
            }

            StatusUpdate("Overlays dismissed", 1);
            return;
        }

        if (_settings.Debug && Keyboard.IsKeyDown(_settings.DebugModifierKey) &&
            Keyboard.IsKeyDown(_settings.SnapitModifierKey))
        {
            //snapit debug
            Logger.Information("Loading screenshot from file for snapit");
            StatusUpdate("Offline testing with screenshot for snapit", 0);
            LoadScreenshot(ScreenshotType.SNAPIT);
        }
        else if (_settings.Debug && Keyboard.IsKeyDown(_settings.DebugModifierKey) &&
                 Keyboard.IsKeyDown(_settings.MasterItModifierKey))
        {
            //master debug
            Logger.Information("Loading screenshot from file for masterit");
            StatusUpdate("Offline testing with screenshot for masterit", 0);
            LoadScreenshot(ScreenshotType.MASTERIT);
        }
        else if (_settings.Debug && Keyboard.IsKeyDown(_settings.DebugModifierKey))
        {
            //normal debug
            Logger.Information("Loading screenshot from file");
            StatusUpdate("Offline testing with screenshot", 0);
            LoadScreenshot(ScreenshotType.NORMAL);
        }
        else if (Keyboard.IsKeyDown(_settings.SnapitModifierKey))
        {
            //snapit
            Logger.Information("Starting snap it");
            StatusUpdate("Starting snap it", 0);
            OCR.SnapScreenshot();
        }
        else if (Keyboard.IsKeyDown(_settings.SearchItModifierKey))
        {
            //Searchit  
            Logger.Information("Starting search it");
            StatusUpdate("Starting search it", 0);
            SearchBox.Start(() => _encryptedDataService.IsJwtLoggedIn());
        }
        else if (Keyboard.IsKeyDown(_settings.MasterItModifierKey))
        {
            //masterit
            Logger.Information("Starting master it");
            StatusUpdate("Starting master it", 0);
            Task.Factory.StartNew(() =>
            {
                using Bitmap bigScreenshot = OCR.CaptureScreenshot();
                OCR.ProcessProfileScreen(bigScreenshot);
            });
        }
        else if (_settings.Debug || _process.IsRunning)
        {
            Task.Factory.StartNew(() => OCR.ProcessRewardScreen());
        }
    }

    public void OnMouseAction(MouseButton key)
    {
        _latestActive = DateTime.UtcNow.Add(TimeTillAfk);

        if (_settings.ActivationMouseButton != null && key == _settings.ActivationMouseButton)
        {
            //check if user pressed activation key
            if (SearchBox.IsInUse)
            {
                //if key is pressed and searchbox is active then rederect keystokes to it.
                if (Keyboard.IsKeyDown(Key.Escape))
                {
                    // close it if esc is used.
                    SearchBox.Finish();
                    return;
                }

                SearchBox.searchField.Focus();
                return;
            }

            ActivationKeyPressed(key);
        }
        else if (key == MouseButton.Left
                 && _process is { Warframe.HasExited: false, GameIsStreamed: false }
                 && Overlay.rewardsDisplaying)
        {
            if (_settings.Display != Display.Overlay
                && _settings is { AutoList: false, AutoCSV: false, AutoCount: false })
            {
                //only "naturally" set to false on overlay disappearing and/or specific log message with auto-list enabled
                Overlay.rewardsDisplaying = false;
                return;
            }

            Task.Run(() =>
            {
                var lastClick = System.Windows.Forms.Cursor.Position;
                int index = OCR.GetSelectedReward(lastClick);
                Logger.Debug("Reward chosen. index={Index}", index);
                if (index < 0)
                    return;
                ListingHelper.SelectedRewardIndex = (short)index;
            });
        }
    }

    public void OnKeyAction(Key key)
    {
        _latestActive = DateTime.UtcNow.Add(TimeTillAfk);

        // close the snapit overlay when *any* key is pressed down
        if (SnapItOverlayWindow.isEnabled && KeyInterop.KeyFromVirtualKey((int)key) != Key.None)
        {
            SnapItOverlayWindow.CloseOverlay();
            StatusUpdate("Closed snapit", 0);
            return;
        }

        if (SearchBox.IsInUse)
        {
            //if key is pressed and searchbox is active then rederect keystokes to it.
            if (key == Key.Escape)
            {
                // close it if esc is used.
                SearchBox.Finish();
                return;
            }

            SearchBox.searchField.Focus();
            return;
        }


        if (key == _settings.ActivationKeyKey)
        {
            //check if user pressed activation key

            ActivationKeyPressed(key);
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

    private void LoadScreenshot(ScreenshotType type)
    {
        // Using WinForms for the openFileDialog because it's simpler and much easier
        using OpenFileDialog openFileDialog = new OpenFileDialog();
        openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        openFileDialog.Filter = "image files (*.png)|*.png|All files (*.*)|*.*";
        openFileDialog.FilterIndex = 2;
        openFileDialog.RestoreDirectory = true;
        openFileDialog.Multiselect = true;

        if (openFileDialog.ShowDialog() == DialogResult.OK)
        {
            Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        // TODO: This
                        foreach (string file in openFileDialog.FileNames)
                        {
                            switch (type)
                            {
                                case ScreenshotType.NORMAL:
                                {
                                    Logger.Debug("Testing file. name={File}", file);

                                    //Get the path of specified file
                                    Bitmap image = new Bitmap(file);
                                    _windowInfo.UseImage(image);
                                    OCR.ProcessRewardScreen(image);
                                    break;
                                }
                                case ScreenshotType.SNAPIT:
                                {
                                    Logger.Debug("Testing snapit on file. name={File}", file);

                                    Bitmap image = new Bitmap(file);
                                    _windowInfo.UseImage(image);
                                    OCR.ProcessSnapIt(image, image, new System.Drawing.Point(0, 0));
                                    break;
                                }
                                case ScreenshotType.MASTERIT:
                                {
                                    Logger.Debug("Testing masterit on file. name={File}", file);

                                    Bitmap image = new Bitmap(file);
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
                        StatusUpdate("Failed to load image", 1);
                    }
                });
        }
        else
        {
            StatusUpdate("Failed to load image", 1);
            if (type == ScreenshotType.NORMAL)
            {
                OCR.processingActive = false;
            }
        }
    }

    // Switch to logged in mode for warfrane.market systems
    public void LoggedIn()
    {
        //this is bullshit, but I couldn't call it in login.xaml.cs because it doesn't properly get to the main window
        MainWindow.INSTANCE.Dispatcher.Invoke(() => { MainWindow.INSTANCE.LoggedIn(); });

        // start the AFK timer
        _latestActive = DateTime.UtcNow.AddMinutes(1);
        TimeSpan startTimeSpan = TimeSpan.Zero;
        TimeSpan periodTimeSpan = TimeSpan.FromMinutes(1);

        _timer = new System.Threading.Timer((e) => { TimeoutCheck(); }, null, startTimeSpan, periodTimeSpan);
    }

    private static void FinishedLoading()
    {
        MainWindow.INSTANCE.Dispatcher.Invoke(() => { MainWindow.INSTANCE.FinishedLoading(); });
    }

    public static void UpdateMarketStatus(string msg)
    {
        Debug.WriteLine($"New market status received: {msg}");
        if (!UserAway)
        {
            // AFK system only cares about a status that the user set
            LastMarketStatus = msg;
            Logger.Debug("User is not away. last known market status will be: {LastMarketStatus}", LastMarketStatus);
        }

        MainWindow.INSTANCE.Dispatcher.Invoke(() => { MainWindow.INSTANCE.UpdateMarketStatus(msg); });
    }

    public static int VersionToInteger(string vers)
    {
        int ret = 0;
        string[] versParts = Regex.Replace(vers, "[^0-9.]+", "").Split('.');
        if (versParts.Length == 3)
            for (int i = 0; i < versParts.Length; i++)
            {
                if (versParts[i].Length == 0)
                    return -1;
                ret += Convert.ToInt32(int.Parse(versParts[i], Main.Culture) * Math.Pow(100, 2 - i));
            }

        return ret;
    }

    public static void SignOut()
    {
        MainWindow.INSTANCE.Dispatcher.Invoke(() => { MainWindow.INSTANCE.SignOut(); });
    }
}