using System.Windows;
using System.Windows.Input;
using Serilog;

namespace WFInfo;

public partial class FullscreenReminder : Window
{
    private static readonly ILogger Logger = Log.Logger.ForContext<FullscreenReminder>();

    public FullscreenReminder()
    {
        InitializeComponent();
    }

    public void Open()
    {
        Show();
        Focus();
    }

    private void DisableOverlayClick(object sender, RoutedEventArgs e)
    {
        Logger.Debug("[Fullscreen Reminder] User selected \"Disable overlay mode\" - showing Setting window");
        Main.SettingsWindow.Show();
        Main.SettingsWindow.Populate();
        Main.SettingsWindow.Left = Left;
        Main.SettingsWindow.Top = Top + Height;
        Main.SettingsWindow.Show();
        Close();
    }

    private void NoClick(object sender, RoutedEventArgs e)
    {
        Logger.Debug($"[Fullscreen Reminder] User selected \"Do nothing\"");
        Close();
    }

    // Allows the draging of the window
    private new void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}
