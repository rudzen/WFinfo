namespace WFInfo.Services;

public interface IEncryptedDataService
{
    string? JWT { get; set; }
    void LoadStoredJWT();
    bool IsJwtLoggedIn();
    void PersistJWT();
}