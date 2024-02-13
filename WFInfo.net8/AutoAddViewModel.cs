using System.Collections.ObjectModel;
using Serilog;

namespace WFInfo;

public sealed class AutoAddViewModel : INPC
{
    private static readonly ILogger Logger = Log.Logger.ForContext<AutoAddViewModel>();

    private ObservableCollection<AutoAddSingleItem> _itemList = [];

    public ObservableCollection<AutoAddSingleItem> ItemList
    {
        get => _itemList;
        private set
        {
            _itemList = value;
            RaisePropertyChanged();
        }
    }

    public void AddItem(AutoAddSingleItem item)
    {
        _itemList.Add(item);
        RaisePropertyChanged();
    }

    public void RemoveItem(AutoAddSingleItem item)
    {
        _itemList.Remove(item);
        RaisePropertyChanged();
    }
}

public class AutoAddSingleItem : INPC
{
    private static readonly ILogger Logger = Log.Logger.ForContext<AutoAddSingleItem>();

    public AutoAddViewModel? _parent;

    private readonly ObservableCollection<string> _rewardOptions;

    public ObservableCollection<string> RewardOptions
    {
        get => _rewardOptions;
        private init
        {
            _rewardOptions = value;
            RaisePropertyChanged();
        }
    }

    private string _activeOption;

    public string ActiveOption
    {
        get => _activeOption;
        set
        {
            _activeOption = value;
            RaisePropertyChanged();
        }
    }

    public SimpleCommand Increment { get; }

    public SimpleCommand Remove { get; }

    public AutoAddSingleItem(List<string> options, int activeIndex, AutoAddViewModel parent)
    {
        RewardOptions = new ObservableCollection<string>(options);
        activeIndex = Math.Min(RewardOptions.Count - 1, activeIndex);
        if (activeIndex >= 0 && options != null)
        {
            ActiveOption = options[activeIndex];
        }
        else
        {
            ActiveOption = string.Empty;
        }

        _parent = parent;
        Remove = new SimpleCommand(RemoveFromParent);
        Increment = new SimpleCommand(() => AddCount(true));
    }

    public async Task AddCount(bool save)
    {
        //get item count, increment, save
        bool saveFailed = false;
        string item = ActiveOption;
        if (item.Contains("Prime"))
        {
            string[] nameParts = item.Split(["Prime"], 2, StringSplitOptions.None);
            string primeName = $"{nameParts[0]}Prime";
            string partName = primeName + (nameParts[1].Length > 10 && !nameParts[1].Contains("Kubrow")
                ? nameParts[1].Replace(" Blueprint", string.Empty)
                : nameParts[1]);

            Logger.Debug("Incrementing owned amount for part {PartName}", partName);

            try
            {
                int count = Main.DataBase.EquipmentData[primeName]["parts"][partName]["owned"].ToObject<int>();

                Main.DataBase.EquipmentData[primeName]["parts"][partName]["owned"] = count + 1;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "FAILED to increment owned amount, Name: {Item}, primeName: {PrimeName}, partName: {PartName}", item, primeName, partName);
                saveFailed = true;
            }
        }

        if (saveFailed)
        {
            //shouldn't need Main.RunOnUIThread since this is already on the UI Thread
            //adjust for time diff between snap-it finishing and save being pressed, in case of long delay
            Main.RunOnUIThread(() =>
            {
                Main.SpawnErrorPopup(DateTime.UtcNow);
                Main.StatusUpdate("Failed to save one or more item, report to dev", 2);
            });
        }

        RemoveFromParent();
        if (save)
        {
            Main.DataBase.SaveAllJSONs();
            Main.RunOnUIThread(() =>
            {
                EquipmentWindow.INSTANCE.ReloadItems();
            });
        }
    }

    private void RemoveFromParent()
    {
        _parent?.RemoveItem(this);
        RaisePropertyChanged();
    }
}