using System.Collections.Frozen;
using System.IO;
using System.Net.Http;
using Serilog;
using Tesseract;
using WFInfo.Extensions;
using WFInfo.Services;
using WFInfo.Settings;

namespace WFInfo;

/// <summary>
/// Holds all the TesseractEngine instances and is responsible for loadind/reloading them
/// They are all configured in the same way
/// </summary>
public class TesseractService : ITesseractService
{
    private static readonly ILogger Logger = Log.ForContext<TesseractService>();
    
    private static string AppdataTessdataFolder => CustomEntrypoint.appdata_tessdata_folder;

    private static readonly string DataPath = Path.Combine(ApplicationConstants.AppPath, "tessdata");

    private readonly FrozenDictionary<string, string> _checksums = new Dictionary<string, string>
    {
        { "en", "7af2ad02d11702c7092a5f8dd044d52f" },
        { "ko", "c776744205668b7e76b190cc648765da" }
    }.ToFrozenDictionary();

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
    private readonly HttpClient _httpClient;
    private readonly IHasherService _hasherService;

    public TesseractService(
        ApplicationSettings applicationSettings,
        IHttpClientFactory httpClientFactory,
        IHasherService hasherService)
    {
        _locale = applicationSettings.Locale;
        _httpClient = httpClientFactory.CreateClient(nameof(TesseractService));
        _hasherService = hasherService;
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

    public async Task ReloadEngines()
    {
        await GetLocaleTessdata();
        LoadEngines();
        FirstEngine?.Dispose();
        FirstEngine = CreateEngine();
        SecondEngine?.Dispose();
        SecondEngine = CreateEngine();
    }

    private async Task GetLocaleTessdata()
    {
        var trainedDataPath = $@"{AppdataTessdataFolder}\{_locale}.traineddata";
        var shouldDownload = !File.Exists(trainedDataPath);

        if (!shouldDownload
            && _checksums.TryGetValue(_locale, out var checksum))
        {
            shouldDownload = string.Equals(checksum, _hasherService.GetMD5hash(trainedDataPath),
                StringComparison.InvariantCultureIgnoreCase);
        }

        if (shouldDownload)
        {
            const string trainedDataHotlinkPrefix = "https://raw.githubusercontent.com/WFCD/WFinfo/libs/tessdata/";
            var trainedDataHotlink = $"{trainedDataHotlinkPrefix}{_locale}.traineddata";
            var response = await _httpClient.DownloadFile(
                trainedDataHotlink,
                trainedDataPath);
            try
            {
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException e)
            {
                Logger.Error(e, "Failed to download tessdata for locale {locale}", _locale);
                throw;
            }
        }
    }
}