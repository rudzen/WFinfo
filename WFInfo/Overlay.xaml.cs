using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Serilog;
using WFInfo.Services;
using WFInfo.Settings;

namespace WFInfo;

/// <summary>
/// Interaction logic for Overlay.xaml
/// </summary>
public partial class Overlay : Window
{
    private static readonly ILogger Logger = Log.Logger.ForContext<Overlay>();

    private const double window_wid = 243.0;
    private const double window_hei = 160.0;
    private static double part_line_hei = 20.0; // TBD
    private const double partMarginTop = 39.0;
    private const double partMarginBottom = 100.0;
    private const double volumeMarginTop = 104.0;
    private const double volumeMarginBottom = 37.0;
    private const double platMarginRight = 163.0;
    private const double platMarginTop = 77.0;
    private const double platMarginBottom = 63.0;
    private const double ducatMarginRight = 78.0;
    private const double ducatMarginTop = 77.0;
    private const double ducatMarginbottom = 63.0;
    private const double cornerMarginSide = 23.0;
    private const double cornerMarginTop = 15.0;
    private const double cornerMarginBottom = 130.0;
    private const double primeSetMarginTop = 130.0;
    private const double primeSetMarginBottom = 15.0;

    private const double platImageMarginLeft = 88.0;
    private const double platImageMarginBottom = 64.0;
    private const double platImageHeightWidth = 20.0;
    private const double ducatImageMarginLeft = 172.0;
    private const double ducatImageMarginBottom = 64.0;
    private const double ducatImageHeightWidth = 20.0;
    private const double setPlatImageMarginLeft = 115.0;
    private const double setPlatImageBottom = 15.0;
    private const double setPlatImageHeightWidth = 15.0;

    private const double warningImageMarginRight = 180.0;
    private const double warningImageBottom = 15.0;
    private const double warningImageHeightWidth = 30.0;

    private static double platMarginRightSanpit = 187.0;
    private const double platMarginLeftSanpit = 30.0;

    private static double ducatMargineRightSanpit = 119.0;
    private const double ducatMargineLeftSanpit = 98.0;

    private const double EfficiencyMarginRight = 51.0;

    private static double platImageMarginLeftSanpit = 61.0;
    private const double ducatImageMarginLeftSanpit = 130.0;
    private const double EfficiencyplatImageMarginLeft = 206.0;
    private const double EfficiencyplatImageMarginBottom = 64.0;
    private const double EfficiencyplatImageHeightWidth = 12.0;
    private const double EfficiencyducatImageMarginLeft = 197.0;
    private const double EfficiencyducatImageMarginBottom = 72.0;
    private const double EfficiencyducatImageHeightWidth = 12.0;

    private static double largefont = 18.0;
    private const double mediumFont = 17.0;
    private const double smallFont = 14.0;

    private static readonly Color blu = Color.FromRgb(177, 208, 217);
    private static readonly SolidColorBrush bluBrush = new(blu);

    private readonly DispatcherTimer hider = new();

    public static bool RewardsDisplaying { get; set; }

    private readonly ApplicationSettings _settings;

    public Overlay(ApplicationSettings applicationSettings)
    {
        _settings = applicationSettings;
        hider.Interval = TimeSpan.FromSeconds(10);
        hider.Tick += HideOverlay;
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        Win32.SetWindowExTransparent(hwnd);
    }

    public void BestPlatChoice()
    {
        PlatText.FontWeight = FontWeights.Bold;
        PartText.FontWeight = FontWeights.Bold;
        PlatText.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 0));
        PartText.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 0));
    }

    public void BestDucatChoice()
    {
        DucatText.FontWeight = FontWeights.Bold;
        PartText.FontWeight = FontWeights.Bold;
        DucatText.Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0));
        PartText.Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0));
    }

    public void BestOwnedChoice()
    {
        OwnedText.FontWeight = FontWeights.Bold;
        PartText.FontWeight = FontWeights.Bold;
        OwnedText.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 215));
        PartText.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 215));
    }

    public void LoadTextData(
        string name,
        string plat,
        string? primeSetPlat,
        string ducats,
        string volume,
        bool vaulted,
        bool mastered,
        string owned,
        string detected,
        bool hideRewardInfo,
        bool showWarningTriangle)
    {
        DucatText.Foreground = bluBrush;
        DucatText.FontWeight = FontWeights.Normal;
        PlatText.Foreground = bluBrush;
        PlatText.FontWeight = FontWeights.Normal;
        PrimeSetPlatText.Foreground = bluBrush;
        PrimeSetPlatText.FontWeight = FontWeights.Normal;
        OwnedText.Foreground = bluBrush;
        OwnedText.FontWeight = FontWeights.Normal;
        PartText.Foreground = bluBrush;
        PartText.FontWeight = FontWeights.Normal;

        if (_settings.HighContrast)
        {
            Logger.Debug("Turning high contrast on");
            BackgroundGrid.Background = new SolidColorBrush(Color.FromRgb(0, 0, 0));
        }
        else
        {
            BackgroundGrid.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }

        WarningImage.Visibility = showWarningTriangle ? Visibility.Visible : Visibility.Hidden;

        PartText.Text = name;
        if (hideRewardInfo)
        {
            PlatText.Visibility = Visibility.Hidden;
            PrimeSetPlatText.Visibility = Visibility.Hidden;
            SetPlatImage.Visibility = Visibility.Hidden;
            DucatText.Visibility = Visibility.Hidden;
            VolumeText.Visibility = Visibility.Hidden;
            VaultedMargin.Visibility = Visibility.Hidden;
            PlatImage.Visibility = Visibility.Hidden;
            DucatImage.Visibility = Visibility.Hidden;
            OwnedText.Text = string.Empty;
        }
        else
        {
            PlatText.Visibility = Visibility.Visible;
            if (primeSetPlat != null)
            {
                PrimeSetPlatText.Text = "Full set price: " + primeSetPlat;
                PrimeSetPlatText.Visibility = Visibility.Visible;
            }

            SetPlatImage.Visibility = Visibility.Visible;
            DucatText.Visibility = Visibility.Visible;
            VolumeText.Visibility = Visibility.Visible;
            VaultedMargin.Visibility = Visibility.Visible;
            PlatImage.Visibility = Visibility.Visible;
            DucatImage.Visibility = Visibility.Visible;
            PlatText.Text = plat;
            DucatText.Text = ducats;
            VolumeText.Text = $"{volume} sold last 48hrs";
            VaultedMargin.Visibility = vaulted ? Visibility.Visible : Visibility.Hidden;
            ArgumentNullException.ThrowIfNull(owned);

            OwnedText.Text = owned.Length > 0 ? $"{(mastered ? "✓ " : "")}{owned} OWNED" : string.Empty;
            if (detected.Length > 0)
                OwnedText.Text += $" ({detected} FOUND)";
        }

        double.TryParse(plat, NumberStyles.Any, ApplicationConstants.Culture, out var platinum);
        int.TryParse(ducats, NumberStyles.Any, ApplicationConstants.Culture, out var duc);
        var efficiency = $"{Math.Round(duc / platinum, 1)}";
        var color = GetColor(duc, in platinum);
        var brush = new SolidColorBrush(color);

        EfficiencyText.Text = efficiency;
        EfficiencyText.Foreground = brush;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Color GetColor(int duc, in double platinum)
    {
        if (duc / platinum > _settings.MaximumEfficiencyValue)
            return Colors.LawnGreen;

        if (duc / platinum < _settings.MinimumEfficiencyValue)
            return Colors.DarkRed;

        return Color.FromArgb(100, 174, 199, 206);
    }

    public void Clear()
    {
        DucatText.Foreground = bluBrush;
        DucatText.FontWeight = FontWeights.Normal;
        PlatText.Foreground = bluBrush;
        PlatText.FontWeight = FontWeights.Normal;
        PrimeSetPlatText.Foreground = bluBrush;
        PrimeSetPlatText.FontWeight = FontWeights.Normal;
        OwnedText.Foreground = bluBrush;
        OwnedText.FontWeight = FontWeights.Normal;
        PartText.Foreground = bluBrush;
        PartText.FontWeight = FontWeights.Normal;
    }

    public void Resize(int wid)
    {
        var scale = wid / window_wid;
        Width = wid;
        Height = scale * window_hei;

        Thickness margin;

        // Part_Text
        margin = PartMargin.Margin;
        margin.Top = partMarginTop       * scale;
        margin.Bottom = partMarginBottom * scale;
        PartMargin.Margin = margin;

        // Vaulted_Text
        margin = VaultedMargin.Margin;
        margin.Top = cornerMarginTop       * scale;
        margin.Bottom = cornerMarginBottom * scale;
        margin.Right = cornerMarginSide    * scale;
        VaultedMargin.Margin = margin;
        VaultedText.FontSize = smallFont * scale;

        // Owned_Text
        margin = OwnedMargin.Margin;
        margin.Top = cornerMarginTop       * scale;
        margin.Bottom = cornerMarginBottom * scale;
        margin.Left = cornerMarginSide     * scale;
        OwnedMargin.Margin = margin;
        OwnedText.FontSize = smallFont * scale;

        // Volume_Text
        margin = VolumeMargin.Margin;
        margin.Top = volumeMarginTop       * scale;
        margin.Bottom = volumeMarginBottom * scale;
        VolumeMargin.Margin = margin;
        VolumeText.FontSize = mediumFont * scale;

        // Plat_Text
        margin = PlatMargin.Margin;
        margin.Top = platMarginTop       * scale;
        margin.Bottom = platMarginBottom * scale;
        margin.Right = platMarginRight   * scale;
        PlatMargin.Margin = margin;
        PlatText.FontSize = mediumFont * scale;

        // Set_Plat_Text
        margin = PrimeSetMargin.Margin;
        margin.Top = primeSetMarginTop       * scale;
        margin.Bottom = primeSetMarginBottom * scale;
        PrimeSetMargin.Margin = margin;
        PrimeSetPlatText.FontSize = mediumFont * scale;

        // Ducat_Text
        margin = DucatMargin.Margin;
        margin.Top = ducatMarginTop       * scale;
        margin.Bottom = ducatMarginbottom * scale;
        margin.Right = ducatMarginRight   * scale;
        DucatMargin.Margin = margin;
        DucatText.FontSize = mediumFont * scale;

        // Plat_IMG
        margin = PlatImage.Margin;
        margin.Bottom = platImageMarginBottom * scale;
        margin.Left = platImageMarginLeft     * scale;
        PlatImage.Margin = margin;
        PlatImage.Height = platImageHeightWidth * scale;
        PlatImage.Width = PlatImage.Height;

        // Set_Plat_IMG
        margin = SetPlatImage.Margin;
        margin.Bottom = setPlatImageBottom   * scale;
        margin.Left = setPlatImageMarginLeft * scale;
        SetPlatImage.Margin = margin;
        SetPlatImage.Height = setPlatImageHeightWidth * scale;
        SetPlatImage.Width = SetPlatImage.Height;

        // Warning_Triangle_IMG
        margin = WarningImage.Margin;
        margin.Bottom = warningImageBottom     * scale;
        margin.Right = warningImageMarginRight * scale;
        WarningImage.Margin = margin;
        WarningImage.Height = warningImageHeightWidth * scale;
        WarningImage.Width = WarningImage.Height;

        // Ducat_IMG
        margin = DucatImage.Margin;
        margin.Bottom = ducatImageMarginBottom * scale;
        margin.Left = ducatImageMarginLeft     * scale;
        DucatImage.Margin = margin;
        DucatImage.Height = ducatImageHeightWidth * scale;
        DucatImage.Width = DucatImage.Height;

        //snapit plat text
        margin = PlatMargineSnap.Margin;
        margin.Top = platMarginTop         * scale;
        margin.Bottom = platMarginBottom   * scale;
        margin.Left = platMarginLeftSanpit * scale;
        PlatMargineSnap.Margin = margin;
        PlatTextSnap.FontSize = mediumFont * scale;

        //snapit ducat text
        margin = DucatMargineSnap.Margin;
        margin.Top = platMarginTop           * scale;
        margin.Bottom = platMarginBottom     * scale;
        margin.Left = ducatMargineLeftSanpit * scale;
        DucatMargineSnap.Margin = margin;
        DucatTextSnap.FontSize = mediumFont * scale;

        //snapit efficiency text
        margin = EfficiencyMargin.Margin;
        margin.Top = platMarginTop           * scale;
        margin.Bottom = platMarginBottom     * scale;
        margin.Right = EfficiencyMarginRight * scale;
        EfficiencyMargin.Margin = margin;
        EfficiencyText.FontSize = mediumFont * scale;

        //snapit ducat image
        margin = DucatImageSnap.Margin;
        margin.Top = platMarginTop               * scale;
        margin.Bottom = ducatImageMarginBottom   * scale;
        margin.Left = ducatImageMarginLeftSanpit * scale;
        DucatImageSnap.Margin = margin;
        DucatImageSnap.Height = platImageHeightWidth * scale;
        DucatImageSnap.Width = DucatImage.Height;

        //snapit plat image
        margin = PlatImage.Margin;
        margin.Bottom = platImageMarginBottom * scale;
        margin.Left = 61                      * scale;
        PlatImageSnap.Margin = margin;
        PlatImageSnap.Height = platImageHeightWidth * scale;
        PlatImageSnap.Width = PlatImage.Height;

        //snapit plat efficiency image
        margin = EfficiencyPlatinumImage.Margin;
        margin.Bottom = EfficiencyplatImageMarginBottom * scale;
        margin.Left = EfficiencyplatImageMarginLeft     * scale;
        EfficiencyPlatinumImage.Margin = margin;
        EfficiencyPlatinumImage.Height = EfficiencyplatImageHeightWidth * scale;
        EfficiencyPlatinumImage.Width = DucatImage.Height;

        //snapit ducat efficiency image
        margin = EfficiencyDucatImage.Margin;
        margin.Bottom = EfficiencyducatImageMarginBottom * scale;
        margin.Left = EfficiencyducatImageMarginLeft     * scale;
        EfficiencyDucatImage.Margin = margin;
        EfficiencyDucatImage.Height = EfficiencyducatImageHeightWidth * scale;
        EfficiencyDucatImage.Width = DucatImage.Height;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Display(int x, int y, int wait = 10000)
    {
        hider.Stop();
        hider.Interval = TimeSpan.FromMilliseconds(wait);
        Left = x;
        Top = y;
        Show();
        hider.Start();
    }

    private void HideOverlay(object? sender, EventArgs e)
    {
        hider.Stop();
        Hide();
        Main.StatusUpdate("WFinfo is ready", 0);
        RewardsDisplaying = false;
    }

    public void ToSnapIt()
    {
        PlatImage.Visibility = Visibility.Collapsed;
        PlatMargin.Visibility = Visibility.Collapsed;

        DucatImage.Visibility = Visibility.Collapsed;
        DucatMargin.Visibility = Visibility.Collapsed;

        DucatMargineSnap.Visibility = Visibility.Visible;
        DucatImageSnap.Visibility = Visibility.Visible;

        PlatMargineSnap.Visibility = Visibility.Visible;
        PlatImageSnap.Visibility = Visibility.Visible;

        EfficiencyMargin.Visibility = Visibility.Visible;
        EfficiencyDucatImage.Visibility = Visibility.Visible;
        EfficiencyPlatinumImage.Visibility = Visibility.Visible;

        PlatTextSnap.Text = PlatText.Text;
        DucatTextSnap.Text = DucatText.Text;
    }
}
