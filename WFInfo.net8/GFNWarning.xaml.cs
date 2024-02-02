using System.Windows;
using System.Windows.Input;

namespace WFInfo;

/// <summary>
/// Interaction logic for errorDialogue.xaml
/// </summary>
public partial class GFNWarning : Window
{

    public GFNWarning()
    {
        InitializeComponent();
        Show();
        Focus();
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
