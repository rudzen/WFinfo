using System.IO;
using System.Reflection;

namespace WFInfo;

public static class ApplicationConstants
{
    public static string AppPath => $@"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\WFInfo";
    public static string AppPathDebug => Path.Combine(AppPath, "Debug");

    public static readonly string BuildVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();


}
