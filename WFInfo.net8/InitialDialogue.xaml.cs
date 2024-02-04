using System.Windows;
using System.Windows.Input;

namespace WFInfo;

/// <summary>
/// Interaction logic for errorDialogue.xaml
/// </summary>
public partial class InitialDialogue
{
    private int _filesTotal;
    private int _filesDone = 1;

    public InitialDialogue()
    {
        InitializeComponent();
    }

    // Allows the draging of the window
    private new void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Exit(object sender, EventArgs e)
    {
        CustomEntrypoint.stopDownloadTask.Cancel();
    }

    internal void SetFilesNeed(int filesNeeded)
    {
        _filesTotal = filesNeeded;
        Progress.Text = $"0% ({_filesDone}/{_filesTotal})";
        Progress.Visibility = Visibility.Visible;
    }

    internal void UpdatePercentage(double perc)
    {
        Progress.Text = $"{perc:F0}% ({_filesDone}/{_filesTotal})";
    }

    internal void FileComplete()
    {
        _filesDone++;
        Progress.Text = $"0% ({_filesDone}/{_filesTotal})";
    }
}