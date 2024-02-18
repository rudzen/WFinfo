using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Mediator;
using Microsoft.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using WFInfo.Domain;
using WFInfo.Extensions;
using WFInfo.Services;
using WFInfo.Services.OpticalCharacterRecognition;
using Application = System.Windows.Application;

namespace WFInfo.Settings;

/// <summary>
/// Interaction logic for verifyCount.xaml
/// </summary>
public partial class ThemeAdjuster : Window, INotificationHandler<ThemeAdjusterShow>
{
    private static readonly ILogger Logger = Log.Logger.ForContext<ThemeAdjuster>();
    private static readonly RecyclableMemoryStreamManager StreamManager = new();

    private readonly SettingsViewModel _settingsViewModel;
    private readonly IPublisher _publisher;
    private readonly IOCR _ocr;

    private Bitmap? _unfiltered;
    private BitmapImage? _displayImage;

    public ThemeAdjuster(SettingsViewModel settingsViewModel, IPublisher publisher, IOCR ocr)
    {
        InitializeComponent();
        DataContext = this;
        _settingsViewModel = settingsViewModel;
        _publisher = publisher;
        _ocr = ocr;
    }

    public void ShowThemeAdjuster()
    {
        Show();
        Focus();
    }

    private static BitmapImage BitmapToImageSource(Image bitmap)
    {
        //from https://stackoverflow.com/questions/22499407/how-to-display-a-bitmap-in-a-wpf-image
        using var memory = StreamManager.GetStream();
        bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
        memory.Position = 0;
        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.StreamSource = memory;
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.EndInit();
        bitmapImage.Freeze();

        return bitmapImage;
    }

    private void ApplyFilter(object sender, RoutedEventArgs e)
    {
        if (_unfiltered is null)
            return;

        var filtered = _ocr.ScaleUpAndFilter(_unfiltered, WFtheme.CUSTOM, out var rowHits, out var colHits);
        _displayImage = BitmapToImageSource(filtered);
        previewImage.Source = _displayImage;
        filtered.Dispose();
    }

    private void ShowUnfiltered(object sender, RoutedEventArgs e)
    {
        if (_unfiltered is null)
            return;

        _displayImage = BitmapToImageSource(_unfiltered);
        previewImage.Source = _displayImage;
    }

    private async void LoadLatest(object sender, RoutedEventArgs e)
    {
        var files = new DirectoryInfo(ApplicationConstants.AppPathDebug)
                    .GetFiles()
                    .Where(f => f.Name.Contains("FullScreenShot"))
                    .ToList();

        files = files.OrderBy(f => f.CreationTimeUtc).ToList();
        files.Reverse();

        Bitmap? image = null;
        try
        {
            foreach (var file in files)
            {
                Logger.Debug("Loading filter testing with file: {File}", file.Name);

                //Get the path of specified file
                image = new Bitmap(file.FullName);
                break;
            }
        }
        catch (Exception exc)
        {
            Logger.Error(exc, "Failed to load image");
            await _publisher.Publish(new UpdateStatus("Failed to load image", StatusSeverity.Error));
        }

        if (image is null)
            return;

        _unfiltered = image;
        Application.Current.Dispatcher.InvokeIfRequired(() =>
        {
            ShowUnfiltered(null, null);
        });
    }

    private async void LoadFromFile(object sender, RoutedEventArgs e)
    {
        // Using WinForms for the openFileDialog because it's simpler and much easier
        using var openFileDialog = new OpenFileDialog();
        openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        openFileDialog.Filter = "image files (*.png)|*.png|All files (*.*)|*.*";
        openFileDialog.FilterIndex = 2;
        openFileDialog.RestoreDirectory = true;
        openFileDialog.Multiselect = true;

        if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            Task.Run(async
                () =>
            {
                Bitmap image = null;
                try
                {
                    foreach (var file in openFileDialog.FileNames)
                    {
                        Logger.Debug("Loading filter testing with file: {File}", file);

                        //Get the path of specified file
                        image = new Bitmap(file);
                        break;
                    }
                }
                catch (Exception exc)
                {
                    Logger.Error(exc, "Failed to load image");
                    await _publisher.Publish(new UpdateStatus("Failed to load image", StatusSeverity.Error));
                }

                if (image is null)
                    return;

                _unfiltered?.Dispose();
                _unfiltered = image;
                Application.Current.Dispatcher.InvokeIfRequired(() =>
                {
                    ShowUnfiltered(null, null);
                });
            });
        }
        else
        {
            await _publisher.Publish(new UpdateStatus("Failed to load image", StatusSeverity.Error));
        }
    }

    private void ExportFilterJson(object sender, RoutedEventArgs e)
    {
        var exp = new JObject
        {
            { "CF_usePrimaryHSL", _settingsViewModel.CF_usePrimaryHSL },
            { "CF_pHueMax", _settingsViewModel.CF_pHueMax },
            { "CF_pHueMin", _settingsViewModel.CF_pHueMin },
            { "CF_pSatMax", _settingsViewModel.CF_pSatMax },
            { "CF_pSatMin", _settingsViewModel.CF_pSatMin },
            { "CF_pBrightMax", _settingsViewModel.CF_pBrightMax },
            { "CF_pBrightMin", _settingsViewModel.CF_pBrightMin },

            { "CF_usePrimaryRGB", _settingsViewModel.CF_usePrimaryRGB },
            { "CF_pRMax", _settingsViewModel.CF_pRMax },
            { "CF_pRMin", _settingsViewModel.CF_pRMin },
            { "CF_pGMax", _settingsViewModel.CF_pGMax },
            { "CF_pGMin", _settingsViewModel.CF_pGMin },
            { "CF_pBMax", _settingsViewModel.CF_pBMax },
            { "CF_pBMin", _settingsViewModel.CF_pBMin },

            { "CF_useSecondaryHSL", _settingsViewModel.CF_useSecondaryHSL },
            { "CF_sHueMax", _settingsViewModel.CF_sHueMax },
            { "CF_sHueMin", _settingsViewModel.CF_sHueMin },
            { "CF_sSatMax", _settingsViewModel.CF_sSatMax },
            { "CF_sSatMin", _settingsViewModel.CF_sSatMin },
            { "CF_sBrightMax", _settingsViewModel.CF_sBrightMax },
            { "CF_sBrightMin", _settingsViewModel.CF_sBrightMin },

            { "CF_useSecondaryRGB", _settingsViewModel.CF_useSecondaryRGB },
            { "CF_sRMax", _settingsViewModel.CF_sRMax },
            { "CF_sRMin", _settingsViewModel.CF_sRMin },
            { "CF_sGMax", _settingsViewModel.CF_sGMax },
            { "CF_sGMin", _settingsViewModel.CF_sGMin },
            { "CF_sBMax", _settingsViewModel.CF_sBMax },
            { "CF_sBMin", _settingsViewModel.CF_sBMin }
        };
        filterTextBox.Text = JsonConvert.SerializeObject(exp, Formatting.None);
    }

    private async void ImportFilterJson(object sender, RoutedEventArgs e)
    {
        var input = filterTextBox.Text;
        try
        {
            //try to read all parameters to temporary variables
            var json = JsonConvert.DeserializeObject<JObject>(input);
            var CF_usePrimaryHSL = json["CF_usePrimaryHSL"].ToObject<bool>();
            var CF_pHueMax = json["CF_pHueMax"].ToObject<float>();
            var CF_pHueMin = json["CF_pHueMin"].ToObject<float>();
            var CF_pSatMax = json["CF_pSatMax"].ToObject<float>();
            var CF_pSatMin = json["CF_pSatMin"].ToObject<float>();
            var CF_pBrightMax = json["CF_pBrightMax"].ToObject<float>();
            var CF_pBrightMin = json["CF_pBrightMin"].ToObject<float>();

            var CF_usePrimaryRGB = json["CF_usePrimaryRGB"].ToObject<bool>();
            var CF_pRMax = json["CF_pRMax"].ToObject<int>();
            var CF_pRMin = json["CF_pRMin"].ToObject<int>();
            var CF_pGMax = json["CF_pGMax"].ToObject<int>();
            var CF_pGMin = json["CF_pGMin"].ToObject<int>();
            var CF_pBMax = json["CF_pBMax"].ToObject<int>();
            var CF_pBMin = json["CF_pBMin"].ToObject<int>();

            var CF_useSecondaryHSL = json["CF_useSecondaryHSL"].ToObject<bool>();
            var CF_sHueMax = json["CF_sHueMax"].ToObject<float>();
            var CF_sHueMin = json["CF_sHueMin"].ToObject<float>();
            var CF_sSatMax = json["CF_sSatMax"].ToObject<float>();
            var CF_sSatMin = json["CF_sSatMin"].ToObject<float>();
            var CF_sBrightMax = json["CF_sBrightMax"].ToObject<float>();
            var CF_sBrightMin = json["CF_sBrightMin"].ToObject<float>();

            var CF_useSecondaryRGB = json["CF_useSecondaryRGB"].ToObject<bool>();
            var CF_sRMax = json["CF_sRMax"].ToObject<int>();
            var CF_sRMin = json["CF_sRMin"].ToObject<int>();
            var CF_sGMax = json["CF_sGMax"].ToObject<int>();
            var CF_sGMin = json["CF_sGMin"].ToObject<int>();
            var CF_sBMax = json["CF_sBMax"].ToObject<int>();
            var CF_sBMin = json["CF_sBMin"].ToObject<int>();

            //all parameters read successfully, apply to actual settings
            _settingsViewModel.CF_usePrimaryHSL = CF_usePrimaryHSL;
            _settingsViewModel.CF_pHueMax = CF_pHueMax;
            _settingsViewModel.CF_pHueMin = CF_pHueMin;
            _settingsViewModel.CF_pSatMax = CF_pSatMax;
            _settingsViewModel.CF_pSatMin = CF_pSatMin;
            _settingsViewModel.CF_pBrightMax = CF_pBrightMax;
            _settingsViewModel.CF_pBrightMin = CF_pBrightMin;

            _settingsViewModel.CF_usePrimaryRGB = CF_usePrimaryRGB;
            _settingsViewModel.CF_pRMax = CF_pRMax;
            _settingsViewModel.CF_pRMin = CF_pRMin;
            _settingsViewModel.CF_pGMax = CF_pGMax;
            _settingsViewModel.CF_pGMin = CF_pGMin;
            _settingsViewModel.CF_pBMax = CF_pBMax;
            _settingsViewModel.CF_pBMin = CF_pBMin;

            _settingsViewModel.CF_useSecondaryHSL = CF_useSecondaryHSL;
            _settingsViewModel.CF_sHueMax = CF_sHueMax;
            _settingsViewModel.CF_sHueMin = CF_sHueMin;
            _settingsViewModel.CF_sSatMax = CF_sSatMax;
            _settingsViewModel.CF_sSatMin = CF_sSatMin;
            _settingsViewModel.CF_sBrightMax = CF_sBrightMax;
            _settingsViewModel.CF_sBrightMin = CF_sBrightMin;

            _settingsViewModel.CF_useSecondaryRGB = CF_useSecondaryRGB;
            _settingsViewModel.CF_sRMax = CF_sRMax;
            _settingsViewModel.CF_sRMin = CF_sRMin;
            _settingsViewModel.CF_sGMax = CF_sGMax;
            _settingsViewModel.CF_sGMin = CF_sGMin;
            _settingsViewModel.CF_sBMax = CF_sBMax;
            _settingsViewModel.CF_sBMin = CF_sBMin;
        }
        catch (Exception exc)
        {
            Logger.Error(exc, "Custom Filter Import failed. Input: {Input}", input);
            await _publisher.Publish(new ErrorDialogShow(DateTime.UtcNow));
        }
    }

    private void Hide(object sender, RoutedEventArgs e)
    {
        _unfiltered?.Dispose();
        _unfiltered = null;
        _displayImage = null;
        previewImage.Source = null;
        Hide();
    }

    // Allows the draging of the window
    private new void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    public ValueTask Handle(ThemeAdjusterShow notification, CancellationToken cancellationToken)
    {
        Dispatcher.InvokeIfRequired(ShowThemeAdjuster);
        return ValueTask.CompletedTask;
    }
}
