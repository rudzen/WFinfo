using Newtonsoft.Json.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mediator;
using Serilog;
using WFInfo.Domain;
using WFInfo.Extensions;

namespace WFInfo;

/// <summary>
/// Interaction logic for RelicsWindow.xaml
/// </summary>
public partial class EquipmentWindow : Window, INotificationHandler<EventWindowReloadItems>
{
    private static readonly ILogger Logger = Log.Logger.ForContext<EquipmentWindow>();

    private List<string> types = ["Warframes", "Primary", "Secondary", "Melee", "Archwing", "Companion"];
    private Dictionary<string, TreeNode>? primeTypes;
    private bool searchActive;
    private bool showAllEqmt;
    private int searchTimerDurationMS = 500;

    private static System.Windows.Forms.Timer searchTimer { get; set; } = new();

    private static string[]? searchText;
    public static EquipmentWindow INSTANCE { get; set; }

    public EquipmentWindow()
    {
        InitializeComponent();
        INSTANCE = this;
    }

    private void Hide(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    public void ReloadItems()
    {
        if (primeTypes is null)
            return;

        foreach (var category in primeTypes.Values)
        {
            foreach (var prime in category.Children)
            {
                foreach (var part in prime.Children)
                {
                    part.ReloadPartOwned(prime);
                }

                prime.GetSetInfo();
            }
        }

        EqmtTree.Items.Refresh();
    }

    private void Populate()
    {
        primeTypes = new Dictionary<string, TreeNode>();
        foreach (KeyValuePair<string, JToken> prime in Main.DataBase.EquipmentData)
        {
            var primeIndex = prime.Key.IndexOf("Prime");
            if (primeIndex != -1)
            {
                var primeName = prime.Key[..(primeIndex + 5)];
                var primeType = prime.Value["type"].ToObject<string>();
                var mastered = prime.Value["mastered"].ToObject<bool>();
                if (primeType.Contains("Sentinel") || primeType.Contains("Skin"))
                    primeType = "Companion";
                else if (primeType.Contains("Arch")) //Future proofing for Arch-Guns and Arch-Melee
                    primeType = "Archwing";

                if (!primeTypes.TryGetValue(primeType, out var value))
                {
                    var newType = new TreeNode(primeType, string.Empty, false, 0);
                    if (!types.Contains(primeType))
                        types.Add(primeType);
                    newType.SortNum = types.IndexOf(primeType);
                    value = newType;
                    primeTypes[primeType] = value;
                }

                var type = value;
                var primeNode = new TreeNode(primeName, prime.Value["vaulted"].ToObject<bool>() ? "Vaulted" : string.Empty,
                    mastered, 1);
                primeNode.MakeClickable(prime.Key);
                foreach (KeyValuePair<string, JToken> primePart in prime.Value["parts"].ToObject<JObject>())
                {
                    var partName = primePart.Key;
                    if (primePart.Key.IndexOf("Prime") + 6 < primePart.Key.Length)
                        partName = partName[(primePart.Key.IndexOf("Prime") + 6)..];

                    if (partName.Contains("Kubrow"))
                        partName = partName[(partName.IndexOf(" Blueprint") + 1)..];
                    var partNode = new TreeNode(partName,
                        primePart.Value["vaulted"].ToObject<bool>() ? "Vaulted" : string.Empty, false, 0);
                    partNode.MakeClickable(primePart.Key);
                    if (Main.DataBase.MarketData.TryGetValue(primePart.Key, out var marketValues))
                        partNode.SetPrimePart(marketValues["plat"].ToObject<double>(),
                            marketValues["ducats"].ToObject<int>(), primePart.Value["owned"].ToObject<int>(),
                            primePart.Value["count"].ToObject<int>());
                    else if (Main.DataBase.EquipmentData.TryGetValue(primePart.Key, out var job))
                    {
                        var plat = 0.0;
                        var ducats = 0.0;
                        foreach (KeyValuePair<string, JToken> subPartPart in job["parts"].ToObject<JObject>())
                        {
                            if (Main.DataBase.MarketData.TryGetValue(subPartPart.Key,
                                    out var subMarketValues))
                            {
                                var temp = subPartPart.Value["count"].ToObject<int>();
                                plat += temp * subMarketValues["plat"].ToObject<double>();
                                ducats += temp * subMarketValues["ducats"].ToObject<double>();
                            }
                        }

                        partNode.SetPrimeEqmt(plat, ducats, primePart.Value["owned"].ToObject<int>(),
                            primePart.Value["count"].ToObject<int>());
                    }
                    else
                    {
                        Logger.Debug("Couldn't find market values for part. name={PrimePart}", primePart.Key);
                        continue;
                    }

                    primeNode.AddChild(partNode);
                }

                if (primeNode.Children.Count > 0)
                {
                    primeNode.GetSetInfo();
                    type.AddChild(primeNode);
                }
            }
        }

        foreach (var typeName in types)
        {
            var primeType = primeTypes[typeName];
            primeType.ResetFilter();
            primeType.FilterOutVaulted();
            EqmtTree.Items.Add(primeType);
        }

        SortBoxChanged(null, null);
        RefreshVisibleRelics();
        Show();
        Focus();
    }

    // Allows the dragging of the window
    private new void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void SortBoxChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        EqmtTree.Items.SortDescriptions.Clear();

        foreach (var (_, value) in primeTypes)
        {
            value.Sort(SortBox.SelectedIndex, false);
            value.RecolorChildren();
        }

        if (showAllEqmt)
        {
            EqmtTree.Items.IsLiveSorting = true;
            switch (SortBox.SelectedIndex)
            {
                case 1:
                    EqmtTree.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("Plat_Val",
                        System.ComponentModel.ListSortDirection.Descending));
                    break;
                case 2:
                    EqmtTree.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("Diff_Val",
                        System.ComponentModel.ListSortDirection.Ascending));
                    break;
                case 3:
                    EqmtTree.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("Owned_Val",
                        System.ComponentModel.ListSortDirection.Descending));
                    break;
                case 4:
                    EqmtTree.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("Owned_Plat_Val",
                        System.ComponentModel.ListSortDirection.Descending));
                    break;
                case 5:
                    EqmtTree.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("Owned_Ducat_Val",
                        System.ComponentModel.ListSortDirection.Descending));
                    break;
                default:
                    EqmtTree.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("EqmtName_Sort",
                        System.ComponentModel.ListSortDirection.Ascending));
                    break;
            }

            var i = false;
            foreach (TreeNode prime in EqmtTree.Items)
            {
                i = !i;
                var brush = i ? TreeNode.BACK_D_BRUSH : TreeNode.BACK_U_BRUSH;
                prime.Background_Color = brush;
            }
        }

        EqmtTree.Items.Refresh();
    }

    private void VaultedClick(object sender, RoutedEventArgs e)
    {
        if (vaulted.IsChecked is true)
        {
            foreach (var (_, value) in primeTypes)
                value.FilterOutVaulted(true);

            RefreshVisibleRelics();
        }
        else
        {
            ReapplyFilters();
        }
    }

    /// <summary>
    /// Starts a timer to wait to apply changes to filters on search bar
    /// </summary>
    private void StartSearchReapplyTimer()
    {
        if (searchTimer.Enabled)
        {
            searchTimer.Stop();
        }

        searchTimer.Interval = searchTimerDurationMS;
        searchTimer.Enabled = true;
        searchTimer.Tick += (s, e) =>
        {
            searchTimer.Enabled = false;
            searchTimer.Stop();
            ReapplyFilters();
        };
    }

    private void TextboxTextChanged(object sender, TextChangedEventArgs e)
    {
        searchActive = textBox.Text.Length > 0 && textBox.Text != "Filter Terms";

        if (!textBox.IsLoaded)
            return;

        if (!searchActive && searchText is not { Length: > 0 })
            return;

        searchText = searchActive ? textBox.Text.Split(' ') : null;
        StartSearchReapplyTimer();
    }

    private void TextBoxFocus(object sender, RoutedEventArgs e)
    {
        if (!searchActive)
            textBox.Clear();
    }

    private void TextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (!searchActive)
            textBox.Text = "Filter Terms";
    }

    private void ToggleShowAllEqmt(object sender, RoutedEventArgs e)
    {
        showAllEqmt = !showAllEqmt;
        foreach (var value in primeTypes.Values)
        {
            foreach (var kid in value.Children)
                kid.TopLevel = showAllEqmt;
        }

        var content = showAllEqmt ? "All Equipment" : "Equipment Types";
        eqmtComboButton.Content = content;

        EqmtTree.Items.Clear();
        RefreshVisibleRelics();
    }

    private void SingleClickExpand(object sender, RoutedEventArgs e)
    {
        if (e.Handled)
            return;

        if (e.OriginalSource is not TreeViewItem tvi)
            return;

        tvi.IsExpanded ^= true;
        tvi.IsSelected = false;
        e.Handled = true;
    }

    private void ExpandAll(object sender, RoutedEventArgs e)
    {
        ExpandAll(true);
    }

    private void CollapseAll(object sender, RoutedEventArgs e)
    {
        ExpandAll(false);
    }

    private void ExpandAll(bool expand)
    {
        foreach (var primeType in primeTypes)
            primeType.Value.ChangeExpandedTo(expand);
    }

    private void RefreshVisibleRelics()
    {
        var index = 0;
        if (showAllEqmt)
        {
            List<TreeNode> activeNodes = [];
            foreach (var typeName in types)
            {
                var primeType = primeTypes[typeName];
                foreach (var eqmt in primeType.ChildrenFiltered)
                    activeNodes.Add(eqmt);
            }

            while (index < EqmtTree.Items.Count)
            {
                var eqmt = (TreeNode)EqmtTree.Items.GetItemAt(index);
                if (!activeNodes.Contains(eqmt))
                    EqmtTree.Items.RemoveAt(index);
                else
                {
                    activeNodes.Remove(eqmt);
                    index++;
                }
            }

            foreach (var eqmt in activeNodes)
                EqmtTree.Items.Add(eqmt);

            SortBoxChanged(null, null);
        }
        else
        {
            foreach (var typeName in types)
            {
                var primeType = primeTypes[typeName];
                var curr = EqmtTree.Items.IndexOf(primeType);
                if (primeType.ChildrenFiltered.Count == 0)
                {
                    if (curr != -1)
                        EqmtTree.Items.RemoveAt(curr);
                }
                else
                {
                    if (curr == -1)
                        EqmtTree.Items.Insert(index, primeType);

                    index++;
                }

                primeType.RecolorChildren();
            }
        }

        EqmtTree.Items.Refresh();
    }

    private void ReapplyFilters()
    {
        if (primeTypes is null)
            return;

        var isVaulted = vaulted.IsChecked is true;
        var hasSearchText = searchText is not null && searchText.Length != 0;
        foreach (var value in primeTypes.Values)
        {
            value.ResetFilter();
            if (isVaulted)
                value.FilterOutVaulted(true);
            if (hasSearchText)
                value.FilterSearchText(searchText.AsSpan(), false, true);
        }

        RefreshVisibleRelics();
    }

    private void WindowLoaded(object sender, RoutedEventArgs e)
    {
        Populate();
    }

    public ValueTask Handle(EventWindowReloadItems notification, CancellationToken cancellationToken)
    {
        Dispatcher.InvokeIfRequired(ReloadItems);
        return ValueTask.CompletedTask;
    }
}
