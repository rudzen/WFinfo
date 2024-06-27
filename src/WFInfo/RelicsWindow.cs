using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WFInfo;

/// <summary>
/// Interaction logic for RelicsWindow.xaml
/// </summary>
public partial class RelicsWindow : Window
{
    private readonly RelicsViewModel _viewModel;

    public RelicsWindow(RelicsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void Hide(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    // Allows the dragging of the window
    private new void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void SingleClickExpand(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem tvi || e.Handled)
            return;

        tvi.IsExpanded = !tvi.IsExpanded;
        tvi.IsSelected = false;
        e.Handled = true;
    }

    private void WindowLoaded(object sender, RoutedEventArgs e)
    {
        // triggers when the window is first loaded, populates all the listviews once.
        _viewModel.InitializeTree();
    }
}
