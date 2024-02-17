using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Serilog;
using WebSocketSharp;
using WFInfo.Domain.Types;

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

    public bool IsInUse { get; set; }

    /// <summary>
    /// Launch snapit, prompts user if not logged in
    /// </summary>
    public void Start(Func<bool> isJwtLegal)
    {
        Main.SearchIt.Show();
        MainWindow.INSTANCE.Topmost = true;
        Main.SearchIt.Placeholder.Content = "Search for warframe.market Items";
        if (!isJwtLegal())
        {
            Main.SearchIt.Placeholder.Content = "Please log in first";
            Main.Login.MoveLogin(Left, Main.SearchIt.Top - 130);
            return;
        }

        MainWindow.INSTANCE.Topmost = false;
        IsInUse = true;
        Main.SearchIt.Show();
        SearchField.Focusable = true;
        Main.SearchIt.Topmost = true;
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
            var closest = Main.DataBase.GetPartNameHuman(SearchField.Text, out var _);
            var primeRewards = new List<string> { closest };
            var rewardCollection = Main.ListingHelper.GetRewardCollection(primeRewards).GetAwaiter().GetResult();
            Main.ListingHelper.ScreensList.Add(new RewardCollectionItem(string.Empty, rewardCollection));
            if (!Main.ListingHelper.IsVisible)
                Main.ListingHelper.SetScreen(Main.ListingHelper.ScreensList.Count - 1);

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
        SearchField.Text = string.Empty;
        Placeholder.Visibility = Visibility.Visible;
        SearchField.Focusable = false;
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
        if (!SearchField.Text.IsNullOrEmpty())
            Placeholder.Visibility = Visibility.Hidden;
    }
}
