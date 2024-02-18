using Ionic.Zip;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Serilog;
using WFInfo.Services;

namespace WFInfo;

/// <summary>
/// Interaction logic for errorDialogue.xaml
/// </summary>
public partial class ErrorDialogue : Window
{
    private static readonly ILogger Logger = Log.Logger.ForContext<ErrorDialogue>();
    private static readonly string ZipPath = Path.Combine(ApplicationConstants.AppPath, "generatedZip");

    private static readonly string[] StaticFiles =
    [
        Path.Combine(ApplicationConstants.AppPath, "eqmt_data.json"),
        Path.Combine(ApplicationConstants.AppPath, "market_data.json"),
        Path.Combine(ApplicationConstants.AppPath, "market_items.json"),
        Path.Combine(ApplicationConstants.AppPath, "name_data.json"),
        Path.Combine(ApplicationConstants.AppPath, "relic_data.json"),
        Path.Combine(ApplicationConstants.AppPath, "settings.json"),
        Path.Combine(ApplicationConstants.AppPath, "debug.json")
    ];

    private readonly int _distance;
    private readonly DateTime _closest;

    public ErrorDialogue(DateTime timeStamp, int gap)
    {
        _distance = gap;
        _closest = timeStamp;

        InitializeComponent();
        Show();
        Focus();
    }

    private void YesClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ZipPath);

        var files = new DirectoryInfo(ApplicationConstants.AppPathDebug)
                    .GetFiles()
                    .Where(f => f.CreationTimeUtc > _closest.AddSeconds(-1 * _distance))
                    .Where(f => f.CreationTimeUtc < _closest.AddSeconds(_distance));

        var time = _closest.ToString("yyyy-MM-dd_HH-mm-ssff");
        var fullZipPath = Path.Combine(ZipPath, $"WFInfoError_{time}.zip");
        try
        {
            using var zip = new ZipFile(fullZipPath);
            foreach (var file in files)
                zip.AddFile(file.FullName, string.Empty);

            foreach (var staticFile in StaticFiles)
            {
                if (File.Exists(staticFile))
                    zip.AddFile(staticFile, string.Empty);
                else
                    Logger.Debug("File doesn't exist. file={File}", staticFile);
            }

            zip.Comment = $"This zip was created at {time}";
            zip.MaxOutputSegmentSize64 = 25000 * 1024; // 8m segments
            zip.Save();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unable to zip file(s)");
            throw;
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(ZipPath),
            UseShellExecute = true
        };

        Process.Start(processStartInfo);
        Close();
    }

    private void NoClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // Allows the draging of the window
    private new void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}
