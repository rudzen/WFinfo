using System.ComponentModel;
using System.Drawing;
using System.Windows.Data;
using Newtonsoft.Json.Linq;
using WebSocketSharp;
using WFInfo.Extensions;

namespace WFInfo;

public class RelicsViewModel : INPC
{
    public RelicsViewModel()
    {
        RelicsItemsView = new ListCollectionView(_relicTreeItems);
        ExpandAllCommand = new SimpleCommand(() => ExpandOrCollapseAll(true));
        CollapseAllCommand = new SimpleCommand(() => ExpandOrCollapseAll(false));
    }

    private bool _initialized;
    private string _filterText = string.Empty;
    private int searchTimerDurationMS = 500;
    private bool _showAllRelics;
    private readonly List<TreeNode> _relicTreeItems = [];
    private int _sortBoxSelectedIndex;
    private bool _hideVaulted = true;
    private readonly List<TreeNode> _rawRelicNodes = [];

    private static System.Windows.Forms.Timer searchTimer = new();

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

    public string FilterText
    {
        get => _filterText;
        set
        {
            this.SetField(ref _filterText, value);
            StartSearchReapplyTimer();
            RaisePropertyChanged(nameof(IsFilterEmpty));
        }
    }

    public bool IsFilterEmpty => FilterText.IsNullOrEmpty();

    public SimpleCommand ExpandAllCommand { get; }
    public SimpleCommand CollapseAllCommand { get; }

    private void ExpandOrCollapseAll(bool expand)
    {
        foreach (var era in _rawRelicNodes)
            era.ChangeExpandedTo(expand);
    }

    public bool ShowAllRelics
    {
        get => _showAllRelics;
        set
        {
            foreach (var era in _rawRelicNodes)
            {
                foreach (var relic in era.Children)
                    relic.TopLevel = value;
            }

            SetField(ref _showAllRelics, value);

            _relicTreeItems.Clear();
            RefreshVisibleRelics();
            RaisePropertyChanged(nameof(ShowAllRelicsText));
        }
    }

    public string ShowAllRelicsText => ShowAllRelics ? "All Relics" : "Relic Eras";

    public bool HideVaulted
    {
        get => _hideVaulted;
        set
        {
            SetField(ref _hideVaulted, value);
            ReapplyFilters();
        }
    }

    public ICollectionView RelicsItemsView { get; }

    public int SortBoxSelectedIndex
    {
        get => _sortBoxSelectedIndex;
        set
        {
            SetField(ref _sortBoxSelectedIndex, value);
            SortBoxChanged();
        }
    }

    public void SortBoxChanged()
    {
        // 0 - Name
        // 1 - Average intact plat
        // 2 - Average radiant plat
        // 3 - Difference (radiant-intact)

        foreach (var era in _rawRelicNodes)
        {
            era.Sort(SortBoxSelectedIndex);
            era.RecolorChildren();
        }

        if (ShowAllRelics)
        {
            RelicsItemsView.SortDescriptions.Clear();
            //TODO:
            //_relicTreeItems.IsLiveSorting = true;
            switch (SortBoxSelectedIndex)
            {
                case 1:
                    RelicsItemsView.SortDescriptions.Add(new SortDescription("Intact_Val", ListSortDirection.Descending));
                    break;
                case 2:
                    RelicsItemsView.SortDescriptions.Add(new SortDescription("Radiant_Val", ListSortDirection.Descending));
                    break;
                case 3:
                    RelicsItemsView.SortDescriptions.Add(new SortDescription("Bonus_Val", ListSortDirection.Descending));
                    break;
                default:
                    RelicsItemsView.SortDescriptions.Add(new SortDescription("Name_Sort", ListSortDirection.Ascending));
                    break;
            }

            var backgrounds = new[]
            {
                TreeNode.BACK_U_BRUSH,
                TreeNode.BACK_D_BRUSH
            };

            var i = false;
            foreach (var relic in _relicTreeItems)
            {
                i = !i;
                relic.Background_Color = backgrounds[i.AsByte()];
            }
        }
    }

    private void RefreshVisibleRelics()
    {
        var index = 0;
        if (ShowAllRelics)
        {
            List<TreeNode> activeNodes = [];
            foreach (var era in _rawRelicNodes)
                activeNodes.AddRange(era.ChildrenFiltered);

            while (index < _relicTreeItems.Count)
            {
                var relic = (TreeNode)_relicTreeItems[index];
                if (!activeNodes.Contains(relic))
                {
                    _relicTreeItems.RemoveAt(index);
                }
                else
                {
                    activeNodes.Remove(relic);
                    index++;
                }
            }

            foreach (var relic in activeNodes)
                _relicTreeItems.Add(relic);

            SortBoxChanged();
        }
        else
        {
            foreach (var era in _rawRelicNodes)
            {
                var curr = _relicTreeItems.IndexOf(era);
                if (era.ChildrenFiltered.Count == 0)
                {
                    if (curr != -1)
                        _relicTreeItems.RemoveAt(curr);
                }
                else
                {
                    if (curr == -1)
                        _relicTreeItems.Insert(index, era);

                    index++;
                }

                era.RecolorChildren();
            }
        }

        RelicsItemsView.Refresh();
    }

    public void ReapplyFilters()
    {
        foreach (var era in _rawRelicNodes)
        {
            era.ResetFilter();
            if (HideVaulted)
                era.FilterOutVaulted(true);
            if (!FilterText.IsNullOrEmpty())
            {
                var searchText = FilterText.Split(' ');
                era.FilterSearchText(searchText, false, true);
            }
        }

        RefreshVisibleRelics();
    }

    public void InitializeTree()
    {
        if (_initialized)
        {
            return;
        }

        var lith = new TreeNode("Lith", "", false, 0);
        var meso = new TreeNode("Meso", "", false, 0);
        var neo = new TreeNode("Neo", "", false, 0);
        var axi = new TreeNode("Axi", "", false, 0);
        _rawRelicNodes.AddRange(new[] { lith, meso, neo, axi });
        var eraNum = 0;
        foreach (var head in _rawRelicNodes)
        {
            double sumIntact = 0;
            double sumRad = 0;

            head.SortNum = eraNum++;
            foreach (JProperty prop in Main.DataBase.RelicData[head.Name])
            {
                var primeItems = (JObject)Main.DataBase.RelicData[head.Name][prop.Name];
                var vaulted = primeItems["vaulted"].ToObject<bool>() ? "vaulted" : "";
                var relic = new TreeNode(prop.Name, vaulted, false, 0);
                relic.Era = head.Name;
                foreach (KeyValuePair<string, JToken> kvp in primeItems)
                {
                    if (kvp.Key != "vaulted" &&
                        Main.DataBase.MarketData.TryGetValue(kvp.Value.ToString(), out var marketValues))
                    {
                        var part = new TreeNode(kvp.Value.ToString(), "", false, 0);
                        part.SetPartText(marketValues["plat"].ToObject<double>(),
                            marketValues["ducats"].ToObject<int>(), kvp.Key);
                        relic.AddChild(part);
                    }
                }

                relic.SetRelicText();
                head.AddChild(relic);

                //groupedByAll.Items.Add(relic);
                //Search.Items.Add(relic);
            }

            head.SetEraText();
            head.ResetFilter();
            head.FilterOutVaulted();
            head.RecolorChildren();
        }

        RefreshVisibleRelics();
        SortBoxChanged();
        _initialized = true;
    }
}
