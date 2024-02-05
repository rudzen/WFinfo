using Ionic.Zip;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Serilog;

namespace WFInfo;

/// <summary>
/// Interaction logic for errorDialogue.xaml
/// </summary>
public partial class ErrorDialogue : Window
{
    private static readonly ILogger Logger = Log.Logger.ForContext<ErrorDialogue>();

    private static readonly string startPath = Path.Combine(ApplicationConstants.AppPath, "Debug");
    private static readonly string zipPath = Path.Combine(ApplicationConstants.AppPath, "generatedZip");

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
        Directory.CreateDirectory(zipPath);

        var files = new DirectoryInfo(startPath)
                    .GetFiles()
                    .Where(f => f.CreationTimeUtc > closest.AddSeconds(-1 * distance))
                    .Where(f => f.CreationTimeUtc < closest.AddSeconds(distance));

        var staticFiles = new string[]
        {
            Path.Combine(startPath, "..", "eqmt_data.json"),
            Path.Combine(startPath, "..", "market_data.json"),
            Path.Combine(startPath, "..", "market_items.json"),
            Path.Combine(startPath, "..", "name_data.json"),
            Path.Combine(startPath, "..", "relic_data.json"),
            Path.Combine(startPath, "..", "settings.json"),
            Path.Combine(startPath, "..", "debug.json"),
        };

        var time = closest.ToString("yyyy-MM-dd_HH-mm-ssff");
        var fullZipPath = Path.Combine(zipPath, $"WFInfoError_{time}");
        try
        {
            using ZipFile zip = new ZipFile();
            foreach (FileInfo file in files)
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
            FileName = Path.Combine(zipPath),
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