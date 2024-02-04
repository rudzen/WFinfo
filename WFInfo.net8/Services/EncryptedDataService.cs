using System.IO;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Serilog;

namespace WFInfo;

public sealed class EncryptedDataService(IDataProtectionProvider provider) : IEncryptedDataService
{
    private static readonly ILogger Logger = Log.Logger.ForContext<EncryptedDataService>();
    private static readonly string FileName = Path.Combine(Main.AppPath, "jwt_encrypted");

    private readonly IDataProtector _jwtProtector = provider.CreateProtector("WFInfo.JWT.v1");

    public string? JWT { get; set; }

    public void LoadStoredJWT()
    {
        try
        {
            var fileText = File.ReadAllText(FileName);
            JWT = _jwtProtector.Unprotect(fileText);
        }
        catch (FileNotFoundException e)
        {
            Logger.Error(e, "JWT not set");
        }
        catch (CryptographicException e)
        {
            Logger.Error(e, "JWT decryption failed");
        }
    }

    public bool IsJwtLoggedIn()
    {
        //check if the token is of the right length
        return JWT is { Length: > 300 };
    }

    public void PersistJWT()
    {
        if (JWT is null)
        {
            Logger.Warning("JWT is null, not persisting");
            return;
        }

        var encryptedJwt = _jwtProtector.Protect(JWT);
        File.WriteAllText(FileName, encryptedJwt);
    }
}