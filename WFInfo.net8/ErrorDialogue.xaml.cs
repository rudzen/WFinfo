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

    private int distance;
    private DateTime closest;

    public ErrorDialogue(DateTime timeStamp, int gap)
    {
        distance = gap;
        closest = timeStamp;

        InitializeComponent();
        Show();
        Focus();
    }

    private void YesClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ZipPath);

        var files = new DirectoryInfo(ApplicationConstants.AppPathDebug)
                    .GetFiles()
                    .Where(f => f.CreationTimeUtc > closest.AddSeconds(-1 * distance))
                    .Where(f => f.CreationTimeUtc < closest.AddSeconds(distance));

        var staticFiles = new[]
        {
            Path.Combine(ApplicationConstants.AppPathDebug, "..", "eqmt_data.json"),
            Path.Combine(ApplicationConstants.AppPathDebug, "..", "market_data.json"),
            Path.Combine(ApplicationConstants.AppPathDebug, "..", "market_items.json"),
            Path.Combine(ApplicationConstants.AppPathDebug, "..", "name_data.json"),
            Path.Combine(ApplicationConstants.AppPathDebug, "..", "relic_data.json"),
            Path.Combine(ApplicationConstants.AppPathDebug, "..", "settings.json"),
            Path.Combine(ApplicationConstants.AppPathDebug, "..", "debug.json"),
        };

        var time = closest.ToString("yyyy-MM-dd_HH-mm-ssff");
        var fullZipPath = Path.Combine(ZipPath, $"WFInfoError_{time}");
        try
        {
            using var zip = new ZipFile();
            foreach (var file in files)
                zip.AddFile(file.FullName, string.Empty);

            foreach (var staticFile in staticFiles)
            {
                if (File.Exists(staticFile))
                    zip.AddFile(staticFile, string.Empty);
                else
                    Logger.Debug("File doesn't exist. file={File}", staticFile);
            }

            zip.Comment = $"This zip was created at {time}";
            zip.MaxOutputSegmentSize64 = 25000 * 1024; // 8m segments
            zip.Save(Path.Combine(fullZipPath, ".zip"));
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
