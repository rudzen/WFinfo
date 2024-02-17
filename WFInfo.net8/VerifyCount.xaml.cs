using Newtonsoft.Json.Linq;
using System.IO;
using System.Windows;
using System.Windows.Input;
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

    private static string itemPath = ApplicationConstants.AppPath   + @"\eqmt_data.json";
    private static string backupPath = ApplicationConstants.AppPath + @"\eqmt_data.json.bak";

    private List<InventoryItem> latestSnap;
    private static VerifyCount? INSTANCE;
    private DateTime triggerTime;

    public VerifyCount()
    {
        InitializeComponent();
        INSTANCE = this;
        latestSnap = [];
        triggerTime = DateTime.UtcNow;
    }

    public static void ShowVerifyCount(List<InventoryItem> itemList)
    {
        if (INSTANCE == null)
            return;

        INSTANCE.latestSnap = itemList;
        INSTANCE.triggerTime = DateTime.UtcNow;
        INSTANCE.BackupButton.Visibility = Visibility.Visible;
        INSTANCE.Show();
        INSTANCE.Focus();
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        var saveFailed = false;
        const string prime = "Prime";
        var primes = latestSnap.Where(x => x.Name.Contains(prime));
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
            Main.SpawnErrorPopup(DateTime.UtcNow, (int)((DateTime.UtcNow - triggerTime).TotalSeconds) + 30);
            Main.StatusUpdate("Failed to save one or more item, report to dev", StatusSeverity.Warning);
        }

        Hide();
    }

    private void BackupClick(object sender, RoutedEventArgs e)
    {
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }

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
