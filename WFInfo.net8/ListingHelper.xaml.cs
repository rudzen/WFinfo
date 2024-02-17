using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json.Linq;
using Serilog;
using WFInfo.Domain.Types;
using WFInfo.Services;

namespace WFInfo;

/// <summary>
/// Interaction logic for CreateListing.xaml
/// </summary>
public partial class ListingHelper : Window
{
    private static readonly ILogger Logger = Log.Logger.ForContext<ListingHelper>();

    public List<RewardCollectionItem> ScreensList { get; } = [];

    public List<List<string>> PrimeRewards { get; } = [];

    public short SelectedRewardIndex { get; set; }

    //Helper, allowing to store the rewards until needed to be processed
    private int _pageIndex;
    private bool _updating;

    private const int SuccessHeight = 180;
    private const int FailedHeight = 270;
    private const int NormalHeight = 255;

    #region default methods

    public ListingHelper()
    {
        InitializeComponent();
    }

    private void Minimize(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Close(object sender, RoutedEventArgs e)
    {
        Hide();
        ScreensList.Clear();
        _pageIndex = 0;
    }

    // Allows the draging of the window
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    #endregion

    #region frontend

    /// <summary>
    /// Sets the screen to one of the screen-lists indicated by it's index
    /// </summary>
    /// <param name="index">The index needed for the screen</param>
    public void SetScreen(int index)
    {
        Logger.Debug("Setting screen index. count={Count},index={Index}", ScreensList.Count, index);

        if (ScreensList.Count == 0)
            Hide();
        if (ScreensList.Count < index || 0 > index)
            throw new Exception("Tried setting screen to an item that didn't exist");

        var screen = ScreensList[index];
        _updating = true;
        ComboBox.Items.Clear();
        ComboBox.SelectedIndex = screen.Collection.RewardIndex;
        foreach (var primeItem in screen.Collection.PrimeNames.Where(primeItem => !string.IsNullOrEmpty(primeItem)))
            ComboBox.Items.Add(primeItem);

        SetCurrentStatus();
        SetListings(screen.Collection.RewardIndex);
        _updating = false;
    }

    /// <summary>
    /// changes screen over if there is a follow up screen
    /// </summary>
    private void NextScreen(object sender, RoutedEventArgs e)
    {
        Back.IsEnabled = true;
        if (PrimeRewards.Count > 0)
        {
            // if there are new prime rewards
            try
            {
                Next.Content = "...";
                // TODO (rudzen) : fix this .Result
                var rewardCollection = Main.ListingHelper.GetRewardCollection(PrimeRewards[0]).GetAwaiter().GetResult();
                if (rewardCollection.PrimeNames.Count != 0)
                    Main.ListingHelper.ScreensList.Add( new(string.Empty, rewardCollection));
                PrimeRewards.RemoveAt(0);
                Next.Content = "Next";
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Error thrown in NextScreen. rewardCount={Count}, SelectedRewardIndex={Index}", PrimeRewards.Count, SelectedRewardIndex);
                throw;
            }
        }

        if (ScreensList.Count - 1 == _pageIndex) //reached the end of the list
        {
            Next.IsEnabled = false;
            return;
        }

        _pageIndex++;
        SetScreen(_pageIndex);
    }

    /// <summary>
    /// changes screen back if there is a previous screen
    /// </summary>
    private void PreviousScreen(object sender, RoutedEventArgs e)
    {
        Next.IsEnabled = true;

        Logger.Debug("There are {ScreensList} screens and: {PrimeRewards} prime rewards. Currently on screen {PageIndex} and trying to go to the previous screen",
            ScreensList.Count, PrimeRewards.Count, _pageIndex);

        if (_pageIndex == 0)
        {
            //reached start of the list
            Back.IsEnabled = false;
            return;
        }

        _pageIndex--;
        SetScreen(_pageIndex);
    }

    /// <summary>
    /// Updates the screen to reflect status
    /// </summary>
    private void SetCurrentStatus()
    {
        Logger.Debug("Current status is: {Status}",ScreensList[_pageIndex].Key);
        switch (ScreensList[_pageIndex].Key)
        {
            //listing already successfully posted
            case "successful":
                ListingGrid.Visibility = Visibility.Collapsed;
                Height = SuccessHeight;
                ConfirmListingButton.IsEnabled = false;
                Status.Content = "Listing already successfully posted";
                Status.Visibility = Visibility.Visible;
                ComboBox.IsEnabled = false;
                ConfirmListingButton.IsEnabled = false;
                break;
            case "": //listing is not yet assigned anything
                Height = NormalHeight;
                Status.Visibility = Visibility.Collapsed;
                ListingGrid.Visibility = Visibility.Visible;
                ComboBox.IsEnabled = true;
                ConfirmListingButton.IsEnabled = true;
                break;
            default: //an error occured.
                Height = FailedHeight;
                Status.Content = ScreensList[_pageIndex].Key;
                Status.Visibility = Visibility.Visible;
                ListingGrid.Visibility = Visibility.Visible;
                ComboBox.IsEnabled = true;
                ConfirmListingButton.IsEnabled = true;
                break;
        }
    }

    /// <summary>
    /// List the current selected prime item with it's currently filled in plat value.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ConfirmListing(object sender, RoutedEventArgs e)
    {
        Logger.Debug("Trying to place listing");
        try
        {
            var primeItem = (string)ComboBox.Items[ComboBox.SelectedIndex];
            var platinum = int.Parse(PlatinumTextBox.Text, ApplicationConstants.Culture);
            var success = Task.Run(async () => await PlaceListing(primeItem, platinum)).Result;
            if (success)
            {
                var newEntry = new RewardCollectionItem("successful", ScreensList[_pageIndex].Collection);
                ScreensList.RemoveAt(_pageIndex);
                ScreensList.Insert(_pageIndex, newEntry);
                ConfirmListingButton.IsEnabled = true;
            }
            else
            {
                var newEntry = new RewardCollectionItem("Something uncaught went wrong",
                    ScreensList[_pageIndex].Collection);
                ScreensList.RemoveAt(_pageIndex);
                ScreensList.Insert(_pageIndex, newEntry);
            }

            SetCurrentStatus();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to place listing");
            var newEntry = new RewardCollectionItem(exception.ToString(), ScreensList[_pageIndex].Collection);
            ScreensList.RemoveAt(_pageIndex);
            ScreensList.Insert(_pageIndex, newEntry);
        }
    }

    /// <summary>
    /// Changes the top 5 listings when the user selects a new item
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!ComboBox.IsLoaded || _updating) //Prevent firing off to early
            return;
        SetListings(ComboBox.SelectedIndex);
    }

    /// <summary>
    /// Sets the listing to the current selected prime item
    /// </summary>
    /// <param name="index">the currently selected prime item</param>
    private void SetListings(int index)
    {
        Logger.Debug("There are {Count} of plat values, setting index to: {Index}", ScreensList[_pageIndex].Collection.PrimeNames.Count, index);

        PlatinumTextBox.Text = ScreensList[_pageIndex].Collection.PlatinumValues[index].ToString(ApplicationConstants.Culture);

        ListingGrid.Visibility = Visibility.Visible;
        Height = 255;
        Status.Content = ScreensList[_pageIndex].Key;
        Status.Visibility = Visibility.Collapsed;
        ComboBox.IsEnabled = true;
        ConfirmListingButton.IsEnabled = true;
        PlatinumTextBox.IsEnabled = true;

        Platinum0.Content = ScreensList[_pageIndex].Collection.MarketListings[index][0].Platinum;
        Amount0.Content = ScreensList[_pageIndex].Collection.MarketListings[index][0].Amount;
        Reputation0.Content = ScreensList[_pageIndex].Collection.MarketListings[index][0].Reputation;

        Platinum1.Content = ScreensList[_pageIndex].Collection.MarketListings[index][1].Platinum;
        Amount1.Content = ScreensList[_pageIndex].Collection.MarketListings[index][1].Amount;
        Reputation1.Content = ScreensList[_pageIndex].Collection.MarketListings[index][1].Reputation;

        Platinum2.Content = ScreensList[_pageIndex].Collection.MarketListings[index][2].Platinum;
        Amount2.Content = ScreensList[_pageIndex].Collection.MarketListings[index][2].Amount;
        Reputation2.Content = ScreensList[_pageIndex].Collection.MarketListings[index][2].Reputation;

        Platinum3.Content = ScreensList[_pageIndex].Collection.MarketListings[index][3].Platinum;
        Amount3.Content = ScreensList[_pageIndex].Collection.MarketListings[index][3].Amount;
        Reputation3.Content = ScreensList[_pageIndex].Collection.MarketListings[index][3].Reputation;

        Platinum4.Content = ScreensList[_pageIndex].Collection.MarketListings[index][4].Platinum;
        Amount4.Content = ScreensList[_pageIndex].Collection.MarketListings[index][4].Amount;
        Reputation4.Content = ScreensList[_pageIndex].Collection.MarketListings[index][4].Reputation;

        if (!IsItemBanned(ScreensList[_pageIndex].Collection.PrimeNames[index])) return;
        ListingGrid.Visibility = Visibility.Collapsed;
        Height = 180;
        Status.Content = "Cannot list this item";
        Status.Visibility = Visibility.Visible;
        ComboBox.IsEnabled = true;
        PlatinumTextBox.IsEnabled = false;
        ConfirmListingButton.IsEnabled = false;
    }

    /// <summary>
    /// Cancels the current selection, removing it from the list
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Cancel(object sender, RoutedEventArgs e)
    {
        if (ScreensList.Count == 1 || ScreensList.Count == 0)
        {
            // if it's the last item
            Close(null, null);
            return;
        }

        if (_pageIndex == 0) // if looking at the first screen
        {
            SetScreen(1);
            ScreensList.RemoveAt(0);
        }
        else
        {
            ScreensList.RemoveAt(_pageIndex);
            --_pageIndex;
            SetScreen(_pageIndex);
        }
    }

    public void ShowLoading()
    {
        CancelButton.Content = "loading";
        Next.IsEnabled = false;
        Back.IsEnabled = false;
    }

    public void ShowFinished()
    {
        CancelButton.Content = "Cancel";
        Next.IsEnabled = true;
        Back.IsEnabled = true;
    }

    #endregion

    /// <summary>
    /// returns the data for an entire "Create listing" screen
    /// </summary>
    /// <param name="primeNames">The human friendly name to search listings for</param>
    /// <returns>the data for an entire "Create listing" screen</returns>
    public async Task<RewardCollection> GetRewardCollection(List<string> primeNames)
    {
        var platinumValues = new List<short>(4);
        var marketListings = new List<List<MarketListing>>(5);
        var index = SelectedRewardIndex;
        SelectedRewardIndex = 0;
        if (primeNames == null)
        {
            throw new ArgumentNullException(nameof(primeNames));
        }

        foreach (var primeItem in primeNames)
        {
            try
            {
                var tempListings = await GetMarketListing(primeItem);
                marketListings.Add(tempListings);
                platinumValues.Add(tempListings[0].Platinum);
            }
            catch (Exception e)
            {
                Main.RunOnUIThread(() =>
                {
                    Main.SearchIt.placeholder.Content = $"Could not find {primeItem}";
                    Main.SearchIt.searchField.Text = string.Empty;
                });
            }
        }

        return new RewardCollection(primeNames, platinumValues, marketListings, index);
    }

    private static bool IsItemBanned(string item)
    {
        return item.Contains("kuva", StringComparison.CurrentCultureIgnoreCase) ||
               item.Contains("exilus", StringComparison.CurrentCultureIgnoreCase) ||
               item.Contains("riven", StringComparison.CurrentCultureIgnoreCase) ||
               item.Contains("ayatan", StringComparison.CurrentCultureIgnoreCase) ||
               item.Contains("forma", StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Gets the top 5 current market listings
    /// </summary>
    /// <param name="primeName">The human friendly name to search listings for</param>
    /// <returns>the top 5 current market listings</returns>
    private static async Task<List<MarketListing>> GetMarketListing(string primeName)
    {
        if (IsItemBanned(primeName))
        {
            var bannedListing = new List<MarketListing>();
            for (var i = 0; i < 5; i++)
            {
                bannedListing.Add(new MarketListing(0, 0, 0));
            }

            return bannedListing;
        }

        Logger.Debug("Getting listing for {PrimeName}", primeName);
        var listings = new List<MarketListing>();
        var possibleTopListings = await Main.DataBase.GetTopListings(primeName);

        if (!possibleTopListings.TryGet(out var topListings))
        {
            Log.Debug("No results found for {PrimeName}", primeName);
            return listings;
        }

        var payload = topListings["payload"];

        if (payload is null)
        {
            Log.Debug("Payload was null for {PrimeName}", primeName);
            return listings;
        }

        var sellOrders = new JArray(payload["sell_orders"].Children());
        foreach (var item in sellOrders)
        {
            var platinum = item.Value<short>("platinum");
            var amount = item.Value<short>("quantity");
            var reputation = item["user"].Value<short>("reputation");
            var listing = new MarketListing(platinum, amount, reputation);
            Logger.Debug("Getting listing for {Listing}", listing.ToHumanString());
            listings.Add(listing);
        }

        return listings;
    }

    /// <summary>
    /// Tries to post the current screen to wfm
    /// </summary>
    /// <returns>if it succeeded</returns>
    private static async Task<bool> PlaceListing(string primeItem, int platinum)
    {
        var potentialListing = await Main.DataBase.GetCurrentListing(primeItem);

        if (!potentialListing.TryGet(out var listing))
            return await Main.DataBase.ListItem(primeItem, platinum, 1);

        //listing already exists, thus update it
        var listingId = listing["id"].ToString();
        var quantity = (int)listing["quantity"];
        return await Main.DataBase.UpdateListing(listingId, platinum, quantity);
    }

    private void PlatinumTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        PlatinumTextBox.Text = PlatinumReplaceRegEx().Replace(PlatinumTextBox.Text, string.Empty);
    }

    [GeneratedRegex("[^0-9.]")]
    private static partial Regex PlatinumReplaceRegEx();
}

/// <summary>
/// Class to represent a single "
/// " of the create listing screen, consisting of up to 4 possible rewards for which are unique plat, quantity and market listings
/// </summary>
public class RewardCollection
{
    public List<string> PrimeNames { get; set; } =
        new List<string>(4); // the reward items in case user wants to change selection

    public List<short> PlatinumValues { get; set; } = new List<short>(4);
    public List<List<MarketListing>> MarketListings { get; set; } = new List<List<MarketListing>>(5);
    public short RewardIndex { get; set; }

    public RewardCollection(List<string> primeNames, List<short> platinumValues,
        List<List<MarketListing>> marketListings, short rewardIndex)
    {
        PrimeNames = primeNames;
        PlatinumValues = platinumValues;
        MarketListings = marketListings;
        RewardIndex = rewardIndex;
    }

    /// <summary>
    /// Gets a human friendly version back for logging.
    /// </summary>
    /// <returns></returns>
    public string ToHumanString()
    {
        var msg = "Reward collection screen:\n";
        foreach (var item in PrimeNames)
        {
            if (string.IsNullOrEmpty(item))
                continue;
            var index = PrimeNames.IndexOf(item);

            msg += $"Prime item: \"{item}\", Platinum value: \"{PlatinumValues[index]}\",  Market listings: \n";


            msg = MarketListings[index]
                .Aggregate(msg, (current, listing) => current + (listing.ToHumanString() + "\n"));
        }

        return msg;
    }
}

/// <summary>
/// Class to represent a single listing of an item, usually comes in groups of 5
/// </summary>
public class MarketListing
{
    public short Platinum { get; set; }   // plat amount of listing
    public short Amount { get; set; }     //amount user lists
    public short Reputation { get; set; } // user's reputation

    public MarketListing(short platinum, short amount, short reputation)
    {
        Platinum = platinum;
        Amount = amount;
        Reputation = reputation;
    }

    /// <summary>
    /// Gets a human friendly version back for logging.
    /// </summary>
    /// <returns></returns>
    public string ToHumanString()
    {
        return "Platinum: " + Platinum + " Amount: " + Amount + " Reputation: " + Reputation;
    }
}
