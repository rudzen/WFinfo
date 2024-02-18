using System.Collections.ObjectModel;
using System.Windows;
using Mediator;
using Serilog;
using WFInfo.Domain;
using WFInfo.Extensions;

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

    public AutoAddSingleItem(
        List<string> options,
        int activeIndex,
        AutoAddViewModel parent,
        IMediator mediator)
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
        Increment = new SimpleCommand(() => AddCount(true, mediator));
    }

    public async Task AddCount(bool save, IMediator mediator)
    {
        //get item count, increment, save
        var saveFailed = false;
        var item = ActiveOption;
        if (item.Contains("Prime"))
        {
            var nameParts = item.Split(["Prime"], 2, StringSplitOptions.None);
            var primeName = $"{nameParts[0]}Prime";
            var partName = primeName + (nameParts[1].Length > 10 && !nameParts[1].Contains("Kubrow")
                ? nameParts[1].Replace(" Blueprint", string.Empty)
                : nameParts[1]);

            Logger.Debug("Incrementing owned amount for part {PartName}", partName);

            try
            {
                var primeDataPartsOwned = Main.DataBase.EquipmentData[primeName]["parts"][partName];
                var count = primeDataPartsOwned["owned"].ToObject<int>();
                primeDataPartsOwned["owned"] = count + 1;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "FAILED to increment owned amount, Name: {Item}, primeName: {PrimeName}, partName: {PartName}", item, primeName, partName);
                saveFailed = true;
            }
        }

        if (saveFailed)
        {
            await mediator.Publish(new UpdateStatus("Failed to save one or more item, report to dev", StatusSeverity.Warning));
            //adjust for time diff between snap-it finishing and save being pressed, in case of long delay
            await mediator.Publish(new ErrorDialogShow(DateTime.UtcNow));
        }

        RemoveFromParent();
        if (save)
        {
            Main.DataBase.SaveAll(DataTypes.All);
            await mediator.Publish(EventWindowReloadItems.Instance);
        }
    }

    private void RemoveFromParent()
    {
        _parent?.RemoveItem(this);
        RaisePropertyChanged();
    }
}
