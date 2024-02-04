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
        Show();
        Focus();
    }

    private void DisableOverlayClick(object sender, RoutedEventArgs e)
    {
        Logger.Debug("[Fullscreen Reminder] User selected \"Disable overlay mode\" - showing Setting window");
        Main.settingsWindow.Show();
        Main.settingsWindow.Populate();
        Main.settingsWindow.Left = Left;
        Main.settingsWindow.Top = Top + Height;
        Main.settingsWindow.Show();
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