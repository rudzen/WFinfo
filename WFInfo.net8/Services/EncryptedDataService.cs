using System.IO;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace WFInfo;

public sealed class EncryptedDataService
{
    private static readonly ILogger Logger = Log.Logger.ForContext<EncryptedDataService>();
    private static readonly IDataProtector JwtProtector;

    static EncryptedDataService()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddDataProtection();
        var services = serviceCollection.BuildServiceProvider();
        IDataProtectionProvider provider = services.GetService<IDataProtectionProvider>();
        JwtProtector = provider?.CreateProtector("WFInfo.JWT.v1");
    }

    public static string LoadStoredJWT()
    {
        try
        {
            var fileText = File.ReadAllText(Main.AppPath + @"\jwt_encrypted");
            return JwtProtector?.Unprotect(fileText);
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

    public static void PersistJWT(string jwt)
    {
        var encryptedJWT = JwtProtector?.Protect(jwt);
        File.WriteAllText(Main.AppPath + @"\jwt_encrypted", encryptedJWT);
    }
}