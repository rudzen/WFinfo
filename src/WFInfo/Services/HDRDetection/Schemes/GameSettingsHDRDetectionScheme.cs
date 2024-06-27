using System.IO;
using Serilog;

namespace WFInfo.Services.HDRDetection.Schemes;

public sealed class GameSettingsHDRDetectionScheme : IHDRDetectionScheme
{
    private static readonly ILogger Logger = Log.ForContext<GameSettingsHDRDetectionScheme>();

    private readonly string _configurationFile;

    public GameSettingsHDRDetectionScheme()
    {
        var localAppdata = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _configurationFile = Path.Combine(localAppdata, "Warframe", "EE.cfg");
    }

    public HDRDetectionSchemeResult Detect()
    {
        if (File.Exists(_configurationFile))
        {
            var contents = File.ReadAllText(_configurationFile);
            var containsEnable = contents.Contains("Graphics.HDROutput=1");

            Logger.Debug("Found Warframe configuration. HDR={Hdr},file={ConfigurationFile}",
                containsEnable, _configurationFile);

            // if containsEnable is true, then it's 100% HDR, otherwise it could still be Auto HDR
            return new HDRDetectionSchemeResult(containsEnable, containsEnable);
        }

        Logger.Debug("Failed to find Warframe configuration file at {ConfigurationFile}", _configurationFile);

        // Could still be Auto HDR with old engine?
        return new HDRDetectionSchemeResult(false, false);
    }
}