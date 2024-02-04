using AutoUpdaterDotNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using WFInfo.Settings;

namespace WFInfo;

/// <summary>
/// Interaction logic for errorDialogue.xaml
/// </summary>
public partial class UpdateDialogue : Window
{
    private readonly UpdateInfoEventArgs _updateInfo;
    
    private readonly SettingsViewModel _settings;

    public UpdateDialogue(UpdateInfoEventArgs args, IServiceProvider sp)
    {
        InitializeComponent();
        _settings = sp.GetRequiredService<SettingsViewModel>();
        _updateInfo = args;

        string version = args.CurrentVersion.ToString();
        if (!args.IsUpdateAvailable || (_settings.Ignored == version))
            return;
        version = version[..version.LastIndexOf('.')];

        NewVersionText.Text = "WFInfo version "   + version           + " has been released!";
        OldVersionText.Text = "You have version " + Main.BuildVersion + " installed.";

        using var webClient = CustomEntrypoint.CreateNewWebClient();
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        var data = webClient.DownloadString("https://api.github.com/repos/WFCD/WFInfo/releases");
        JArray releases = JsonConvert.DeserializeObject<JArray>(data);
        foreach (JObject prop in releases)
        {
            if (!prop["prerelease"].ToObject<bool>())
            {
                string tag_name = prop["tag_name"].ToString();
                if (tag_name.Substring(1) == Main.BuildVersion)
                    break;
                TextBlock tag = new TextBlock();
                tag.Text = tag_name;
                tag.FontWeight = FontWeights.Bold;
                ReleaseNotes.Children.Add(tag);
                TextBlock body = new TextBlock
                {
                    Text = prop["body"].ToString() + "\n",
                    Padding = new Thickness(10, 0, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                };
                ReleaseNotes.Children.Add(body);
            }
        }

        Show();
        Focus();
    }

    public void YesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            e.Handled = true;
            if (AutoUpdater.DownloadUpdate(_updateInfo))
                WFInfo.MainWindow.INSTANCE.Exit(null, null);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, exception.GetType().ToString(), MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Skip(object sender, RoutedEventArgs e)
    {
        _settings.Ignored = _updateInfo.CurrentVersion.ToString();
        _settings.Save();
        Close();
    }

    private void Exit(object sender, RoutedEventArgs e)
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