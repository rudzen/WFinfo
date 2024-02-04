using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Serilog;
using WFInfo.Resources;
using WFInfo.Services.OpticalCharacterRecognition;
using WFInfo.Settings;

namespace WFInfo;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private static readonly ILogger Logger = Log.Logger.ForContext<MainWindow>();

    private static readonly SolidColorBrush[] StatusBrushes =
    [
        new(Color.FromRgb(177, 208, 217)),
        Brushes.Red,
        Brushes.Orange
    ];

    private Main Main { get; set; } //subscriber
    public static MainWindow INSTANCE { get; set; }
    public static WelcomeDialogue welcomeDialogue { get; set; }
    public static LowLevelListener listener { get; set; }
    private static bool updatesupression;

    private readonly IServiceProvider _sp;
    private readonly ApplicationSettings _applicationSettings;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly PlusOne _plusOne;

    private readonly RelicsWindow _relicsWindow;
    private readonly EquipmentWindow _equipmentWindow;

    private readonly IEncryptedDataService _encryptedDataService;
    
    public MainWindow(
        ApplicationSettings applicationSettings,
        SettingsViewModel settingsViewModel,
        PlusOne plusOne,
        RelicsWindow relicsWindow,
        EquipmentWindow equipmentWindow,
        IEncryptedDataService encryptedDataService,
        IServiceProvider sp)
    {
        _applicationSettings = applicationSettings;
        _sp = sp;
        INSTANCE = this;
        Main = new Main(sp);

        _settingsViewModel = settingsViewModel;
        _plusOne = plusOne;

        _relicsWindow = relicsWindow;
        _equipmentWindow = equipmentWindow;

        _encryptedDataService = encryptedDataService;
        
        listener = new LowLevelListener(); //publisher
        try
        {
            InitializeSettings();

            LowLevelListener.KeyEvent += Main.OnKeyAction;
            LowLevelListener.MouseEvent += Main.OnMouseAction;
            listener.Hook();
            InitializeComponent();
            Version.Content = "v" + Main.BuildVersion;

            Left = 300;
            Top = 300;

            System.Drawing.Rectangle winBounds = new System.Drawing.Rectangle(
                Convert.ToInt32(_settingsViewModel.MainWindowLocation.X),
                Convert.ToInt32(_settingsViewModel.MainWindowLocation.Y), Convert.ToInt32(Width),
                Convert.ToInt32(Height));
            foreach (System.Windows.Forms.Screen scr in System.Windows.Forms.Screen.AllScreens)
            {
                if (scr.Bounds.Contains(winBounds))
                {
                    Left = _settingsViewModel.MainWindowLocation.X;
                    Top = _settingsViewModel.MainWindowLocation.Y;
                    break;
                }
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

    public void InitializeSettings()
    {
        var jsonSettings = new JsonSerializerSettings()
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

        try
        {
            Enum.Parse(typeof(Key), _settingsViewModel.ActivationKey);
        }
        catch
        {
            try
            {
                Enum.Parse(typeof(MouseButton), _settingsViewModel.ActivationKey);
            }
            catch
            {
                Logger.Debug("Couldn't Parse Activation Key -- Defaulting to PrintScreen");
                _settingsViewModel.ActivationKey = "Snapshot";
            }
        }

        _settingsViewModel.Save();

        Main.dataBase.JWT = _encryptedDataService.LoadStoredJWT();
    }

    public void OnContentRendered(object sender, EventArgs e)
    {
        if (welcomeDialogue != null)
        {
            welcomeDialogue.Left = Left            + Width + 30;
            welcomeDialogue.Top = Top + Height / 2 - welcomeDialogue.Height / 2;
            welcomeDialogue.Show();
        }
    }

    /// <summary>
    /// Sets the status
    /// </summary>
    /// <param name="status">The string to be displayed</param>
    /// <param name="severity">0 = normal, 1 = red, 2 = orange, 3 =yellow</param>
    public void ChangeStatus(string status, int severity)
    {
        if (Status == null) return;
        Debug.WriteLine("Status message: " + status);
        Status.Text = status;

        Status.Foreground = severity is >= 0 and <= 2
            ? StatusBrushes[severity]
            : Brushes.Yellow;
    }

    public void Exit(object sender, RoutedEventArgs e)
    {
        NotifyIcon.Dispose();
        if (Main.dataBase.rememberMe)
        {
            // if rememberme was checked then save it
            _encryptedDataService.PersistJWT(Main.dataBase.JWT);
        }

        Application.Current.Shutdown();
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
        if (Main.dataBase.RelicData == null)
        {
            ChangeStatus("Relic data not yet loaded in", 2);
            return;
        }

        _relicsWindow.Show();
        _relicsWindow.Focus();
    }

    private void EquipmentClick(object sender, RoutedEventArgs e)
    {
        if (Main.dataBase.EquipmentData == null)
        {
            ChangeStatus("Equipment data not yet loaded in", 2);
            return;
        }

        _equipmentWindow.Show();
    }

    private void Settings_click(object sender, RoutedEventArgs e)
    {
        Main.settingsWindow.Populate();
        Main.settingsWindow.Left = Left;
        Main.settingsWindow.Top = Top + Height;
        Main.settingsWindow.Show();
    }

    private void ReloadMarketClick(object sender, RoutedEventArgs e)
    {
        ReloadDrop.IsEnabled = false;
        ReloadMarket.IsEnabled = false;
        MarketData.Content = "Loading...";
        Main.StatusUpdate("Forcing Market Update", 0);
        Task.Factory.StartNew(Main.dataBase.ForceMarketUpdate);
    }

    private void ReloadDropClick(object sender, RoutedEventArgs e)
    {
        ReloadDrop.IsEnabled = false;
        ReloadMarket.IsEnabled = false;
        DropData.Content = "Loading...";
        Main.StatusUpdate("Forcing Prime Update", 0);
        Task.Factory.StartNew(Main.dataBase.ForceEquipmentUpdate);
    }

    // Allows the draging of the window
    private new void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.LeftButton == MouseButtonState.Pressed)
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
        Main.login.MoveLogin(Left + Width, Top);
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
    public void UpdateMarketStatus(string status)
    {
        updatesupression = true;
        switch (status)
        {
            case "online":
                if (ComboBox.SelectedIndex == 1) break;
                ComboBox.SelectedIndex = 1;
                break;
            case "invisible":
                if (ComboBox.SelectedIndex == 2) break;
                ComboBox.SelectedIndex = 2;
                break;
            case "ingame":
                if (ComboBox.SelectedIndex == 0) break;
                ComboBox.SelectedIndex = 0;
                break;
        }

        updatesupression = false;
    }

    /// <summary>
    /// Allows the user to overwrite the current websocket status
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ComboBoxOnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ComboBox.IsLoaded || updatesupression) //Prevent firing off to early
            return;
        switch (ComboBox.SelectedIndex)
        {
            case 0: //Online in game
                Task.Run(async () => { await Main.dataBase.SetWebsocketStatus("in game"); });
                break;
            case 1: //Online
                Task.Run(async () => { await Main.dataBase.SetWebsocketStatus("online"); });
                break;
            case 2: //Invisible
                Task.Run(async () => { await Main.dataBase.SetWebsocketStatus("offline"); });
                break;
            case 3: //Sign out
                LoggOut(null, null);
                //delete the jwt token if user logs out
                if (File.Exists(Main.AppPath + @"\jwt_encrypted"))
                {
                    File.Delete(Main.AppPath + @"\jwt_encrypted");
                }

                break;
        }
    }

    internal void LoggOut(object sender, CancelEventArgs e)
    {
        Login.Visibility = Visibility.Visible;
        ComboBox.Visibility = Visibility.Hidden;
        PlusOneButton.Visibility = Visibility.Hidden;
        CreateListing.Visibility = Visibility.Hidden;
        Task.Factory.StartNew(() => { Main.dataBase.Disconnect(); });
    }

    internal void FinishedLoading()
    {
        Login.IsEnabled = true;
    }

    private void CreateListing_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (OCR.processingActive)
        {
            Main.StatusUpdate("Still Processing Reward Screen", 2);
            return;
        }

        if (Main.listingHelper.PrimeRewards == null || Main.listingHelper.PrimeRewards.Count == 0)
        {
            ChangeStatus("No recorded rewards found", 2);
            return;
        }

        var t = Task.Run(() =>
        {
            foreach (var rewardscreen in Main.listingHelper.PrimeRewards)
            {
                var rewardCollection = Task.Run(() => Main.listingHelper.GetRewardCollection(rewardscreen)).Result;
                if (rewardCollection.PrimeNames.Count == 0)
                    continue;
                Main.listingHelper.ScreensList.Add(new KeyValuePair<string, RewardCollection>("", rewardCollection));
            }
        });
        t.Wait();
        if (Main.listingHelper.ScreensList.Count == 0)
        {
            ChangeStatus("No recorded rewards found", 2);
            return;
        }

        Main.listingHelper.SetScreen(0);
        Main.listingHelper.PrimeRewards.Clear();
        WindowState = WindowState.Normal;
        Main.listingHelper.Show();
    }

    private void PlusOne(object sender, MouseButtonEventArgs e)
    {
        _plusOne.Show();
        _plusOne.Left = Left + Width;
        _plusOne.Top = Top;
    }

    private void SearchItButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (OCR.processingActive)
        {
            Main.StatusUpdate("Still Processing Reward Screen", 2);
            return;
        }

        Logger.Debug("Starting search it");
        Main.StatusUpdate("Starting search it", 0);
        Main.searchBox.Start();
    }

    private void OpenAppDataFolder(object sender, MouseButtonEventArgs e)
    {
        var processInfo = new ProcessStartInfo();
        processInfo.FileName = Main.AppPath;
        processInfo.UseShellExecute = true;

        Process.Start(processInfo);
    }
}