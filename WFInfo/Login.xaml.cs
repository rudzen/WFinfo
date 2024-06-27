using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Mediator;
using Serilog;
using WFInfo.Domain;
using WFInfo.Services;
using WFInfo.Settings;

namespace WFInfo;

/// <summary>
/// Interaction logic for Login.xaml
/// </summary>
public partial class Login : Window
{
    private static readonly ILogger Logger = Log.Logger.ForContext<Login>();

    private readonly SettingsWindow _settingsWindow;
    private readonly IEncryptedDataService _encryptedDataService;
    private readonly IMediator _mediator;

    public Login(
        SettingsWindow settingsWindow,
        IEncryptedDataService encryptedDataService,
        IMediator mediator)
    {
        InitializeComponent();
        _settingsWindow = settingsWindow;
        _encryptedDataService = encryptedDataService;
        _mediator = mediator;
    }

    #region default methods

    /// <summary>
    /// Hides the window
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void HideExternal(object sender, MouseButtonEventArgs e)
    {
        Main.SearchIt.Hide(); //hide search it if the user minimizes the window even though not yet logged in
        Hide();
    }

    /// <summary>
    /// Allows the window to be dragged
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private new void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    #endregion

    /// <summary>
    /// Attempts to log in with the filled in credentials.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void LoginClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            await Main.DataBase.GetUserLogin(Email.Text, Password.Password);
            await _mediator.Publish(new StartLoggedInTimer(Email.Text));
            Email.Text = "Email";
            Password.Password = string.Empty;
            Main.DataBase.RememberMe = RememberMe.IsChecked.HasValue && RememberMe.IsChecked.Value;

            //dispose of window once done
            Hide();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to login");
            _encryptedDataService.JWT = null;
            _settingsWindow.Save();

            //StatusMessage = text to display on StatusUpdate() AND the error box under login
            string statusMessage;

            //StatusSeverity = Severity for StatusUpdate()
            StatusSeverity statusSeverity;

            if (ex.Message.Contains("email"))
            {
                if (ex.Message.Contains("app.form.invalid"))
                {
                    statusMessage = "Invalid email form";
                    statusSeverity = StatusSeverity.Warning;
                }
                else
                {
                    statusMessage = "Unknown email";
                    statusSeverity = StatusSeverity.Error;
                }
            }
            else if (ex.Message.Contains("password"))
            {
                statusMessage = "Wrong password";
                statusSeverity = StatusSeverity.Error;
            }
            else if (ex.Message.Contains("could not understand"))
            {
                statusMessage = "Severe issue, server did not understand request";
                statusSeverity = StatusSeverity.Error;
            }
            else
            {
                //default to too many requests
                statusMessage = "Too many requests";
                statusSeverity = StatusSeverity.Error;
            }

            await _mediator.Publish(WarframeMarketSignOut.Instance);
            await _mediator.Publish(new UpdateStatus(statusMessage, statusSeverity));

            Error.Foreground = statusSeverity switch
            {
                StatusSeverity.Error => Brushes.Red,
                StatusSeverity.Warning => Brushes.Orange,
                _ => Brushes.Yellow
            };

            //Displaying the error under the text fields
            Error.Text = statusMessage;
            if (Error.Visibility != Visibility.Visible)
                Height += 20;

            Error.Visibility = Visibility.Visible;
            return;
        }

        if (Main.SearchIt.IsActive)
        {
            Main.SearchIt.Placeholder.Content = "Logged in";
            Main.SearchIt.IsInUse = true;
            Main.SearchIt.SearchField.Focusable = true;
        }
    }

    /// <summary>
    /// Clears the email field if it's not been used yet.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Email_GotFocus(object sender, RoutedEventArgs e)
    {
        if (Email.Text == "Email")
            Email.Text = string.Empty;
    }

    /// <summary>
    /// Allow the window to be spawned in an appropriate place.
    /// </summary>
    /// <param name="x">Left most border of the window</param>
    /// <param name="y">Top most border of the window</param>
    public void MoveLogin(double x, double y)
    {
        Left = x;
        Top = y;
        Show();
    }
}
