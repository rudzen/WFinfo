using System.Globalization;
using System.IO;
using System.Reflection;

namespace WFInfo.Services;

public static class ApplicationConstants
{
    public static string AppPath => $@"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\WFInfo";
    public static string AppPathDebug => Path.Combine(AppPath, "Debug");

    public static string BuildVersion => Assembly.GetExecutingAssembly().GetName().Version.ToString();

    public static string MajorBuildVersion { get; }

    public static CultureInfo Culture => new("en", false);

    public const string DateFormat = "MMM dd - HH:mm";

    static ApplicationConstants()
    {
        var version = BuildVersion;
        var splitIndex = version.LastIndexOf('.');
        MajorBuildVersion = version[..splitIndex];
    }
}
