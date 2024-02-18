namespace WFInfo.Services;

public interface ICsvCollection
{
    void AddRow(string fileName, string itemName, string plat, string ducats, string volume, bool vaulted, string owned, string partsDetected);
}
