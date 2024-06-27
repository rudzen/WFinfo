using System.Windows;
using System.Windows.Input;

namespace WFInfo;

public partial class GFNWarning : Window
{
    public GFNWarning()
    {
        InitializeComponent();
    }

    public void Open()
    {
        Show();
        Focus();
    }

    private void Exit(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    // Allows the dragging of the window
    private new void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}
