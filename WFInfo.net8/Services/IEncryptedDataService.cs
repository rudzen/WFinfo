namespace WFInfo;

public interface IEncryptedDataService
{
    string? LoadStoredJWT();
    void PersistJWT(string? jwt);
}