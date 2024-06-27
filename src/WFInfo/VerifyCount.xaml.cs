using Newtonsoft.Json.Linq;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Mediator;
using Serilog;
using WFInfo.Domain;
using WFInfo.Services;
using WFInfo.Services.OpticalCharacterRecognition;

namespace WFInfo;

/// <summary>
/// Interaction logic for verifyCount.xaml
/// </summary>
public partial class VerifyCount
{
    private static readonly ILogger Logger = Log.Logger.ForContext<VerifyCount>();

    private static string itemPath = Path.Combine(ApplicationConstants.AppPath, "eqmt_data.json");
    private static string backupPath = Path.Combine(ApplicationConstants.AppPath, "eqmt_data.json.bak");

    private static VerifyCount? INSTANCE;
    private List<InventoryItem> _latestSnap;
    private DateTime _triggerTime;
    private readonly IPublisher _publisher;

    public VerifyCount(IPublisher publisher)
    {
        InitializeComponent();
        _latestSnap = [];
        _triggerTime = DateTime.UtcNow;
        _publisher = publisher;
    }

    public static void ShowVerifyCount(List<InventoryItem> itemList)
    {
        if (INSTANCE == null)
            return;

        INSTANCE._latestSnap = itemList;
        INSTANCE._triggerTime = DateTime.UtcNow;
        INSTANCE.BackupButton.Visibility = Visibility.Visible;
        INSTANCE.Show();
        INSTANCE.Focus();
    }

    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        var saveFailed = false;
        const string prime = "Prime";
        var primes = _latestSnap.Where(x => x.Name.Contains(prime));
        foreach (var item in primes)
        {
            var nameParts = item.Name.Split([prime], 2, StringSplitOptions.RemoveEmptyEntries);
            var primeName = nameParts[0] + prime;
            var partName = primeName + (nameParts[1].Length > 10 && !nameParts[1].Contains("Kubrow")
                ? nameParts[1].Replace(" Blueprint", string.Empty)
                : nameParts[1]);

            Logger.Debug("Saving item. count={Count},name={Name}", item.Count, partName);
            try
            {
                Main.DataBase.EquipmentData[primeName]["parts"][partName]["owned"] = item.Count;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to save count. count={Count},name={Name},primeName={PrimeName},partName={PartName}", item.Count, item.Name, primeName, partName);
                saveFailed = true;
            }
        }

        Main.DataBase.SaveAll(DataTypes.All);
        EquipmentWindow.INSTANCE.ReloadItems();
        if (saveFailed)
        {
            //adjust for time diff between snap-it finishing and save being pressed, in case of long delay
            var gab = DateTime.UtcNow.Subtract(_triggerTime);
            await _publisher.Publish(new ErrorDialogShow(DateTime.UtcNow, (int)gab.TotalSeconds + 30));
            await _publisher.Publish(new UpdateStatus("Failed to save one or more item, report to dev", StatusSeverity.Warning));
        }

        Hide();
    }

    private void BackupClick(object sender, RoutedEventArgs e)
    {
        if (File.Exists(backupPath))
            File.Delete(backupPath);

        File.Copy(itemPath, backupPath);
        foreach (KeyValuePair<string, JToken> prime in Main.DataBase.EquipmentData)
        {
            var primeIndex = prime.Key.IndexOf("Prime");
            if (primeIndex == -1)
                continue;

            var primeName = prime.Key[..(primeIndex + 5)];
            foreach (KeyValuePair<string, JToken> primePart in prime.Value["parts"].ToObject<JObject>())
            {
                var partName = primePart.Key;
                Main.DataBase.EquipmentData[primeName]["parts"][partName]["owned"] = 0;
            }
        }

        BackupButton.Visibility = Visibility.Hidden;
        Main.DataBase.SaveAll(DataTypes.All);
        EquipmentWindow.INSTANCE.ReloadItems();
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    // Allows the dragging of the window
    private new void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}
