using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mediator;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Serilog;
using WFInfo.Domain;
using WFInfo.Domain.Types;
using WFInfo.Extensions;
using WFInfo.Services;
using WFInfo.Services.OpticalCharacterRecognition;
using WFInfo.Settings;

namespace WFInfo;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow
    : Window,
        INotificationHandler<DataUpdatedAt>,
        INotificationHandler<UpdateStatus>,
        INotificationHandler<LoggedIn>,
        INotificationHandler<WarframeMarketStatusUpdate>,
        INotificationHandler<WarframeMarketSignOut>
{
    private static readonly ILogger Logger = Log.Logger.ForContext<MainWindow>();

    private static readonly SolidColorBrush[] StatusBrushes =
    [
        new SolidColorBrush(Color.FromRgb(177, 208, 217)),
        Brushes.Red,
        Brushes.Orange
    ];

    //subscriber
    private Main Main { get; set; }

    public static MainWindow INSTANCE { get; set; }
    public static WelcomeDialogue welcomeDialogue { get; set; }
    private bool updatesupression;

    private readonly IServiceProvider _sp;
    private readonly ApplicationSettings _applicationSettings;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly PlusOne _plusOne;

    private readonly RelicsWindow _relicsWindow;
    private readonly EquipmentWindow _equipmentWindow;

    private readonly IEncryptedDataService _encryptedDataService;
    private readonly ILowLevelListener _lowLevelListener;
    private readonly IMediator _mediator;
    private readonly IOCR _ocr;

    public MainWindow(
        ApplicationSettings applicationSettings,
        SettingsViewModel settingsViewModel,
        PlusOne plusOne,
        RelicsWindow relicsWindow,
        EquipmentWindow equipmentWindow,
        IEncryptedDataService encryptedDataService,
        IServiceProvider sp,
        ILowLevelListener lowLevelListener,
        Main main,
        IMediator mediator,
        IOCR ocr)
    {
        _applicationSettings = applicationSettings;
        _sp = sp;
        INSTANCE = this;
        Main = main;
        _mediator = mediator;
        _ocr = ocr;

        _settingsViewModel = settingsViewModel;
        _plusOne = plusOne;

        _relicsWindow = relicsWindow;
        _equipmentWindow = equipmentWindow;

        _encryptedDataService = encryptedDataService;

        // publisher
        _lowLevelListener = lowLevelListener;

        try
        {
            InitializeSettings();

            LowLevelListener.KeyEvent += Main.OnKeyAction;
            LowLevelListener.MouseEvent += Main.OnMouseAction;
            _lowLevelListener.Hook();
            InitializeComponent();
            Version.Content = $"v{ApplicationConstants.MajorBuildVersion}";

            Left = 300;
            Top = 300;

            var winBounds = new System.Drawing.Rectangle(
                Convert.ToInt32(_settingsViewModel.MainWindowLocation.X),
                Convert.ToInt32(_settingsViewModel.MainWindowLocation.Y), Convert.ToInt32(Width),
                Convert.ToInt32(Height));

            if (Array.Exists(System.Windows.Forms.Screen.AllScreens, scr => scr.Bounds.Contains(winBounds)))
            {
                Left = _settingsViewModel.MainWindowLocation.X;
                Top = _settingsViewModel.MainWindowLocation.Y;
            }

            _settingsViewModel.MainWindowLocation = new Point(Left, Top);
            _settingsViewModel.Save();

            Closing += LoggOut;
        }
        catch (Exception e)
        {
            Logger.Error(e, "An error occured while loading the main window");
        }

        Application.Current.MainWindow = this;
    }

    private void InitializeSettings()
    {
        // TODO (rudzen) : Move this to Pre-startup
        var jsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        jsonSettings.Converters.Add(new StringEnumConverter());
        var settingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WFInfo",
            "settings.json");

        if (File.Exists(settingsFile) && !_applicationSettings.Initialized)
        {
            var jsonText = File.ReadAllText(settingsFile);
            JsonConvert.PopulateObject(jsonText, _applicationSettings, jsonSettings);
        }
        else
        {
            welcomeDialogue = new WelcomeDialogue();
        }

        _applicationSettings.Initialized = true;

        if (!Enum.TryParse(typeof(Key), _settingsViewModel.ActivationKey, out var _)
            && !Enum.TryParse(typeof(MouseButton), _settingsViewModel.ActivationKey, out var _))
        {
            Logger.Debug("Couldn't Parse Activation Key -- Defaulting to PrintScreen");
            _settingsViewModel.ActivationKey = "Snapshot";
        }

        _settingsViewModel.Save();
    }

    private void OnContentRendered(object sender, EventArgs e)
    {
        if (welcomeDialogue != null)
        {
            welcomeDialogue.Left = Left + Width + 30;
            welcomeDialogue.Top = Top + Height / 2 - welcomeDialogue.Height / 2;
            welcomeDialogue.Show();
        }
    }

    /// <summary>
    /// Sets the status
    /// </summary>
    /// <param name="status">The string to be displayed</param>
    /// <param name="severity">0 = normal, 1 = red, 2 = orange, 3 =yellow</param>
    public void ChangeStatus(string status, StatusSeverity severity = StatusSeverity.None)
    {
        if (Status is null)
            return;

        Logger.Debug("Status. message={Msg}", status);
        Status.Text = status;

        Status.Foreground = severity is >= StatusSeverity.None and <= StatusSeverity.Warning
            ? StatusBrushes[(int)severity]
            : Brushes.Yellow;
    }

    private void Minimise(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Hidden;
    }

    private void WebsiteClick(object sender, RoutedEventArgs e)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "https://discord.gg/TAq7bqr",
            UseShellExecute = true
        };

        Process.Start(processInfo);
    }

    private void RelicsClick(object sender, RoutedEventArgs e)
    {
        if (Main.DataBase.RelicData == null)
        {
            ChangeStatus("Relic data not yet loaded in", StatusSeverity.Warning);
            return;
        }

        _relicsWindow.Show();
        _relicsWindow.Focus();
    }

    private void EquipmentClick(object sender, RoutedEventArgs e)
    {
        if (Main.DataBase.EquipmentData == null)
        {
            ChangeStatus("Equipment data not yet loaded in", StatusSeverity.Warning);
            return;
        }

        _equipmentWindow.Show();
    }

    private void Settings_click(object sender, RoutedEventArgs e)
    {
        Main.SettingsWindow.Populate();
        Main.SettingsWindow.Left = Left;
        Main.SettingsWindow.Top = Top + Height;
        Main.SettingsWindow.Show();
    }

    private void ReloadMarketClick(object sender, RoutedEventArgs e)
    {
        ReloadDrop.IsEnabled = false;
        ReloadMarket.IsEnabled = false;
        MarketData.Content = "Loading...";
        Dispatcher.InvokeAsync(() => ChangeStatus("Forcing Market Update", 0));
        Main.DataBase.ForceMarketUpdate();
    }

    private void ReloadDropClick(object sender, RoutedEventArgs e)
    {
        ReloadDrop.IsEnabled = false;
        ReloadMarket.IsEnabled = false;
        DropData.Content = "Loading...";
        Dispatcher.InvokeAsync(() => ChangeStatus("Forcing Prime Update", 0));
        Task.Run(async () => await Main.DataBase.ForceEquipmentUpdate());
    }

    // Allows the dragging of the window
    private new void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e is not { ChangedButton: MouseButton.Left, LeftButton: MouseButtonState.Pressed })
            return;

        try
        {
            DragMove();
        }
        catch (Exception)
        {
            Logger.Debug("Error in Mouse down in mainwindow");
        }
    }

    private void OnLocationChanged(object sender, EventArgs e)
    {
        _settingsViewModel.MainWindowLocation = new Point(Left, Top);
        _settingsViewModel.Save();
    }

    public void ToForeground(object sender, RoutedEventArgs e)
    {
        INSTANCE.Visibility = Visibility.Visible;
        INSTANCE.Activate();
        INSTANCE.Topmost = true;  // important
        INSTANCE.Topmost = false; // important
        INSTANCE.Focus();         // important
    }

    public void LoggedIn()
    {
        Login.Visibility = Visibility.Collapsed;
        ComboBox.SelectedIndex = 1;
        ComboBox.Visibility = Visibility.Visible;
        PlusOneButton.Visibility = Visibility.Visible;
        CreateListing.Visibility = Visibility.Visible;
        SearchItButton.Visibility = Visibility.Visible;
        ChangeStatus("Logged in", 0);
    }

    /// <summary>
    /// Prompts user to log in
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void SpawnLogin(object sender, RoutedEventArgs e)
    {
        Main.Login.MoveLogin(Left + Width, Top);
    }

    public void SignOut()
    {
        Login.Visibility = Visibility.Visible;
        ComboBox.Visibility = Visibility.Collapsed;
        PlusOneButton.Visibility = Visibility.Collapsed;
        CreateListing.Visibility = Visibility.Collapsed;
        SearchItButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Changes the online selector. Used for websocket lisening to see if the status changed externally (i.e from the site)
    /// </summary>
    /// <param name="status">The status to change to</param>
    private void UpdateMarketStatus(string status)
    {
        updatesupression = true;
        var selectionIndex = status switch
        {
            "online"    => 1,
            "invisible" => 2,
            "ingame"    => 0,
            var _       => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };

        if (ComboBox.SelectedIndex != selectionIndex)
            ComboBox.SelectedIndex = selectionIndex;

        updatesupression = false;
    }

    /// <summary>
    /// Allows the user to overwrite the current websocket status
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void ComboBoxOnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Prevent firing off to early
        if (!ComboBox.IsLoaded || updatesupression)
            return;

        switch (ComboBox.SelectedIndex)
        {
            case 0: //Online in game
                await _mediator.Publish(new WebSocketSetStatus("in game"));
                break;
            case 1: //Online
                await _mediator.Publish(new WebSocketSetStatus("online"));
                break;
            case 2: //Invisible
                await _mediator.Publish(new WebSocketSetStatus("offline"));
                break;
            case 3: //Sign out
                LoggOut(null, null);
                //delete the jwt token if user logs out
                var jwtFile = Path.Combine(ApplicationConstants.AppPath, "jwt_encrypted");
                if (File.Exists(jwtFile))
                    File.Delete(jwtFile);

                break;
        }
    }

    internal void LoggOut(object sender, CancelEventArgs e)
    {
        Login.Visibility = Visibility.Visible;
        ComboBox.Visibility = Visibility.Hidden;
        PlusOneButton.Visibility = Visibility.Hidden;
        CreateListing.Visibility = Visibility.Hidden;
        Task.Run(() =>
        {
            Main.DataBase.Disconnect();
        });
    }

    internal void FinishedLoading()
    {
        Login.IsEnabled = true;
    }

    private void CreateListing_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_ocr.ProcessingActive)
        {
            Dispatcher.InvokeAsync(() => ChangeStatus("Still Processing Reward Screen", StatusSeverity.Warning));
            return;
        }

        if (Main.ListingHelper.PrimeRewards.Count == 0)
        {
            Dispatcher.InvokeAsync(() => ChangeStatus("No recorded rewards found", StatusSeverity.Warning));
            return;
        }

        var t = Task.Run(async () =>
        {
            foreach (var rewardscreen in Main.ListingHelper.PrimeRewards)
            {
                var rewardCollection = await Main.ListingHelper.GetRewardCollection(rewardscreen);
                if (rewardCollection.PrimeNames.Count == 0)
                    continue;
                Main.ListingHelper.ScreensList.Add(new RewardCollectionItem(string.Empty, rewardCollection));
            }
        });
        t.Wait();
        if (Main.ListingHelper.ScreensList.Count == 0)
        {
            ChangeStatus("No recorded rewards found", StatusSeverity.Warning);
            return;
        }

        Main.ListingHelper.SetScreen(0);
        Main.ListingHelper.PrimeRewards.Clear();
        WindowState = WindowState.Normal;
        Main.ListingHelper.Show();
    }

    private void PlusOne(object sender, MouseButtonEventArgs e)
    {
        _plusOne.Show();
        _plusOne.Left = Left + Width;
        _plusOne.Top = Top;
    }

    private void SearchItButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_ocr.ProcessingActive)
        {
            Dispatcher.InvokeAsync(() => ChangeStatus("Still Processing Reward Screen", StatusSeverity.Warning));
            return;
        }

        Logger.Debug("Starting search it");
        Dispatcher.InvokeAsync(() => ChangeStatus("Starting search it", 0));
        Main.SearchIt.Start(() => _encryptedDataService.IsJwtLoggedIn());
    }

    private void OpenAppDataFolder(object sender, MouseButtonEventArgs e)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = ApplicationConstants.AppPath,
            UseShellExecute = true
        };

        Process.Start(processInfo);
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        NotifyIcon.Dispose();
        _lowLevelListener.Dispose();

        if (Main.DataBase.RememberMe)
        {
            // if rememberme was checked then save it
            _encryptedDataService.PersistJWT();
        }

        Application.Current.Shutdown();
    }

    public ValueTask Handle(DataUpdatedAt dataUpdatedAt, CancellationToken cancellationToken)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (dataUpdatedAt.Type.Contains(DataTypes.MarketData))
            {
                DropData.Content = dataUpdatedAt.Date;
                MarketData.Content = dataUpdatedAt.Date;
            }
            else
            {
                DropData.Content = dataUpdatedAt.Date;
            }

            ReloadDrop.IsEnabled = true;
            ReloadMarket.IsEnabled = true;
        });

        return ValueTask.CompletedTask;
    }

    public ValueTask Handle(UpdateStatus updateStatus, CancellationToken cancellationToken)
    {
        Dispatcher.InvokeAsyncIfRequired(() => ChangeStatus(updateStatus.Message, updateStatus.Severity));
        return ValueTask.CompletedTask;
    }

    public ValueTask Handle(LoggedIn loggedIn, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(loggedIn.Email))
            Dispatcher.InvokeAsyncIfRequired(() => ChangeStatus("User logged in to warframe.market"));
        else
            Dispatcher.InvokeAsyncIfRequired(() => ChangeStatus($"User [{loggedIn.Email}] logged in to warframe.market"));
        return ValueTask.CompletedTask;
    }

    public ValueTask Handle(WarframeMarketStatusUpdate warframeMarketStatusUpdate, CancellationToken cancellationToken)
    {
        Dispatcher.InvokeAsyncIfRequired(() => UpdateMarketStatus(warframeMarketStatusUpdate.Message));
        return ValueTask.CompletedTask;
    }

    public ValueTask Handle(WarframeMarketSignOut notification, CancellationToken cancellationToken)
    {
        Dispatcher.InvokeAsyncIfRequired(SignOut);
        return ValueTask.CompletedTask;
    }
}