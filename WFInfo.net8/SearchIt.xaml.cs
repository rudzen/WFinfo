using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Serilog;
using WebSocketSharp;

namespace WFInfo;

/// <summary>
/// Interaction logic for SearchIt.xaml
/// </summary>
/// 
public partial class SearchIt : Window
{
    private static readonly ILogger Logger = Log.Logger.ForContext<SearchIt>();

    public SearchIt()
    {
        InitializeComponent();
    }

    public bool IsInUse { get; set; } = false;

    /// <summary>
    /// Launch snapit, prompts user if not logged in
    /// </summary>
    public void Start(Func<bool> isJwtLegal)
    {
        Main.SearchBox.Show();
        MainWindow.INSTANCE.Topmost = true;
        Main.SearchBox.placeholder.Content = "Search for warframe.market Items";
        if (!isJwtLegal())
        {
            Main.SearchBox.placeholder.Content = "Please log in first";
            Main.Login.MoveLogin(Left, Main.SearchBox.Top - 130);
            return;
        }

        MainWindow.INSTANCE.Topmost = false;
        IsInUse = true;
        Main.SearchBox.Show();
        searchField.Focusable = true;
        Main.SearchBox.Topmost = true;
        Win32.BringToFront(Process.GetCurrentProcess());
    }

    /// <summary>
    /// Stats a search, it will try to get the closest item from the search box and spawn a create listing screen
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Search(object sender, RoutedEventArgs e)
    {
        try
        {
            var closest = Main.DataBase.GetPartNameHuman(searchField.Text, out _);
            var primeRewards = new List<string> { closest };
            var rewardCollection = Task.Run(() => Main.ListingHelper.GetRewardCollection(primeRewards)).Result;
            Main.ListingHelper.ScreensList.Add(new KeyValuePair<string, RewardCollection>("", rewardCollection));
            if (!Main.ListingHelper.IsVisible)
            {
                Main.ListingHelper.SetScreen(Main.ListingHelper.ScreensList.Count - 1);
            }

            Main.ListingHelper.Show();
            Main.ListingHelper.BringIntoView();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to search");
        }

        Finish();
    }

    /// <summary>
    /// Reset the search box back to original status and then hide it
    /// </summary>
    internal void Finish()
    {
        searchField.Text = string.Empty;
        placeholder.Visibility = Visibility.Visible;
        searchField.Focusable = false;
        IsInUse = false;
        Hide();
    }

    /// <summary>
    /// Helper method to remove the placeholder text
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!searchField.Text.IsNullOrEmpty())
            placeholder.Visibility = Visibility.Hidden;
    }
}