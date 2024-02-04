using System.IO;
using System.Net;
using Newtonsoft.Json.Linq;
using Tesseract;
using WFInfo.Settings;

namespace WFInfo;

public interface ITesseractService : IDisposable
{
    /// <summary>
    /// Inventory/Profile engine
    /// </summary>
    TesseractEngine FirstEngine { get; }

    /// <summary>
    /// Second slow pass engine
    /// </summary>
    TesseractEngine SecondEngine { get; }

    /// <summary>
    /// Engines for parallel processing the reward screen and snapit
    /// </summary>
    TesseractEngine[] Engines { get; }

    void Init();
    void ReloadEngines();
}

/// <summary>
/// Holds all the TesseractEngine instances and is responsible for loadind/reloading them
/// They are all configured in the same way
/// </summary>
public class TesseractService : ITesseractService
{
    /// <summary>
    /// Inventory/Profile engine
    /// </summary>
    public TesseractEngine FirstEngine { get; private set; }

    /// <summary>
    /// Second slow pass engine
    /// </summary>
    public TesseractEngine SecondEngine { get; private set; }

    /// <summary>
    /// Engines for parallel processing the reward screen and snapit
    /// </summary>
    public TesseractEngine[] Engines { get; } = new TesseractEngine[4];

    private readonly string _locale;
    
    private static string AppdataTessdataFolder => CustomEntrypoint.appdata_tessdata_folder;

    private static readonly string ApplicationDirectory =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\WFInfo";

    private static readonly string DataPath = ApplicationDirectory + @"\tessdata";

    public TesseractService(ApplicationSettings applicationSettings)
    {
        _locale = applicationSettings.Locale;
        getLocaleTessdata();
        FirstEngine = CreateEngine();
        SecondEngine = CreateEngine();
    }

    private void ReleaseUnmanagedResources()
    {
        // TODO release unmanaged resources here
    }

    private void Dispose(bool disposing)
    {
        ReleaseUnmanagedResources();
        if (disposing)
        {
            FirstEngine.Dispose();
            SecondEngine.Dispose();
            foreach (var engine in Engines)
                engine?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~TesseractService()
    {
        Dispose(false);
    }

    private TesseractEngine CreateEngine() => new TesseractEngine(DataPath, _locale)
    {
        DefaultPageSegMode = PageSegMode.SingleBlock
    };

    public void Init()
    {
        LoadEngines();
    }

    private void LoadEngines()
    {
        for (var i = 0; i < 4; i++)
        {
            Engines[i]?.Dispose();
            Engines[i] = CreateEngine();
        }
    }

    public void ReloadEngines()
    {
        getLocaleTessdata();
        LoadEngines();
        FirstEngine?.Dispose();
        FirstEngine = CreateEngine();
        SecondEngine?.Dispose();
        SecondEngine = CreateEngine();
    }

    private void getLocaleTessdata()
    {
        var traineddata_hotlink_prefix = "https://raw.githubusercontent.com/WFCD/WFinfo/libs/tessdata/";
        JObject traineddata_checksums = new JObject
        {
            { "en", "7af2ad02d11702c7092a5f8dd044d52f" },
            { "ko", "c776744205668b7e76b190cc648765da" }
        };

        // get trainned data
        string traineddata_hotlink = traineddata_hotlink_prefix  + _locale + ".traineddata";
        string app_data_traineddata_path = AppdataTessdataFolder + @"\"   + _locale + ".traineddata";

        WebClient webClient = CustomEntrypoint.CreateNewWebClient();

        if (!File.Exists(app_data_traineddata_path) || CustomEntrypoint.GetMD5hash(app_data_traineddata_path) !=
            traineddata_checksums.GetValue(_locale).ToObject<string>())
        {
            try
            {
                webClient.DownloadFile(traineddata_hotlink, app_data_traineddata_path);
            }
            catch (Exception)
            {
            }
        }
    }
}