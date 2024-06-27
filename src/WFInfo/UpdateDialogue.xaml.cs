using AutoUpdaterDotNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mediator;
using WFInfo.Domain;
using WFInfo.Extensions;
using WFInfo.Services;
using WFInfo.Settings;

namespace WFInfo;

/// <summary>
/// Interaction logic for errorDialogue.xaml
/// </summary>
public partial class UpdateDialogue : Window, INotificationHandler<UpdateWindowShow>
{
    private UpdateInfoEventArgs _updateInfo;

    private readonly SettingsViewModel _settings;
    private readonly IMediator _mediator;
    private readonly IHttpClientFactory _httpFactory;

    public UpdateDialogue(
        SettingsViewModel settings,
        IMediator mediator,
        IHttpClientFactory httpFactory)
    {
        InitializeComponent();
        _settings = settings;
        _mediator = mediator;
        _httpFactory = httpFactory;
    }

    private async Task UpdateAndShow(UpdateInfoEventArgs args)
    {
        _updateInfo = args;

        var version = args.CurrentVersion;

        if (!args.IsUpdateAvailable || _settings.Ignored == version)
            return;

        version = version[..version.LastIndexOf('.')];

        Dispatcher.InvokeIfRequired(() =>
        {
            NewVersionText.Text = $"WFInfo version {version} has been released!";
            OldVersionText.Text = $"You have version {ApplicationConstants.BuildVersion} installed.";
            ReleaseNotes.Children.Clear();
        });

        var client = _httpFactory.CreateClient();

        var response = await client.GetAsync("https://api.github.com/repos/WFCD/WFInfo/releases").ConfigureAwait(ConfigureAwaitOptions.None);
        var data = await response.DecompressContent().ConfigureAwait(ConfigureAwaitOptions.None);

        var releases = JsonConvert.DeserializeObject<JArray>(data);

        foreach (JObject prop in releases)
        {
            if (prop["prerelease"].ToObject<bool>())
                continue;

            var tagName = prop["tag_name"].ToString();

            if (tagName[1..] == ApplicationConstants.MajorBuildVersion)
                break;

            Dispatcher.InvokeIfRequired(() =>
            {
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
            });
        }

        Dispatcher.InvokeIfRequired(() =>
        {
            Show();
            Focus();
        });
    }

    private async void YesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            e.Handled = true;
            await _mediator.Publish(new DownloadUpdate(_updateInfo));
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                messageBoxText: exception.Message,
                caption: exception.GetType().ToString(),
                button: MessageBoxButton.OK,
                icon: MessageBoxImage.Error
            );
        }

        Close();
    }

    private void Skip(object sender, RoutedEventArgs e)
    {
        _settings.Ignored = _updateInfo.CurrentVersion;
        _settings.Save();
        Close();
    }

    private void Exit(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // Allows the dragging of the window
    private new void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    public ValueTask Handle(UpdateWindowShow notification, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
