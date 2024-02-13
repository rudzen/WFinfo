using System.IO;

namespace WFInfo;

public static class ApplicationConstants
{
    public static string AppPath => $@"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\WFInfo";
    public static string AppPathDebug => Path.Combine(AppPath, "Debug");
}