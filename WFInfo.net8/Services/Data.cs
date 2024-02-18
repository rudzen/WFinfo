using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using DotNext;
using Mediator;
using Microsoft.Extensions.ObjectPool;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using WebSocketSharp;
using WFInfo.Domain;
using WFInfo.Domain.Types;
using WFInfo.Extensions;
using WFInfo.Services.OpticalCharacterRecognition;
using WFInfo.Services.WarframeProcess;
using WFInfo.Services.WindowInfo;
using WFInfo.Settings;

namespace WFInfo.Services;

public sealed partial class Data :
    INotificationHandler<LogCapture.LogCaptureLineChange>,
    IRequestHandler<WebSocketAliveStatusRequest, WebSocketAliveStatusResponse>,
    INotificationHandler<WebSocketSetStatus>,
    IRequestHandler<DataRequest, DataResponse>
{
    private static readonly ILogger Logger = Log.Logger.ForContext<Data>();

    // Warframe.market item listing           {<id>: "<name>|<url_name>", ...}
    public JObject? MarketItems
    {
        get => _relicData[DataTypes.MarketItems.Index()];
        set => _relicData[DataTypes.MarketItems.Index()] = value;
    }

    // Contains warframe.market ducatonator listing     {<partName>: {"ducats": <ducat_val>,"plat": <plat_val>}, ...}
    public JObject? MarketData
    {
        get => _relicData[DataTypes.MarketData.Index()];
        private set => _relicData[DataTypes.MarketData.Index()] = value;
    }

    // Contains relicData from Warframe PC Drops        {<Era>: {"A1":{"vaulted": true,<rare1/uncommon[12]/common[123]>: <part>}, ...}, "Meso": ..., "Neo": ..., "Axi": ...}
    public JObject? RelicData
    {
        get => _relicData[DataTypes.Relic.Index()];
        private set => _relicData[DataTypes.Relic.Index()] = value;
    }

    /// <summary>
    /// Contains equipmentData from Warframe PC Drops
    /// <para>
    /// </para>
    /// </summary>
    // {<EQMT>: {"vaulted": true, "PARTS": {<NAME>:{"relic_name":<name>|"","count":<num>}, ...}},  ...}
    public JObject? EquipmentData
    {
        get => _relicData[DataTypes.Equipment.Index()];
        private set => _relicData[DataTypes.Equipment.Index()] = value;
    }

    // Contains relic to market name translation          {<relic_name>: <market_name>}
    private JObject? NameData
    {
        get => _relicData[DataTypes.Name.Index()];
        set => _relicData[DataTypes.Name.Index()] = value;
    }

    private readonly JObject?[] _relicData = new JObject[DataTypes.All.PopCount()];

    private static readonly string[] DataPaths;

    // TODO (rudzen) : split web socket into own class
    private readonly WebSocket marketSocket = new("wss://warframe.market/socket?platform=pc");

    private const string filterAllJSON = "https://api.warframestat.us/wfinfo/filtered_items";
    private readonly string sheetJsonUrl = "https://api.warframestat.us/wfinfo/prices";

    public string inGameName { get; private set; } = string.Empty;
    private readonly HttpClient _client;
    public bool rememberMe { get; set; }
    private Task? autoThread;

    private readonly ApplicationSettings _settings;
    private readonly IProcessFinder _process;
    private readonly IWindowInfoService _window;
    private readonly IEncryptedDataService _encryptedDataService;
    private readonly ObjectPool<StringBuilder> _stringBuilderPool;
    private readonly IMediator _mediator;
    private readonly ILogCapture _logCapture;
    private readonly ILevenshteinDistanceService _levenshteinDistanceService;
    private readonly IOCR _ocr;

    static Data()
    {
        DataPaths = new string[DataTypes.All.PopCount()];
        DataPaths[DataTypes.Equipment.Index()] = Path.Combine(ApplicationConstants.AppPath, "eqmt_data.json");
        DataPaths[DataTypes.Relic.Index()] = Path.Combine(ApplicationConstants.AppPath, "relic_data.json");
        DataPaths[DataTypes.Name.Index()] = Path.Combine(ApplicationConstants.AppPath, "name_data.json");
        DataPaths[DataTypes.MarketItems.Index()] = Path.Combine(ApplicationConstants.AppPath, "market_items.json");
        DataPaths[DataTypes.MarketData.Index()] = Path.Combine(ApplicationConstants.AppPath, "market_data.json");
    }

    public Data(
        ApplicationSettings settings,
        IProcessFinder process,
        IWindowInfoService window,
        IHttpClientFactory httpClientFactory,
        IEncryptedDataService encryptedDataService,
        ObjectPool<StringBuilder> stringBuilderPool,
        IMediator mediator,
        ILogCapture logCapture,
        ILevenshteinDistanceService levenshteinDistanceService,
        IOCR ocr)
    {
        _settings = settings;
        _process = process;
        _window = window;
        _encryptedDataService = encryptedDataService;
        _stringBuilderPool = stringBuilderPool;
        _mediator = mediator;
        _logCapture = logCapture;
        _levenshteinDistanceService = levenshteinDistanceService;
        _ocr = ocr;

        Logger.Debug("Initializing Databases");

        Directory.CreateDirectory(ApplicationConstants.AppPath);

        _client = httpClientFactory.CreateClient(nameof(Data));

        marketSocket.SslConfiguration.EnabledSslProtocols = SslProtocols.None;
    }

    private static void SaveDatabase<T>(string path, T db)
    {
        Logger.Debug("Saving database. file={File}", Path.GetFileName(path));
        File.WriteAllText(path, JsonConvert.SerializeObject(db, Formatting.Indented));
    }

    private static async Task SaveDatabaseAsync<T>(string path, T db)
    {
        Logger.Debug("Saving database. file={File}", Path.GetFileName(path));
        var json = JsonConvert.SerializeObject(db, Formatting.Indented);
        await File.WriteAllTextAsync(path, json).ConfigureAwait(ConfigureAwaitOptions.None);
    }

    // Load item list from Sheets
    public async Task ReloadItems()
    {
        const string url = "https://api.warframe.market/v1/items";

        MarketItems = new JObject();

        try
        {
            var body = await DownloadItems(url).ConfigureAwait(ConfigureAwaitOptions.None);
            Logger.Debug("First 256 char items: {Items}", body[..256]);

            var obj = JsonConvert.DeserializeObject<JObject>(body);
            var items = JArray.FromObject(obj["payload"]["items"]);
            foreach (var item in items)
            {
                var name = item["url_name"].ToString();

                if (!name.Contains("prime"))
                    continue;

                var itemId = item["id"].ToString();
                if (MarketItems.ContainsKey(itemId))
                    MarketItems[itemId] = $"{MarketItems[itemId]}|{item["item_name"]}";
            }
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "Failed to get items from warframe.market");
        }

        MarketItems["version"] = ApplicationConstants.MajorBuildVersion;
        Logger.Debug("Item database has been downloaded");
    }

    private async Task<string> DownloadItems(string url)
    {
        using var request = new HttpRequestMessage();
        request.RequestUri = new Uri(url);
        request.Method = HttpMethod.Get;
        request.Headers.Add("language", _settings.Locale);
        request.Headers.Add("accept", "application/json");
        request.Headers.Add("platform", "pc");
        var response = await _client.SendAsync(request).ConfigureAwait(ConfigureAwaitOptions.None);
        response = response.EnsureSuccessStatusCode();
        var body = await response.DecompressContent().ConfigureAwait(ConfigureAwaitOptions.None);
        return body;
    }

    // Load market data from Sheets
    private async ValueTask<bool> LoadMarket(JObject allFiltered, bool force = false)
    {
        if (!force)
        {
            var marketDataPath = DataPaths[DataTypes.MarketData.Index()];
            var marketItemsPath = DataPaths[DataTypes.MarketItems.Index()];
            if (File.Exists(marketDataPath) && File.Exists(marketItemsPath))
            {
                MarketData ??= JsonConvert.DeserializeObject<JObject>(ReadFileContent(marketDataPath));
                MarketData ??= new JObject();
                MarketItems ??= JsonConvert.DeserializeObject<JObject>(ReadFileContent(marketItemsPath));
                MarketItems ??= new JObject();

                if (MarketData.TryGetValue("version", out var version) && version.ToObject<string>() == ApplicationConstants.MajorBuildVersion)
                {
                    var now = DateTime.Now;
                    var timestamp = MarketData["timestamp"].ToObject<DateTime>();
                    if (timestamp > now.AddHours(-12))
                    {
                        Logger.Debug("Market Databases are up to date");
                        return false;
                    }
                }
            }
        }

        try
        {
            await ReloadItems();
        }
        catch
        {
            Logger.Debug(
                "Failed to refresh items from warframe.market, skipping WFM update for now. Some items might have incomplete info");
        }

        MarketData = new JObject();
        var response = await _client.GetAsync(sheetJsonUrl).ConfigureAwait(ConfigureAwaitOptions.None);
        var data = await response.DecompressContent().ConfigureAwait(ConfigureAwaitOptions.None);

        var rows = JsonConvert.DeserializeObject<JArray>(data);

        foreach (var row in rows)
        {
            var name = row["name"].ToString();

            if (!name.Contains("Prime "))
                continue;

            if (CanHaveBluePrint(name))
                name = name.Replace(" Blueprint", string.Empty);

            MarketData[name] = new JObject
            {
                { "plat", double.Parse(row["custom_avg"].ToString(), ApplicationConstants.Culture) },
                { "ducats", 0 },
                {
                    "volume",
                    int.Parse(row["yesterday_vol"].ToString(), ApplicationConstants.Culture) +
                    int.Parse(row["today_vol"].ToString(), ApplicationConstants.Culture)
                }
            };
        }

        // Add default values for ignored items
        foreach (var ignored in allFiltered["ignored_items"].ToObject<JObject>())
        {
            MarketData[ignored.Key] = ignored.Value;
        }

        MarketData["timestamp"] = DateTime.Now;
        MarketData["version"] = ApplicationConstants.MajorBuildVersion;

        Logger.Debug("Plat database has been downloaded");

        return true;
    }

    private async Task LoadMarketItem(string itemName, string url)
    {
        Logger.Debug("Load missing market item: {ItemName}", itemName);

        var statsUrl = $"https://api.warframe.market/v1/items/{url}/statistics";
        var itemsUrl = $"https://api.warframe.market/v1/items/{url}";

        try
        {
            using var statsRequest = new HttpRequestMessage();
            statsRequest.RequestUri = new Uri(statsUrl);
            statsRequest.Method = HttpMethod.Get;
            statsRequest.Headers.Add("accept", "application/json");
            statsRequest.Headers.Add("platform", "pc");
            statsRequest.Headers.Add("language", "en");

            var response = await _client.SendAsync(statsRequest).ConfigureAwait(ConfigureAwaitOptions.None);
            response.EnsureSuccessStatusCode();
            var statsData = await response.DecompressContent().ConfigureAwait(ConfigureAwaitOptions.None);

            var stats = JsonConvert.DeserializeObject<JObject>(statsData);
            var latestStats = stats["payload"]["statistics_closed"]["90days"].LastOrDefault();
            if (latestStats == null)
            {
                stats = new JObject
                {
                    { "avg_price", 999 },
                    { "volume", 0 }
                };
            }
            else
                stats = latestStats.ToObject<JObject>();

            using var itemsRequest = new HttpRequestMessage();
            itemsRequest.RequestUri = new Uri(itemsUrl);
            itemsRequest.Method = HttpMethod.Get;
            itemsRequest.Headers.Add("accept", "application/json");
            itemsRequest.Headers.Add("platform", "pc");
            itemsRequest.Headers.Add("language", "en");

            response = await _client.SendAsync(itemsRequest).ConfigureAwait(ConfigureAwaitOptions.None);
            response.EnsureSuccessStatusCode();

            var itemsData = await response.DecompressContent().ConfigureAwait(ConfigureAwaitOptions.None);

            var ducats = JsonConvert.DeserializeObject<JObject>(itemsData);

            ducats = ducats["payload"]["item"].ToObject<JObject>();
            var id = ducats["id"].ToObject<string>();
            ducats = ducats["items_in_set"].AsParallel().First(part => (string)part["id"] == id).ToObject<JObject>();
            var ducat = !ducats.TryGetValue("ducats", out var temp) ? "0" : temp.ToObject<string>();

            MarketData[itemName] = new JObject
            {
                { "ducats", ducat },
                { "plat", stats["avg_price"] },
                { "volume", stats["volume"] }
            };
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            throw;
        }
    }

    private bool LoadEqmtData(JObject allFiltered, bool force = false)
    {
        var equipmentDataPath = DataPaths[DataTypes.Equipment.Index()];
        var relicDataPath = DataPaths[DataTypes.Relic.Index()];
        var nameDataPath = DataPaths[DataTypes.Name.Index()];

        EquipmentData ??= File.Exists(equipmentDataPath)
            ? JsonConvert.DeserializeObject<JObject>(ReadFileContent(equipmentDataPath))
            : new JObject();
        RelicData ??= File.Exists(relicDataPath)
            ? JsonConvert.DeserializeObject<JObject>(ReadFileContent(relicDataPath))
            : new JObject();
        NameData ??= File.Exists(nameDataPath)
            ? JsonConvert.DeserializeObject<JObject>(ReadFileContent(nameDataPath))
            : new JObject();

        // fill in equipmentData (NO OVERWRITE)
        // fill in nameData
        // fill in relicData

        var filteredDate = allFiltered["timestamp"].ToObject<DateTime>().ToLocalTime().AddHours(-1);
        var eqmtDate = EquipmentData.TryGetValue("timestamp", out var date)
            ? date.ToObject<DateTime>()
            : filteredDate;

        if (force || eqmtDate.CompareTo(filteredDate) <= 0)
        {
            var now = DateTime.Now;
            EquipmentData["timestamp"] = now;
            RelicData["timestamp"] = now;
            NameData = new JObject();

            foreach (KeyValuePair<string, JToken> era in allFiltered["relics"].ToObject<JObject>())
            {
                if (!RelicData.ContainsKey(era.Key))
                    RelicData[era.Key] = new JObject();

                foreach (KeyValuePair<string, JToken> relic in era.Value.ToObject<JObject>())
                    RelicData[era.Key][relic.Key] = relic.Value;
            }

            foreach (KeyValuePair<string, JToken> prime in allFiltered["eqmt"].ToObject<JObject>())
            {
                var primeName = prime.Key[..(prime.Key.IndexOf("Prime") + 5)];
                if (!EquipmentData.TryGetValue(primeName, out var primeNameData))
                {
                    primeNameData = new JObject();
                    EquipmentData[primeName] = primeNameData;
                }

                primeNameData["vaulted"] = prime.Value["vaulted"];
                primeNameData["type"] = prime.Value["type"];
                if (!primeNameData.ToObject<JObject>().TryGetValue("mastered", out _))
                    primeNameData["mastered"] = false;

                primeNameData["parts"] ??= new JObject();

                foreach (KeyValuePair<string, JToken> part in prime.Value["parts"].ToObject<JObject>())
                {
                    var partName = part.Key;
                    var primeNameDataParts = primeNameData["parts"];

                    if (!primeNameDataParts.ToObject<JObject>().TryGetValue(partName, out var primeNameDataPartsPartName))
                        primeNameDataPartsPartName = new JObject();

                    if (!primeNameDataPartsPartName.ToObject<JObject>().TryGetValue("owned", out _))
                        primeNameDataPartsPartName["owned"] = 0;

                    primeNameDataPartsPartName["vaulted"] = part.Value["vaulted"];
                    primeNameDataPartsPartName["count"] = part.Value["count"];
                    primeNameDataPartsPartName["ducats"] = part.Value["ducats"];

                    var gameName = IsBlueprint(in prime, in part) ? $"{part.Key} Blueprint" : part.Key;

                    if (MarketData.TryGetValue(partName, out _))
                    {
                        NameData[gameName] = partName;
                        MarketData[partName]["ducats"] = Convert.ToInt32(part.Value["ducats"].ToString());
                    }
                }

                EquipmentData[primeName] = primeNameData;
            }

            // Add default values for ignored items
            foreach (KeyValuePair<string, JToken> ignored in allFiltered["ignored_items"].ToObject<JObject>())
            {
                NameData[ignored.Key] = ignored.Key;
            }

            Logger.Debug("Prime Database has been downloaded");
            return true;
        }

        Logger.Debug("Prime Database is up to date");
        return false;
    }

    private static bool IsBlueprint(in KeyValuePair<string, JToken> prime, in KeyValuePair<string, JToken> part)
    {
        var primeType = prime.Value["type"].ToString();
        return primeType switch
        {
            "Archwing" => part.Key.EndsWith("Systems") || part.Key.EndsWith("Harness") || part.Key.EndsWith("Wings"),
            "Warframes" => part.Key.EndsWith("Systems") || part.Key.EndsWith("Neuroptics") ||
                           part.Key.EndsWith("Chassis"),
            _ => false
        };
    }

    private void RefreshMarketDucats()
    {
        //equipmentData[primeName]["parts"][partName]["ducats"]
        foreach (KeyValuePair<string, JToken> prime in EquipmentData)
            if (prime.Key != "timestamp")
                foreach (KeyValuePair<string, JToken> part in EquipmentData[prime.Key]["parts"].ToObject<JObject>())
                    if (MarketData.TryGetValue(part.Key, out var value))
                        value["ducats"] = Convert.ToInt32(part.Value["ducats"].ToString());
    }

    public async ValueTask<bool> Update()
    {
        Logger.Debug("Checking for Updates to Databases");

        using var request = new HttpRequestMessage();
        request.RequestUri = new Uri(filterAllJSON);
        request.Method = HttpMethod.Get;
        request.Headers.Add("accept", "application/json");
        request.Headers.Add("platform", "pc");
        request.Headers.Add("language", "en");

        var response = await _client.SendAsync(request).ConfigureAwait(ConfigureAwaitOptions.None);
        response.EnsureSuccessStatusCode();

        var data = await response.DecompressContent().ConfigureAwait(ConfigureAwaitOptions.None);
        var allFiltered = JsonConvert.DeserializeObject<JObject>(data);
        var saveDatabases = await LoadMarket(allFiltered);

        foreach (KeyValuePair<string, JToken> elem in MarketItems)
        {
            if (elem.Key == "version")
                continue;

            var split = elem.Value.ToString().Split('|');
            var itemName = split[0];
            var itemUrl = split[1];
            if (!itemName.Contains(" Set") && !MarketData.TryGetValue(itemName, out var _))
            {
                await LoadMarketItem(itemName, itemUrl);
                saveDatabases = true;
            }
        }

        if (MarketData["timestamp"] == null)
        {
            Application.Current.Dispatcher.InvokeIfRequired(() =>
            {
                MainWindow.INSTANCE.MarketData.Content = "VERIFY";
                MainWindow.INSTANCE.DropData.Content = "TIME";
            });

            return false;
        }

        Logger.Debug("Sending market data timestamp update");

        var msg = new DataUpdatedAt(
            Date: MarketData["timestamp"].ToObject<DateTime>().ToString(ApplicationConstants.DateFormat, ApplicationConstants.Culture),
            Type: DataTypes.MarketData
        );

        await _mediator.Publish(msg);

        saveDatabases = LoadEqmtData(allFiltered, saveDatabases);

        if (saveDatabases)
            SaveAll(DataTypes.All);

        return saveDatabases;
    }

    public async Task ForceMarketUpdate()
    {
        try
        {
            Logger.Debug("Forcing market update");
            var response = await _client.GetAsync(filterAllJSON).ConfigureAwait(ConfigureAwaitOptions.None);
            response.EnsureSuccessStatusCode();
            var data = await response.DecompressContent().ConfigureAwait(ConfigureAwaitOptions.None);
            var allFiltered = JsonConvert.DeserializeObject<JObject>(data);
            var loaded = await LoadMarket(allFiltered, true).ConfigureAwait(false);

            Logger.Debug("Forcing market update complete. success={Loaded}", loaded);

            var marketData = MarketData;

            foreach (var elem in MarketItems)
            {
                if (elem.Key == "version")
                    continue;

                if (elem.Value is null)
                {
                    Logger.Debug("Null market item found. item={ItemName}", elem.Key);
                    continue;
                }

                var split = elem.Value.ToString().Split('|');
                var itemName = split[0];
                var itemUrl = split[1];
                if (!itemName.Contains(" Set") && !marketData.ContainsKey(itemName))
                    await LoadMarketItem(itemName, itemUrl);
            }

            RefreshMarketDucats();

            await SaveAllAsync(DataTypes.MarketItems | DataTypes.MarketData);

            var date = marketData["timestamp"]
                       .ToObject<DateTime>()
                       .ToString(ApplicationConstants.DateFormat, ApplicationConstants.Culture);
            var msg = new DataUpdatedAt(date, DataTypes.MarketData);
            await _mediator.Publish(msg);
            await _mediator.Publish(new UpdateStatus("Market Update Complete", 0));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Market Update Failed");
            await _mediator.Publish(new UpdateStatus("Market Update Failed"));
            Application.Current.Dispatcher.InvokeIfRequired(() =>
            {
                _ = new ErrorDialogue(DateTime.Now, 0);
            });
        }
    }

    public void SaveAll(DataTypes dataTypes)
    {
        var dt = dataTypes;
        while (dt != DataTypes.None)
        {
            var next = DataTypeExtensions.PopLsb(ref dt);
            var index = next.Index();
            var path = DataPaths[index];
            var db = _relicData[index];
            SaveDatabase(path, db);
        }
    }

    private async ValueTask SaveAllAsync(DataTypes dataTypes)
    {
        var dt = dataTypes;
        while (dt != DataTypes.None)
        {
            var next = DataTypeExtensions.PopLsb(ref dt);
            var index = next.Index();
            var path = DataPaths[index];
            var db = _relicData[index];
            await SaveDatabaseAsync(path, db);
        }
    }

    public async Task ForceEquipmentUpdate()
    {
        try
        {
            Logger.Debug("Forcing equipment update");

            var response = await _client.GetAsync(filterAllJSON).ConfigureAwait(ConfigureAwaitOptions.None);
            response.EnsureSuccessStatusCode();
            var data = await response.DecompressContent().ConfigureAwait(ConfigureAwaitOptions.None);
            var allFiltered = JsonConvert.DeserializeObject<JObject>(data);

            LoadEqmtData(allFiltered, true);
            SaveAll(DataTypes.All);

            var msg = new DataUpdatedAt(
                Date: EquipmentData["timestamp"].ToObject<DateTime>().ToString(ApplicationConstants.DateFormat, ApplicationConstants.Culture),
                Type: DataTypes.MarketItems
            );

            await _mediator.Publish(msg);
            await _mediator.Publish(new UpdateStatus("Prime Update Complete", 0));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Prime Update Failed");
            await _mediator.Publish(new UpdateStatus("Prime Update Failed"));
            Application.Current.Dispatcher.InvokeIfRequired(() =>
            {
                _ = new ErrorDialogue(DateTime.Now, 0);
            });
        }
    }

    public bool IsPartVaulted(string name)
    {
        var primeIndex = name.IndexOf("Prime");
        if (primeIndex < 0)
            return false;
        var eqmt = name[..(primeIndex + 5)];
        return EquipmentData[eqmt]["parts"][name]["vaulted"].ToObject<bool>();
    }

    public bool IsPartMastered(string name)
    {
        var primeIndex = name.IndexOf("Prime");
        if (primeIndex < 0)
            return false;
        var eqmt = name[..(primeIndex + 5)];
        return EquipmentData[eqmt]["mastered"].ToObject<bool>();
    }

    public string PartsOwned(string name)
    {
        var primeIndex = name.IndexOf("Prime");
        if (primeIndex < 0)
            return "0";
        var eqmt = name[..(primeIndex + 5)];
        var owned = EquipmentData[eqmt]["parts"][name]["owned"].ToString();
        return owned == "0" ? "0" : owned;
    }

    public string PartsCount(string name)
    {
        var primeIndex = name.IndexOf("Prime");
        if (primeIndex < 0)
            return "0";
        var eqmt = name[..(primeIndex + 5)];
        var count = EquipmentData[eqmt]["parts"][name]["count"].ToString();
        return count == "0" ? "0" : count;
    }

    public string GetPartName(string name, out int low, bool suppressLogging, out bool multipleLowest)
    {
        // Checks the Levenshtein Distance of a string and returns the index in Names() of the closest part
        string lowest = null!;
        string lowestUnfiltered = null!;
        low = 9999;
        multipleLowest = false;
        foreach (var (key, token) in NameData!)
        {
            var value = token!.ToObject<string>();
            var val = _levenshteinDistanceService.LevenshteinDistance(
                s: key,
                t: name,
                locale: _settings.Locale,
                marketItems: MarketItems!
            );
            if (val < low)
            {
                low = val;
                lowest = value!;
                lowestUnfiltered = key;
                multipleLowest = false;
            }
            else if (val == low)
            {
                multipleLowest = true;
            }

            //If both
            if (val == low && lowest.StartsWith("Gara") && key.StartsWith("Ivara"))
            {
                lowest = value!;
                lowestUnfiltered = key;
            }
        }

        if (!suppressLogging)
            Logger.Debug("Found part({Low}): \"{Unfiltered}\" from \"{Name}\"", low, lowestUnfiltered, name);

        return lowest;
    }

    public string GetPartNameHuman(string name, out int low)
    {
        // Checks the Levenshtein Distance of a string and returns the index in Names() of the closest part optimized for human searching
        string lowest = null;
        string lowestUnfiltered = null;
        low = 9999;
        foreach (KeyValuePair<string, JToken> prop in NameData)
        {
            if (prop.Value.ToString().Contains(name, StringComparison.CurrentCultureIgnoreCase))
            {
                var val = _levenshteinDistanceService.LevenshteinDistance(
                    s: prop.Value.ToString(),
                    t: name,
                    locale: _settings.Locale,
                    marketItems: MarketItems
                );
                if (val < low)
                {
                    low = val;
                    lowest = prop.Value.ToObject<string>();
                    lowestUnfiltered = prop.Value.ToString();
                }
            }
        }

        if (low > 10)
        {
            foreach (KeyValuePair<string, JToken> prop in NameData)
            {
                var val = _levenshteinDistanceService.LevenshteinDistance(
                    s: prop.Value.ToString(),
                    t: name,
                    locale: _settings.Locale,
                    marketItems: MarketItems
                );

                if (val >= low)
                    continue;

                low = val;
                lowest = prop.Value.ToObject<string>();
                lowestUnfiltered = prop.Value.ToString();
            }
        }

        Logger.Debug("Found part(" + low + "): \"" + lowestUnfiltered + "\" from \"" + name + "\"");
        return lowest;
    }

    public static string GetSetName(string name)
    {
        var result = name.ToLower(ApplicationConstants.Culture);

        if (result.Contains("kavasa"))
            return "Kavasa Prime Kubrow Collar Set";

        result = result.Replace("lower limb", string.Empty);
        result = result.Replace("upper limb", string.Empty);
        result = result.Replace("neuroptics", string.Empty);
        result = result.Replace("chassis", string.Empty);
        result = result.Replace("systems", string.Empty);
        result = result.Replace("carapace", string.Empty);
        result = result.Replace("cerebrum", string.Empty);
        result = result.Replace("blueprint", string.Empty);
        result = result.Replace("harness", string.Empty);
        result = result.Replace("blade", string.Empty);
        result = result.Replace("pouch", string.Empty);
        result = result.Replace("head", string.Empty);
        result = result.Replace("barrel", string.Empty);
        result = result.Replace("receiver", string.Empty);
        result = result.Replace("stock", string.Empty);
        result = result.Replace("disc", string.Empty);
        result = result.Replace("grip", string.Empty);
        result = result.Replace("string", string.Empty);
        result = result.Replace("handle", string.Empty);
        result = result.Replace("ornament", string.Empty);
        result = result.Replace("wings", string.Empty);
        result = result.Replace("blades", string.Empty);
        result = result.Replace("hilt", string.Empty);
        result = result.Replace("link", string.Empty);
        result = result.TrimEnd();
        result = ApplicationConstants.Culture.TextInfo.ToTitleCase(result);
        result += " Set";
        return result;
    }

    public string GetRelicName(string string1)
    {
        string lowest = null;
        var low = 999;
        int temp;
        string eraName = null;
        JObject job = null;

        foreach (KeyValuePair<string, JToken> era in RelicData)
        {
            if (!era.Key.Contains("timestamp"))
            {
                temp = _levenshteinDistanceService.LevenshteinDistanceSecond(
                    str1: string1,
                    str2: $"{era.Key}??RELIC",
                    limit: low
                );

                if (temp >= low)
                    continue;

                job = era.Value.ToObject<JObject>();
                eraName = era.Key;
                low = temp;
            }
        }

        low = 999;
        foreach (KeyValuePair<string, JToken> relic in job)
        {
            temp = _levenshteinDistanceService.LevenshteinDistanceSecond(
                str1: string1,
                str2: $"{eraName}{relic.Key}RELIC",
                limit: low
            );

            if (temp >= low)
                continue;

            lowest = $"{eraName} {relic.Key}";
            low = temp;
        }

        return lowest;
    }

    private void LogChanged(string line)
    {
        if (autoThread is { IsCompleted: false })
            return;

        if (autoThread != null)
        {
            autoThread.Dispose();
            autoThread = null;
        }

        if (line.Contains("Pause countdown done") || line.Contains("Got rewards"))
        {
            autoThread = Task.Run(AutoTriggered);
            Overlay.RewardsDisplaying = true;
        }

        //abort if autolist and autocsv disabled, or line doesn't contain end-of-session message or timer finished message
        if (!(line.Contains("MatchingService::EndSession") || line.Contains("Relic timer closed")) ||
            !(_settings.AutoList || _settings.AutoCSV || _settings.AutoCount)) return;

        if (Main.ListingHelper.PrimeRewards.Count == 0)
            return;

        Task.Run(async () =>
        {
            if (_settings.AutoList && string.IsNullOrEmpty(inGameName) && !await IsJWTvalid())
                Disconnect();

            Overlay.RewardsDisplaying = false;
            var csv = string.Empty;
            Logger.Debug("Looping through rewards");
            Logger.Debug("AutoList: " + _settings.AutoList + ", AutoCSV: " + _settings.AutoCSV + ", AutoCount: " +
                         _settings.AutoCount);
            foreach (var rewardscreen in Main.ListingHelper.PrimeRewards)
            {
                var rewards = string.Empty;
                for (var i = 0; i < rewardscreen.Count; i++)
                {
                    rewards += rewardscreen[i];
                    if (i + 1 < rewardscreen.Count)
                        rewards += " || ";
                }

                Logger.Debug("Showing rewards. rewards={Rewards},selectedIndex={Selected}",
                    rewards, Main.ListingHelper.SelectedRewardIndex);

                if (_settings.AutoCSV)
                {
                    if (csv.Length == 0 && !File.Exists(Path.Combine(ApplicationConstants.AppPath, "rewardExport.csv")))
                        csv +=
                            "Timestamp,ChosenIndex,Reward_0_Name,Reward_0_Plat,Reward_0_Ducats,Reward_1_Name,Reward_1_Plat,Reward_1_Ducats,Reward_2_Name,Reward_2_Plat,Reward_2_Ducats,Reward_3_Name,Reward_3_Plat,Reward_3_Ducats"
                            +
                            Environment.NewLine;
                    csv += DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff", ApplicationConstants.Culture) + "," +
                           Main.ListingHelper.SelectedRewardIndex;
                    for (var i = 0; i < 4; i++)
                    {
                        if (i < rewardscreen.Count)
                        {
                            var job = MarketData.GetValue(rewardscreen[i]).ToObject<JObject>();
                            var plat = job["plat"].ToObject<string>();
                            var ducats = job["ducats"].ToObject<string>();
                            csv += "," + rewardscreen[i] + "," + plat + "," + ducats;
                        }
                        else
                        {
                            csv += ",\"\",0,0"; //fill empty slots with "",0,0
                        }
                    }

                    csv += Environment.NewLine;
                }

                if (_settings.AutoCount)
                {
                    Application.Current.Dispatcher.InvokeIfRequired(() =>
                    {
                        Main.AutoCount.ViewModel.AddItem(new AutoAddSingleItem(rewardscreen,
                            Main.ListingHelper.SelectedRewardIndex, Main.AutoCount.ViewModel, _mediator));
                    });
                }

                if (_settings.AutoList)
                {
                    var rewardCollection = Main.ListingHelper.GetRewardCollection(rewardscreen).GetAwaiter().GetResult();
                    if (rewardCollection.PrimeNames.Count == 0)
                        continue;

                    Main.ListingHelper.ScreensList.Add(new RewardCollectionItem(string.Empty, rewardCollection));
                }
                else
                {
                    Main.ListingHelper.SelectedRewardIndex =
                        0; //otherwise done by GetRewardCollection, but that calls WFM API
                }
            }

            if (_settings.AutoCount)
            {
                Logger.Debug("Opening AutoCount interface");
                await _mediator.Publish(AutoCountShow.Instance);
            }

            if (_settings.AutoCSV)
            {
                Logger.Debug("appending rewardExport.csv");
                await File
                      .AppendAllTextAsync(Path.Combine(ApplicationConstants.AppPath, "rewardExport.csv"), csv)
                      .ConfigureAwait(ConfigureAwaitOptions.None);
            }

            if (_settings.AutoList)
            {
                Logger.Debug("Opening AutoList interface");
                Application.Current.Dispatcher.InvokeIfRequired(() =>
                {
                    if (Main.ListingHelper.ScreensList.Count == 1)
                        Main.ListingHelper.SetScreen(0);
                    Main.ListingHelper.Show();
                    Main.ListingHelper.Topmost = true;
                    Main.ListingHelper.Topmost = false;
                });
            }

            Logger.Debug("Clearing listingHelper.PrimeRewards");
            Application.Current.Dispatcher.InvokeIfRequired(() =>
            {
                Main.ListingHelper.PrimeRewards.Clear();
            });
        });
    }

    private async Task AutoTriggered()
    {
        try
        {
            var watch = Stopwatch.StartNew();
            var stop = watch.ElapsedMilliseconds + 5000;
            var wait = watch.ElapsedMilliseconds;
            var fixedStop = watch.ElapsedMilliseconds + _settings.FixedAutoDelay;

            await _window.UpdateWindow();

            if (_settings.ThemeSelection == WFtheme.AUTO)
            {
                while (watch.ElapsedMilliseconds < stop)
                {
                    if (watch.ElapsedMilliseconds <= wait) continue;
                    wait += _settings.AutoDelay;
                    _ocr.GetThemeWeighted(out var diff);
                    if (diff <= 40) continue;
                    while (watch.ElapsedMilliseconds < wait) ;
                    Logger.Debug("started auto processing");
                    await _ocr.ProcessRewardScreen();
                    break;
                }
            }
            else
            {
                while (watch.ElapsedMilliseconds < fixedStop) ;
                Logger.Debug("started auto processing (fixed delay)");
                await _ocr.ProcessRewardScreen();
            }

            watch.Stop();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Auto failed");
            await _mediator.Publish(new UpdateStatus("Auto Detection Failed"));
            Application.Current.Dispatcher.InvokeIfRequired(() =>
            {
                _ = new ErrorDialogue(DateTime.Now, 0);
            });
        }
    }

    /// <summary>
    ///	Get's the user's login JWT to authenticate future API calls.
    /// </summary>
    /// <param name="email">Users email</param>
    /// <param name="password">Users password</param>
    /// <exception cref="Exception">Connection exception JSON formatted</exception>
    /// <returns>A task to be awaited</returns>
    public async Task GetUserLogin(string email, string password)
    {
        using var request = new HttpRequestMessage();
        request.RequestUri = new Uri("https://api.warframe.market/v1/auth/signin");
        request.Method = HttpMethod.Post;
        var content =
            $"{{ \"email\":\"{email}\",\"password\":\"{password.Replace(@"\", @"\\")}\", \"auth_type\": \"header\"}}";
        request.Content = new StringContent(content, Encoding.UTF8, "application/json");
        request.Headers.Add("Authorization", "JWT");
        request.Headers.Add("language", "en");
        request.Headers.Add("accept", "application/json");
        request.Headers.Add("platform", "pc");
        request.Headers.Add("auth_type", "header");
        var response = await _client.SendAsync(request).ConfigureAwait(ConfigureAwaitOptions.None);
        var responseBody = await response.DecompressContent().ConfigureAwait(ConfigureAwaitOptions.None);

        var rgxBody = CheckCodeRegEx();
        var censoredResponse = rgxBody.Replace(responseBody, "\"check_code\": \"REDACTED\"");
        Logger.Debug(censoredResponse);
        if (response.IsSuccessStatusCode)
        {
            SetJWT(response.Headers);
            await OpenWebSocket();
        }
        else
        {
            // TODO (rudzen): replace this illegal regex with a proper RFC valid check
            var rgxEmail = new Regex("[a-zA-Z0-9]");
            var censoredEmail = rgxEmail.Replace(email, "*");
            throw new Exception($"GetUserLogin, {responseBody}Email: {censoredEmail}, Pw length: {password.Length}");
        }
    }

    /// <summary>
    /// Attempts to connect the user's account to the websocket
    /// </summary>
    /// <returns>A task to be awaited</returns>
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async Task<bool> OpenWebSocket()
    {
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        Logger.Debug("Connecting to websocket");

        if (marketSocket.IsAlive)
            return false;

        marketSocket.OnMessage += (_, e) =>
        {
            // Error checking, report back to main.status
            if (!e.Data.Contains("@WS/ERROR"))
                return;

            Logger.Debug("warframe.market. error={Data}", e.Data);
            Disconnect();
            Task.Run(async () =>
            {
                await _mediator.Publish(WarframeMarketSignOut.Instance);
            });
        };

        marketSocket.OnMessage += (sender, e) =>
        {
            Logger.Debug("warframe.market. data={Data}", e.Data);

            if (!e.Data.Contains("SET_STATUS"))
                return;

            var marketMessage = JsonConvert.DeserializeObject<JObject>(e.Data);
            var payload = marketMessage!.GetValue("payload")!.ToString();

            Task.Run(async () =>
            {
                var awayStatus = await _mediator.Send(new WarframeMarketStatusAwayStatusRequest(payload));
                await _mediator.Publish(new WarframeMarketStatusUpdate(payload));
            });
        };

        marketSocket.OnOpen += (sender, e) =>
        {
            var isRunning = _process.IsRunning();
            marketSocket.Send(isRunning && _process is { GameIsStreamed: false }
                ? "{\"type\":\"@WS/USER/SET_STATUS\",\"payload\":\"ingame\"}"
                : "{\"type\":\"@WS/USER/SET_STATUS\",\"payload\":\"online\"}");
        };

        try
        {
            marketSocket.SetCookie(new WebSocketSharp.Net.Cookie("JWT", _encryptedDataService.JWT));
            marketSocket.ConnectAsync();
        }
        catch (Exception e)
        {
            Logger.Error(e, "Unable to connect to socket");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Sets the JWT to be used for future calls
    /// </summary>
    /// <param name="headers">Response headers from the original Login call</param>
    private void SetJWT(HttpResponseHeaders headers)
    {
        foreach (var item in headers)
        {
            if (!item.Key.Contains("authorization", StringComparison.CurrentCultureIgnoreCase))
                continue;
            var temp = item.Value.First();
            _encryptedDataService.JWT = temp[4..];
            return;
        }
    }

    /// <summary>
    /// Lists an item under an account. Expected to be called after being logged in thus no login attempts.
    /// </summary>
    /// <param name="primeItem">Human friendly for prime item</param>
    /// <param name="platinum">The amount of platinum the user entered for the listing</param>
    /// <param name="quantity">The quantity of items the user listed.</param>
    /// <returns>The success of the method</returns>
    public async Task<bool> ListItem(string primeItem, int platinum, int quantity)
    {
        try
        {
            using var request = new HttpRequestMessage();
            request.RequestUri = new Uri("https://api.warframe.market/v1/profile/orders");
            request.Method = HttpMethod.Post;
            var itemId = PrimeItemToItemId(primeItem);
            var json =
                $"{{\"order_type\":\"sell\",\"item_id\":\"{itemId}\",\"platinum\":{platinum},\"quantity\":{quantity}}}";
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Add("Authorization", $"JWT {_encryptedDataService.JWT}");
            request.Headers.Add("language", "en");
            request.Headers.Add("accept", "application/json");
            request.Headers.Add("platform", "pc");
            request.Headers.Add("auth_type", "header");

            var response = await _client.SendAsync(request).ConfigureAwait(ConfigureAwaitOptions.None);
            var responseBody = await response.DecompressContent().ConfigureAwait(ConfigureAwaitOptions.None);

            if (!response.IsSuccessStatusCode)
                throw new Exception(responseBody);

            SetJWT(response.Headers);
            return true;
        }
        catch (Exception e)
        {
            Logger.Error(e, "ListItem");
            return false;
        }
    }

    /// <summary>
    /// Updates a listing with given variables
    /// </summary>
    /// <param name="listingId">The listingID of which the listing is going to be updated</param>
    /// <param name="platinum">The new platinum value</param>
    /// <param name="quantity">The new quantity</param>
    /// <returns>The success of the method</returns>
    public async Task<bool> UpdateListing(string listingId, int platinum, int quantity)
    {
        try
        {
            var url = $"https://api.warframe.market/v1/profile/orders/{listingId}";
            using var request = new HttpRequestMessage();
            request.RequestUri = new Uri(url);
            request.Method = HttpMethod.Put;
            var json =
                $"{{\"order_id\":\"{listingId}\", \"platinum\": {platinum}, \"quantity\":{quantity + 1}, \"visible\":true}}";
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Add("Authorization", $"JWT {_encryptedDataService.JWT}");
            request.Headers.Add("language", "en");
            request.Headers.Add("accept", "application/json");
            request.Headers.Add("platform", "pc");
            request.Headers.Add("auth_type", "header");

            var response = await _client.SendAsync(request).ConfigureAwait(ConfigureAwaitOptions.None);
            var responseBody = await response.DecompressContent().ConfigureAwait(ConfigureAwaitOptions.None);

            if (!response.IsSuccessStatusCode)
                throw new Exception(responseBody);

            SetJWT(response.Headers);
            return true;
        }
        catch (Exception e)
        {
            Logger.Error(e, "UpdateListing");
            return false;
        }
    }

    /// <summary>
    /// Converts the human friendly name to warframe.market's ID
    /// </summary>
    /// <param name="primeItem">Human friendly name of prime item</param>
    /// <returns>Warframe.market prime item ID</returns>
    private string PrimeItemToItemId(string primeItem)
    {
        foreach (var marketItem in MarketItems)
        {
            if (marketItem.Value.ToString().Split('|')[0].Equals(primeItem, StringComparison.OrdinalIgnoreCase))
            {
                return marketItem.Key;
            }
        }

        throw new Exception($"PrimeItemToItemID, Prime item \"{primeItem}\" does not exist in marketItem");
    }

    /// <summary>
    /// Sets the status of WFM websocket. Will try to reconnect if it is not already connected.
    /// Accepts the following values:
    /// offline, set's player status to be hidden on the site.
    /// online, set's player status to be online on the site.
    /// in game, set's player status to be online and ingame on the site
    /// </summary>
    /// <param name="status">
    /// </param>
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async Task<bool> SetWebsocketStatus(string status)
    {
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        if (!_encryptedDataService.IsJwtLoggedIn())
            return false;

        var message = "{\"type\":\"@WS/USER/SET_STATUS\",\"payload\":\"";
        switch (status)
        {
            case "ingame":
            case "in game":
                message += "ingame\"}";
                break;
            case "online":
                message += "online\"}";
                break;
            default:
                message += "invisible\"}";
                break;
        }

        try
        {
            SendMessage(message);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "SetWebsocketStatus, Was unable to set status");
            throw;
        }

        return true;
    }

    /// <summary>
    /// Dummy method to make it so that you log send messages
    /// </summary>
    /// <param name="data">The JSON string of data being sent over websocket</param>
    private void SendMessage(string data)
    {
        Logger.Debug("Sending to websocket. data={Data}", data);
        try
        {
            marketSocket.Send(data);
        }
        catch (InvalidOperationException exception)
        {
            Logger.Error(exception, "Was unable to send message");
        }
    }

    /// <summary>
    /// Disconnects the user from websocket and sets JWT to null
    /// </summary>
    public void Disconnect()
    {
        if (marketSocket.ReadyState != WebSocketState.Open)
            return;

        //only send disconnect message if the socket is connected
        SendMessage("{\"type\":\"@WS/USER/SET_STATUS\",\"payload\":\"invisible\"}");
        _encryptedDataService.JWT = null;
        rememberMe = false;
        inGameName = string.Empty;
        marketSocket.Close(1006);
    }

    private string GetUrlName(string primeName)
    {
        foreach (var marketItem in MarketItems)
        {
            var values = marketItem.Value.ToString().Split('|');
            if (values.Length > 2 && values[0].Equals(primeName, StringComparison.OrdinalIgnoreCase))
                return values[1];
        }

        throw new Exception($"GetUrlName, Prime item \"{primeName}\" does not exist in marketItem");
    }

    /// <summary>
    /// Tries to get the top listings of a prime item
    /// https://api.warframe.market/v1/items/ prime_name /orders/top
    /// </summary>
    /// <param name="primeName"></param>
    /// <returns></returns>
    public async Task<Optional<JObject>> GetTopListings(string primeName)
    {
        var urlName = GetUrlName(primeName);

        try
        {
            using var request = new HttpRequestMessage();
            request.RequestUri = new Uri("https://api.warframe.market/v1/items/" + urlName + "/orders/top");
            request.Method = HttpMethod.Get;
            request.Headers.Add("Authorization", "JWT " + _encryptedDataService.JWT);
            request.Headers.Add("language", "en");
            request.Headers.Add("accept", "application/json");
            request.Headers.Add("platform", "pc");
            request.Headers.Add("auth_type", "header");
            var response = await _client.SendAsync(request).ConfigureAwait(ConfigureAwaitOptions.None);
            var body = await response.DecompressContent().ConfigureAwait(ConfigureAwaitOptions.None);
            var payload = JsonConvert.DeserializeObject<JObject>(body);
            if (body.Length < 3)
                throw new Exception("No sell orders found: " + payload);
            Debug.WriteLine(body);

            return JsonConvert.DeserializeObject<JObject>(body);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "GetTopListings");
        }

        return Optional<JObject>.None;
    }

    /// <summary>
    /// Tries to get the profile page with the current JWT token
    /// TODO (rudzen) : Move to own new JwtService class to reduce coupling
    /// </summary>
    /// <returns>bool of which answers the question "Is the user JWT valid?"</returns>
    public async Task<bool> IsJWTvalid()
    {
        if (_encryptedDataService.JWT is null)
            return false;

        try
        {
            using var request = new HttpRequestMessage();
            request.RequestUri = new Uri("https://api.warframe.market/v1/profile");
            request.Method = HttpMethod.Get;
            request.Headers.Add("Authorization", $"JWT {_encryptedDataService.JWT}");
            var response = await _client.SendAsync(request).ConfigureAwait(ConfigureAwaitOptions.None);
            SetJWT(response.Headers);
            var data = await response.DecompressContent().ConfigureAwait(ConfigureAwaitOptions.None);
            var profile = JsonConvert.DeserializeObject<JObject>(data);
            profile["profile"]["check_code"] = "REDACTED"; // remnove the code that can compromise an account.
            Logger.Debug("JWT check response: {Profile}", profile["profile"]);
            return !(bool)profile["profile"]["anonymous"];
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "IsJWTvalid");
            return false;
        }
    }

    /// <summary>
    /// Queries the current account for the amount of the CURRENT listed items
    /// To get the amount of a listing use:
    /// var listing = await Main.dataBase.GetCurrentListing(primeItem);
    /// var amount = (int) listing?["quantity"];
    /// To get the ID of a listing use:
    /// var listing = await Main.dataBase.GetCurrentListing(primeItem);
    /// var amount = (int) listing?["id"];
    /// </summary>
    /// <param name="primeName"></param>
    /// <returns>Quantity of prime named listed on the site</returns>
    public async Task<Optional<JToken>> GetCurrentListing(string primeName)
    {
        try
        {
            if (string.IsNullOrEmpty(inGameName))
                await SetIngameName();

            using var request = new HttpRequestMessage();
            request.RequestUri = new Uri($"https://api.warframe.market/v1/profile/{inGameName}/orders");
            request.Method = HttpMethod.Get;
            request.Headers.Add("Authorization", $"JWT {_encryptedDataService.JWT}");
            request.Headers.Add("language", "en");
            request.Headers.Add("accept", "application/json");
            request.Headers.Add("platform", "pc");
            request.Headers.Add("auth_type", "header");
            var response = await _client.SendAsync(request).ConfigureAwait(ConfigureAwaitOptions.None);
            var body = await response.DecompressContent().ConfigureAwait(ConfigureAwaitOptions.None);
            var payload = JsonConvert.DeserializeObject<JObject>(body);
            var sellOrders = (JArray)payload?["payload"]?["sell_orders"];
            var itemId = PrimeItemToItemId(primeName);

            if (sellOrders is null)
                throw new Exception($"No sell orders found: {payload}");

            foreach (var listing in sellOrders)
            {
                var itemToken = listing["item"];
                var idToken = itemToken?["id"];

                if (idToken is null)
                    continue;

                var id = idToken.ToObject<string>();

                if (itemId == id)
                    return listing;
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "GetCurrentListing");
        }

        return Optional.None<JToken>();
    }

    public bool GetSocketAliveStatus()
    {
        return marketSocket.IsAlive;
    }

    /// <summary>
    /// Post a review on the developers page
    /// </summary>
    /// <param name="message">The content of the review</param>
    /// <returns></returns>
    public async Task<bool> PostReview(string message = "Thank you for WFinfo!")
    {
        var msg = $"{{\"text\":\"{message}\",\"review_type\":\"1\"}}";
        var developers = new List<string> { "dimon222", "Dapal003", "Kekasi" };
        foreach (var developer in developers)
        {
            try
            {
                using var request = new HttpRequestMessage();
                request.RequestUri = new Uri($"https://api.warframe.market/v1/profile/{developer}/review");
                request.Method = HttpMethod.Post;
                request.Headers.Add("Authorization", $"JWT {_encryptedDataService.JWT}");
                request.Headers.Add("language", "en");
                request.Headers.Add("accept", "application/json");
                request.Headers.Add("platform", "pc");
                request.Headers.Add("auth_type", "header");
                request.Content = new StringContent(msg, Encoding.UTF8, "application/json");
                var response = await _client.SendAsync(request).ConfigureAwait(ConfigureAwaitOptions.None);
                var body = await response.DecompressContent().ConfigureAwait(ConfigureAwaitOptions.None);
                Logger.Debug("Body: {Body}, Content: {Msg}", body, msg);
            }
            catch (Exception e)
            {
                Logger.Error(e, "PostReview");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets the user's ingame name needed to make listings
    /// </summary>
    /// <returns></returns>
    private async Task SetIngameName()
    {
        using var request = new HttpRequestMessage();
        request.RequestUri = new Uri("https://api.warframe.market/v1/profile");
        request.Method = HttpMethod.Get;
        request.Headers.Add("Authorization", $"JWT {_encryptedDataService.JWT}");
        request.Headers.Add("language", "en");
        request.Headers.Add("accept", "application/json");
        request.Headers.Add("platform", "pc");
        request.Headers.Add("auth_type", "header");
        var response = await _client.SendAsync(request).ConfigureAwait(ConfigureAwaitOptions.None);
        var content = await response.DecompressContent().ConfigureAwait(ConfigureAwaitOptions.None);
        //setJWT(response.Headers);
        var profile = JsonConvert.DeserializeObject<JObject>(content);
        inGameName = profile["profile"]?.Value<string>("ingame_name");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CanHaveBluePrint(string name)
    {
        return name.Contains("Neuroptics") || name.Contains("Chassis") || name.Contains("Systems") ||
               name.Contains("Harness") || name.Contains("Wings");
    }

    private static string ReadFileContent(string file)
    {
        using var streamReader = new StreamReader(file);
        return streamReader.ReadToEnd();
    }

    public ValueTask Handle(LogCapture.LogCaptureLineChange logCaptureLineChange, CancellationToken cancellationToken)
    {
        LogChanged(logCaptureLineChange.Line);
        return ValueTask.CompletedTask;
    }

    [GeneratedRegex("\"check_code\": \".*?\"", RegexOptions.Compiled)]
    private static partial Regex CheckCodeRegEx();

    public ValueTask<WebSocketAliveStatusResponse> Handle(WebSocketAliveStatusRequest request, CancellationToken cancellationToken)
    {
        Logger.Debug("Checking websocket alive status. status={Status},at={At}", marketSocket.IsAlive, request.RequestedAt);
        return new ValueTask<WebSocketAliveStatusResponse>(new WebSocketAliveStatusResponse(marketSocket.IsAlive));
    }

    public async ValueTask Handle(WebSocketSetStatus notification, CancellationToken cancellationToken)
    {
        Logger.Debug("Setting websocket status. status={Status}", notification.Status);
        await SetWebsocketStatus(notification.Status);
    }

    public ValueTask<DataResponse> Handle(DataRequest request, CancellationToken cancellationToken)
    {
        var data = _relicData[request.Type.Index()];
        return new ValueTask<DataResponse>(new DataResponse(data));
    }
}
