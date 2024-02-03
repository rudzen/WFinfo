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
    
    string startPath = Main.AppPath + @"\Debug";
    string zipPath = Main.AppPath   + @"\generatedZip";

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

    public void YesClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(zipPath);

        List<FileInfo> files = (new DirectoryInfo(Main.AppPath + @"\Debug\")).GetFiles()
                                                                             .Where(f => f.CreationTimeUtc >
                                                                                 closest.AddSeconds(-1 * distance))
                                                                             .Where(f => f.CreationTimeUtc <
                                                                                 closest.AddSeconds(distance))
                                                                             .ToList();

        var fullZipPath = zipPath + @"\WFInfoError_" + closest.ToString("yyyy-MM-dd_HH-mm-ssff");
        try
        {
            using ZipFile zip = new ZipFile();
            foreach (FileInfo file in files)
                zip.AddFile(file.FullName, "");
            if (File.Exists(startPath + @"\..\eqmt_data.json"))
            {
                zip.AddFile(startPath + @"\..\eqmt_data.json", "");
            }
            else
                Logger.Debug(startPath + "eqmt_data.json didn't exist.");

            if (File.Exists(startPath + @"\..\market_data.json"))
            {
                zip.AddFile(startPath + @"\..\market_data.json", "");
            }
            else
                Logger.Debug(startPath + "market_data.json didn't exist.");

            if (File.Exists(startPath + @"\..\market_items.json"))
            {
                zip.AddFile(startPath + @"\..\market_items.json", "");
            }
            else
                Logger.Debug(startPath + "market_items.json didn't exist.");

            if (File.Exists(startPath + @"\..\name_data.json"))
            {
                zip.AddFile(startPath + @"\..\name_data.json", "");
            }
            else
                Logger.Debug(startPath + "name_data.json didn't exist.");

            if (File.Exists(startPath + @"\..\relic_data.json"))
            {
                zip.AddFile(startPath + @"\..\relic_data.json", "");
            }
            else
                Logger.Debug(startPath + "relic_data.json didn't exist.");

            if (File.Exists(startPath + @"\..\settings.json"))
            {
                zip.AddFile(startPath + @"\..\settings.json", "");
            }
            else
                Logger.Debug(startPath + "settings.json didn't exist.");

            zip.AddFile(startPath + @"\..\debug.log", "");
            zip.Comment = "This zip was created at " + closest.ToString("yyyy-MM-dd_HH-mm-ssff");
            zip.MaxOutputSegmentSize64 = 25000 * 1024; // 8m segments
            zip.Save(fullZipPath + ".zip");
        }
        catch (Exception ex)
        {
            Logger.Debug("Unable to zip due to: " + ex.ToString());
            throw;
        }

        var processStartInfo = new ProcessStartInfo();
        processStartInfo.FileName = Path.Combine(Main.AppPath, "generatedZip");
        processStartInfo.UseShellExecute = true;

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