using System.IO;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace WFInfo;

public sealed class EncryptedDataService(IDataProtectionProvider provider) : IEncryptedDataService
{
    private static readonly ILogger Logger = Log.Logger.ForContext<EncryptedDataService>();
    private static readonly string FileName = Path.Combine(Main.AppPath, "jwt_encrypted");

    private readonly IDataProtector _jwtProtector = provider.CreateProtector("WFInfo.JWT.v1");

    public string? LoadStoredJWT()
    {
        try
        {
            var fileText = File.ReadAllText(FileName);
            return _jwtProtector.Unprotect(fileText);
        }
        catch (FileNotFoundException e)
        {
            Logger.Error(e, "JWT not set");
        }
        catch (CryptographicException e)
        {
            Logger.Error(e, "JWT decryption failed");
        }

        return null;
    }

    public void PersistJWT(string? jwt)
    {
        if (jwt is null)
        {
            Logger.Warning("JWT is null, not persisting");
            return;
        }

        var encryptedJwt = _jwtProtector.Protect(jwt);
        File.WriteAllText(FileName, encryptedJwt);
    }
}