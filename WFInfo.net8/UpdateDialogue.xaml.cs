using AutoUpdaterDotNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using WFInfo.Services;
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

        var version = args.CurrentVersion;

        if (!args.IsUpdateAvailable || _settings.Ignored == version)
            return;

        version = version[..version.LastIndexOf('.')];

        NewVersionText.Text = $"WFInfo version {version} has been released!";
        OldVersionText.Text = $"You have version {ApplicationConstants.MajorBuildVersion} installed.";

        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
        var client = httpFactory.CreateClient();

        var response = client.GetAsync("https://api.github.com/repos/WFCD/WFInfo/releases").GetAwaiter().GetResult();
        var data = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        var releases = JsonConvert.DeserializeObject<JArray>(data);
        foreach (JObject prop in releases)
        {
            if (prop["prerelease"].ToObject<bool>())
                continue;

            var tagName = prop["tag_name"].ToString();

            if (tagName[1..] == ApplicationConstants.MajorBuildVersion)
                break;

            var tag = new TextBlock
            {
                Text = tagName,
                FontWeight = FontWeights.Bold
            };

            ReleaseNotes.Children.Add(tag);
            var body = new TextBlock
            {
                Text = $"{prop["body"]}\n",
                Padding = new Thickness(10, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            ReleaseNotes.Children.Add(body);
        }

        Show();
        Focus();
    }

    private void YesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            e.Handled = true;
            if (AutoUpdater.DownloadUpdate(_updateInfo))
                WFInfo.MainWindow.INSTANCE.Close();
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
