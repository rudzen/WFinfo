using System.Buffers.Binary;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Akka.Util;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Serilog;
using Tesseract;
using WFInfo.Domain;
using WFInfo.Extensions;
using WFInfo.Services.HDRDetection;
using WFInfo.Services.Screenshot;
using WFInfo.Services.WindowInfo;
using WFInfo.Settings;
using Brushes = System.Drawing.Brushes;
using Clipboard = System.Windows.Forms.Clipboard;
using Color = System.Drawing.Color;
using Pen = System.Drawing.Pen;
using Point = System.Drawing.Point;

namespace WFInfo.Services.OpticalCharacterRecognition;

internal partial class OCR
{
    private static readonly ILogger Logger = Log.Logger.ForContext(typeof(OCR));

    private sealed record SkipZone(int LeftEdge, int RightEdge, int BottomEdge);

    #region variabels and sizzle

    private const NumberStyles Styles = NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands |
                                        NumberStyles.AllowExponent;

    // UI - Scaling used in Warframe
    public static double UiScaling { get; private set; }

    public static int NumberOfRewardsDisplayed { get; private set; }

    private static readonly Regex Re = WordTrimRegEx();

    // Pixel measurements for reward screen @ 1920 x 1080 with 100% scale https://docs.google.com/drawings/d/1Qgs7FU2w1qzezMK-G1u9gMTsQZnDKYTEU36UPakNRJQ/edit
    private const int PixelRewardWidth = 968;
    private const int PixelRewardHeight = 235;
    private const int PixelRewardYDisplay = 316;
    private const int PixelRewardLineHeight = 48;

    private const int ScalingLimit = 100;

    private static readonly Pen OrangePen = new(Brushes.Orange);
    private static readonly Pen PinkPen = new(Brushes.Pink);
    private static readonly Pen DarkCyanPen = new(Brushes.DarkCyan);
    private static readonly Pen RedPen = new(Brushes.Red);
    private static readonly Pen CyanPen = new(Brushes.Cyan);

    public static AtomicBoolean ProcessingActive { get; set; } = new();

    private static Bitmap? _bigScreenshot;
    private static Bitmap? _partialScreenshot;
    private static Bitmap? _partialScreenshotExpanded;

    private static string[] _firstChecks = [];
    private static Memory<int> _firstProximity = new([-1, -1, -1, -1]);
    private static string _timestamp = null!;

    private static string _clipboard = null!;

    #endregion

    private static ITesseractService _tesseractService = null!;
    private static ISoundPlayer _soundPlayer = null!;
    private static ApplicationSettings _settings = null!;
    private static IWindowInfoService _window = null!;
    private static IHDRDetectorService _hdrDetector = null!;
    private static IThemeDetector _themeDetector = null!;
    private static ISnapZoneDivider _snapZoneDivider = null!;
    private static IMediator _mediator = null!;

    private static IScreenshotService _gdiScreenshot = null!;
    private static IScreenshotService? _windowsScreenshot;

    public static void Init(IServiceProvider sp)
    {
        var tesseractService = sp.GetRequiredService<ITesseractService>();
        var soundPlayer = sp.GetRequiredService<ISoundPlayer>();
        var settings = sp.GetRequiredService<ApplicationSettings>();
        var window = sp.GetRequiredService<IWindowInfoService>();
        var themeDetector = sp.GetRequiredService<IThemeDetector>();
        var snapZoneDivider = sp.GetRequiredService<ISnapZoneDivider>();
        var hdrDetector = sp.GetRequiredService<IHDRDetectorService>();
        var mediator = sp.GetRequiredService<IMediator>();
        var gdiScreenshot = sp.GetRequiredKeyedService<IScreenshotService>(ScreenshotTypes.Gdi);
        var windowsScreenshot = sp.GetKeyedService<IScreenshotService>(ScreenshotTypes.WindowCapture);
        Init(
            tesseractService: tesseractService,
            soundPlayer: soundPlayer,
            settings: settings,
            window: window,
            themeDetector: themeDetector,
            snapZoneDivider: snapZoneDivider,
            hdrDetector: hdrDetector,
            mediator: mediator,
            gdiScreenshot: gdiScreenshot,
            windowsScreenshot: windowsScreenshot
        );
    }

    // You might be asking yourself "Why are you injecting specific services? That's not good practice at all!"
    // Now I can either do this and switch between these two based on settings
    // Or I can make this a scoped service, with each scope being a new screenshot request and dynamically choose the right service using a IScreenshotServiceFactory
    // Unfortunately option 2 means rewriting like half of this thing so I'm sticking with a hack
    private static void Init(
        ITesseractService tesseractService,
        ISoundPlayer soundPlayer,
        ApplicationSettings settings,
        IThemeDetector themeDetector,
        ISnapZoneDivider snapZoneDivider,
        IWindowInfoService window,
        IHDRDetectorService hdrDetector,
        IMediator mediator,
        IScreenshotService gdiScreenshot,
        IScreenshotService? windowsScreenshot = null)
    {
        Directory.CreateDirectory(ApplicationConstants.AppPathDebug);
        _tesseractService = tesseractService;
        _tesseractService.Init();
        _soundPlayer = soundPlayer;
        _settings = settings;
        _themeDetector = themeDetector;
        _snapZoneDivider = snapZoneDivider;
        _window = window;
        _hdrDetector = hdrDetector;
        _mediator = mediator;

        _gdiScreenshot = gdiScreenshot;
        _windowsScreenshot = windowsScreenshot;
    }

    internal static async Task ProcessRewardScreen(Bitmap? file = null)
    {
        #region initializers

        if (ProcessingActive)
        {
            await _mediator.Publish(new UpdateStatus("Still Processing Reward Screen", StatusSeverity.Warning));
            return;
        }

        var primeRewards = new List<string>();

        ProcessingActive.GetAndSet(true);
        await _mediator.Publish(new UpdateStatus("Processing..."));
        Logger.Debug(
            "----  Triggered Reward Screen Processing  ------------------------------------------------------------------");

        var time = DateTime.UtcNow;
        _timestamp = time.ToString("yyyy-MM-dd HH-mm-ssff", ApplicationConstants.Culture);
        var start = Stopwatch.GetTimestamp();

        var parts = new List<Bitmap>();

        _bigScreenshot = file ?? await CaptureScreenshot();
        try
        {
            parts.AddRange(ExtractPartBoxAutomatically(out var _, _bigScreenshot));
        }
        catch (Exception e)
        {
            ProcessingActive.GetAndSet(false);
            Logger.Error(e, "Error while extracting part boxes");
            return;
        }

        _firstChecks = new string[parts.Count];
        var tasks = new Task[parts.Count];
        for (var i = 0; i < parts.Count; i++)
        {
            var tempI = i;
            tasks[i] = Task.Factory.StartNew(() =>
            {
                _firstChecks[tempI] = GetTextFromImage(parts[tempI], _tesseractService.Engines[tempI]);
            });
        }

        Task.WaitAll(tasks);

        // Remove any empty (or suspiciously short) items from the array
        _firstChecks = _firstChecks
                       .Where(s => !string.IsNullOrEmpty(s) && s.Replace(" ", string.Empty).Length > 6)
                       .ToArray();
        if (_firstChecks == null || _firstChecks.Length == 0 || CheckIfError())
        {
            ProcessingActive.GetAndSet(false);
            var end = Stopwatch.GetElapsedTime(start);
            Logger.Debug(
                "----  Partial Processing Time, couldn't find rewards {Time}", end);
            await _mediator.Publish(new UpdateStatus("Couldn't find any rewards to display", StatusSeverity.Warning));
            if (_firstChecks == null)
            {
                // TODO (rudzen) : Add event
                Main.RunOnUIThread(() =>
                {
                    Main.SpawnErrorPopup(time);
                });
            }
        }

        var bestPlat = 0D;
        var bestDucat = 0;
        var bestPlatItem = 0;
        var bestDucatItem = 0;
        List<int> unownedItems = [];

        #endregion

        #region processing data

        if (_firstChecks.Length > 0)
        {
            NumberOfRewardsDisplayed = _firstChecks.Length;
            _clipboard = string.Empty;
            var width = (int)(PixelRewardWidth * _window.ScreenScaling * UiScaling) + 10;
            var startX = _window.Center.X - width / 2 + (int)(width * 0.004);

            if (_firstChecks.Length % 2 == 1)
                startX += width / 8;

            if (_firstChecks.Length <= 2)
                startX += 2 * (width / 8);

            var overWid = (int)(width / (4.1 * _window.DpiScaling));
            var startY = (int)(_window.Center.Y / _window.DpiScaling - 20 * _window.ScreenScaling * UiScaling);
            var partNumber = 0;
            var hideRewardInfo = false;
            for (var i = 0; i < _firstChecks.Length; i++)
            {
                var part = _firstChecks[i];

                #region found a part

                var correctName = Main.DataBase.GetPartName(part, out _firstProximity.Span[i], false, out _);
                var primeSetName = Data.GetSetName(correctName);
                var job = (JObject)Main.DataBase.MarketData.GetValue(correctName);
                var primeSet = (JObject)Main.DataBase.MarketData.GetValue(primeSetName);
                var ducats = job["ducats"].ToObject<string>();
                if (int.Parse(ducats, ApplicationConstants.Culture) == 0)
                {
                    hideRewardInfo = true;
                }

                //else if (correctName != "Kuva" || correctName != "Exilus Weapon Adapter Blueprint" || correctName != "Riven Sliver" || correctName != "Ayatan Amber Star")
                primeRewards.Add(correctName);
                var plat = job["plat"].ToObject<string>();
                string primeSetPlat = null;
                if (primeSet != null)
                    primeSetPlat = (string)primeSet["plat"];

                var platinum = double.Parse(plat, Styles, ApplicationConstants.Culture);
                var volume = job["volume"].ToObject<string>();
                var vaulted = Main.DataBase.IsPartVaulted(correctName);
                var mastered = Main.DataBase.IsPartMastered(correctName);
                var partsOwned = Main.DataBase.PartsOwned(correctName);
                var partsCount = Main.DataBase.PartsCount(correctName);
                var duc = int.Parse(ducats, ApplicationConstants.Culture);

                #endregion

                #region highlighting

                if (platinum >= bestPlat)
                {
                    bestPlat = platinum;
                    bestPlatItem = i;
                    if (duc >= bestDucat)
                    {
                        bestDucat = duc;
                        bestDucatItem = i;
                    }
                }

                if (duc > bestDucat)
                {
                    bestDucat = duc;
                    bestDucatItem = i;
                }

                if (duc > 0
                    && !mastered
                    && int.Parse(partsOwned, ApplicationConstants.Culture) < int.Parse(partsCount, ApplicationConstants.Culture))
                {
                    unownedItems.Add(i);
                }

                #endregion

                _clipboard = _settings.Clipboard
                    ? RewardScreenClipboard(
                        platinum: in platinum,
                        correctName: correctName,
                        plat: plat,
                        primeSetPlat: primeSetPlat,
                        ducats: ducats,
                        vaulted: vaulted,
                        partNumber: partNumber
                    )
                    : string.Empty;

                #region display part

                Overlay.RewardsDisplaying = true;

                if (_settings.IsOverlaySelected)
                {
                    var pos = new Point(
                        (int)((startX + width / 4 * partNumber + _settings.OverlayXOffsetValue) / _window.DpiScaling),
                        startY + (int)(_settings.OverlayYOffsetValue / _window.DpiScaling)
                    );

                    await _mediator.Publish(new OverlayUpdateData(
                        Index: partNumber,
                        CorrectName: correctName,
                        Plat: plat,
                        PrimeSetPlat: primeSetPlat,
                        Ducats: ducats,
                        Volume: volume,
                        Vaulted: vaulted,
                        Mastered: mastered,
                        PartsOwned: partsOwned,
                        PartsCount: partsCount,
                        HideRewardInfo: hideRewardInfo,
                        OverWid: overWid,
                        Position: pos
                    ));
                }

                Main.RunOnUIThread(() =>
                {
                    if (!_settings.IsOverlaySelected && !_settings.IsLightSelected)
                    {
                        // TODO (rudzen) : Add event
                        Main.RewardWindow.loadTextData(correctName, plat, primeSetPlat, ducats, volume, vaulted, mastered,
                            $"{partsOwned} / {partsCount}", partNumber, true, hideRewardInfo);
                    }

                    if (_settings.Clipboard && !string.IsNullOrEmpty(_clipboard))
                        Clipboard.SetText(_clipboard);
                });
                partNumber++;
                hideRewardInfo = false;

                #endregion
            }

            var end = Stopwatch.GetElapsedTime(start);
            await _mediator.Publish(new UpdateStatus($"Completed processing ({end})"));

            if (Main.ListingHelper.PrimeRewards.Count == 0 ||
                Main.ListingHelper.PrimeRewards[^1].Except(primeRewards).Any())
            {
                Main.ListingHelper.PrimeRewards.Add(primeRewards);
            }

            if (_settings.HighlightRewards)
            {
                foreach (var item in unownedItems.Where(x => x != bestDucatItem && x != bestPlatItem))
                    await _mediator.Publish(new OverlayUpdate(item, OverlayUpdateType.Owned));

                await _mediator.Publish(new OverlayUpdate(bestDucatItem, OverlayUpdateType.Ducat));
                await _mediator.Publish(new OverlayUpdate(bestPlatItem, OverlayUpdateType.Plat));
            }

            Logger.Debug(("----  Total Processing Time " + end + " ------------------------------------------------------------------------------------------")[..108]);
        }

        #endregion

        // light mode doesn't have any visual confirmation that the ocr has finished, thus we use a sound to indicate this.
        if (_settings.IsLightSelected && _clipboard.Length > 3)
            _soundPlayer.Play();

        var directory = new DirectoryInfo(ApplicationConstants.AppPathDebug);
        var fileCreationTimeThreshold = DateTime.Now.AddHours(-1 * _settings.ImageRetentionTime);
        var filesToDelete = directory
                            .GetFiles()
                            .Where(f => f.CreationTime < fileCreationTimeThreshold);

        foreach (var fileToDelete in filesToDelete)
            fileToDelete.Delete();

        if (_partialScreenshot is not null)
        {
            var path = Path.Combine(ApplicationConstants.AppPathDebug, $"PartBox {_timestamp}.png");
            _partialScreenshot.Save(path);
            _partialScreenshot.Dispose();
            _partialScreenshot = null;
        }

        ProcessingActive.GetAndSet(false);
    }

    #region clipboard

    private static string RewardScreenClipboard(
        in double platinum,
        string correctName,
        string plat,
        string? primeSetPlat,
        string ducats,
        bool vaulted,
        int partNumber)
    {
        var sb = new StringBuilder(64);

        if (platinum > 0)
        {
            if (!string.IsNullOrEmpty(_clipboard))
                sb.Append("-  ");

            sb.Append('[');
            sb.Append(correctName.Replace(" Blueprint", string.Empty));
            sb.Append("]: ").Append(plat).Append(":platinum: ");

            if (primeSetPlat is not null)
                sb.Append("Set: ").Append(primeSetPlat).Append(":platinum: ");

            if (_settings.ClipboardVaulted)
            {
                sb.Append(ducats).Append(":ducats:");
                if (vaulted)
                    sb.Append("(V)");
            }
        }

        if (partNumber == _firstChecks.Length - 1 && sb.Length > 0)
            sb.Append(_settings.ClipboardTemplate);

        if (sb.Length > 0)
        {
            var clip = sb.ToString();
            Logger.Debug("Clipboard msg: {Clip}", clip);
            return clip;
        }

        return string.Empty;
    }

    #endregion clipboard

    private static bool CheckIfError()
    {
        if (_firstChecks.Length == 0 || _firstProximity.Length == 0)
            return false;

        const double errorDetectionThreshold = 0.25;

        var max = Math.Min(_firstChecks.Length, _firstProximity.Length);
        for (var i = 0; i < max; i++)
            if (_firstProximity.Span[i] > errorDetectionThreshold * _firstChecks[i].Length)
                return true;

        return false;
    }

    public static WFtheme GetThemeWeighted(out double closestThresh, Bitmap? image = null)
    {
        image ??= CaptureScreenshot().GetAwaiter().GetResult();
        var theme = _themeDetector.GetThemeWeighted(out closestThresh, image);
        return theme;
    }

    /// <summary>
    /// Checks if partName is close enough to valid to actually process
    /// </summary>
    /// <param name="partName">Scanned part name</param>
    /// <returns>If part name is close enough to valid to actually process</returns>
    private static bool PartNameValid(string partName)
    {
        // if part name is smaller than "Bo prime handle" skip current part
        //TODO: Add a min character for other locale here.
        if ((partName.Length < 13 && _settings.Locale == "en") ||
            (partName.Replace(" ", string.Empty).Length < 6 && _settings.Locale == "ko"))
            return false;
        return true;
    }

    /// <summary>
    /// Processes the image the user cropped in the selection
    /// </summary>
    /// <param name="snapItImage"></param>
    /// <param name="fullShot"></param>
    /// <param name="snapItOrigin"></param>
    internal static async Task ProcessSnapIt(
        Bitmap snapItImage,
        Bitmap fullShot,
        Point snapItOrigin)
    {
        var start = Stopwatch.GetTimestamp();
        var now = DateTime.UtcNow;
        var timeStamp = now.ToString("yyyy-MM-dd HH-mm-ssff", ApplicationConstants.Culture);

        var theme = GetThemeWeighted(out _, fullShot);

        snapItImage.Save(Path.Combine(ApplicationConstants.AppPathDebug, $"SnapItImage {timeStamp}.png"));

        var snapItImageFiltered = ScaleUpAndFilter(snapItImage, theme, out var rowHits, out var colHits);

        snapItImageFiltered.Save(Path.Combine(ApplicationConstants.AppPathDebug, $"SnapItImageFiltered {timeStamp}.png"));

        // TODO: (rudzen) : Convert foundParts to queue
        var foundParts = FindAllParts(snapItImageFiltered, snapItImage, rowHits, colHits);
        var end = Stopwatch.GetElapsedTime(start);

        snapItImage.Dispose();
        snapItImageFiltered.Dispose();

        await _mediator.Publish(new UpdateStatus($"Snap-it completed processing. time={end}"));

        var csvDate = now.ToString("yyyy-MM-dd", ApplicationConstants.Culture);
        var csv = string.Empty;
        if (_settings.SnapitExport && !File.Exists(Path.Combine(ApplicationConstants.AppPath, "export " + csvDate + ".csv")))
            csv += "ItemName,Plat,Ducats,Volume,Vaulted,Owned,partsDetected" +
                   csvDate + Environment.NewLine;

        var resultCount = foundParts.Count;
        for (var i = 0; i < foundParts.Count; i++)
        {
            var part = foundParts[i];
            if (!PartNameValid(part.Name))
            {
                // remove invalid part from list to not clog VerifyCount. Decrement to not skip any entries
                foundParts.RemoveAt(i--);
                resultCount--;
                continue;
            }

            Logger.Debug("Snap-it processing part {Part} out of {Count}", i, foundParts.Count);

            // TODO (rudzen) : Convert to request event?
            var name = Main.DataBase.GetPartName(part.Name, out var levenDist, false, out var multipleLowest);
            var primeSetName = Data.GetSetName(name);

            // show warning triangle if the result is of questionable accuracy. The limit is basically arbitrary
            if (levenDist > Math.Min(part.Name.Length, name.Length) / 3 || multipleLowest)
                part.Warning = true;

            var doWarn = part.Warning;
            part.Name = name;
            foundParts[i] = part;

            // TODO (rudzen) : Convert to request event?
            var job = Main.DataBase.MarketData.GetValue(name).ToObject<JObject>();

            // TODO (rudzen) : Convert to request event?
            var primeSet = (JObject)Main.DataBase.MarketData.GetValue(primeSetName);

            var plat = job["plat"].ToObject<string>();

            string primeSetPlat = null;
            if (primeSet is not null)
                primeSetPlat = (string)primeSet["plat"];

            var ducats = job["ducats"].ToObject<string>();
            var volume = job["volume"].ToObject<string>();

            // TODO (rudzen) : Convert to request events?
            var vaulted = Main.DataBase.IsPartVaulted(name);
            var mastered = Main.DataBase.IsPartMastered(name);
            var partsOwned = Main.DataBase.PartsOwned(name);

            var partsDetected = part.Count.ToString();

            if (_settings.SnapitExport)
            {
                var owned = string.IsNullOrEmpty(partsOwned) ? "0" : partsOwned;
                csv += name + "," + plat + "," + ducats + "," + volume + "," + vaulted.ToString(ApplicationConstants.Culture) + "," +
                       owned + "," + partsDetected + ", \"\"" + Environment.NewLine;
            }

            var width = Math.Clamp((int)(part.Bounding.Width * _window.ScreenScaling), _settings.MinOverlayWidth, _settings.MaxOverlayWidth);

            Main.RunOnUIThread(() =>
            {
                var itemOverlay = new Overlay(_settings);
                itemOverlay.LoadTextData(name, plat, primeSetPlat, ducats, volume, vaulted, mastered, partsOwned,
                    partsDetected, false, doWarn);
                itemOverlay.toSnapit();
                itemOverlay.Resize(width);
                itemOverlay.Display(
                    (int)(_window.Window.X + snapItOrigin.X + (part.Bounding.X - width / 8) / _window.DpiScaling),
                    (int)((_window.Window.Y + snapItOrigin.Y + part.Bounding.Y - itemOverlay.Height) /
                          _window.DpiScaling), _settings.SnapItDelay);
            });
        }

        // TODO (rudzen) : COnvert to notification event
        if (_settings.DoSnapItCount && resultCount > 0)
            Main.RunOnUIThread(() =>
            {
                VerifyCount.ShowVerifyCount(foundParts);
            });

        if (Main.SnapItOverlayWindow.tempImage is not null)
            Main.SnapItOverlayWindow.tempImage.Dispose();

        end = Stopwatch.GetElapsedTime(start);

        if (resultCount == 0)
        {
            await _mediator.Publish(new UpdateStatus($"Snap-it couldn't find any items to display. time={end}", StatusSeverity.Error));
            Main.RunOnUIThread(() =>
            {
                Main.SpawnErrorPopup(DateTime.UtcNow);
            });
        }
        else
        {
            await _mediator.Publish(new UpdateStatus($"Snap-it completed, displaying. time={end}"));
        }

        Logger.Debug("Snap-it finished, displayed reward. count={Count},time={Time}", resultCount, end);
        if (_settings.SnapitExport)
        {
            var file = Path.Combine(ApplicationConstants.AppPath, $"export {csvDate}.csv");
            await File.AppendAllTextAsync(file, csv);
        }
    }

    private static List<TextWithBounds> GetTextWithBoundsFromImage(
        TesseractEngine engine, Bitmap image,
        int rectXOffset, int rectYOffset)
    {
        List<TextWithBounds> data = [];

        using var page = engine.Process(image, PageSegMode.SparseText);
        using var iterator = page.GetIterator();
        iterator.Begin();
        do
        {
            var currentWord = iterator.GetText(PageIteratorLevel.TextLine);

            if (string.IsNullOrEmpty(currentWord))
                continue;

            currentWord = Re.Replace(currentWord, string.Empty).Trim();

            if (currentWord.Length == 0)
                continue;

            iterator.TryGetBoundingBox(PageIteratorLevel.TextLine, out var boxBounds);

            var bounds = new Rectangle(
                boxBounds.X1 + rectXOffset,
                boxBounds.Y1 + rectYOffset,
                boxBounds.Width,
                boxBounds.Height
            );

            //word is valid start comparing to others
            data.Add(new TextWithBounds(currentWord, bounds));
        } while (iterator.Next(PageIteratorLevel.TextLine));

        return data;
    }

    /// <summary>
    /// Filters out any group of words and addes them all into a single InventoryItem, containing the found words as well as the bounds within they reside.
    /// </summary>
    /// <returns>List of found items</returns>
    private static List<InventoryItem> FindAllParts(
        Bitmap filteredImage,
        Bitmap unfilteredImage,
        int[] rowHits,
        int[] colHits)
    {
        var filteredImageClean = new Bitmap(filteredImage);
        var time = DateTime.UtcNow;
        var timestamp = time.ToString("yyyy-MM-dd HH-mm-ssff", ApplicationConstants.Culture);

        //List containing Tuples of overlapping InventoryItems and their combined bounds
        List<FoundItem> foundItems = [];
        var numberTooLarge = 0;
        var numberTooFewCharacters = 0;
        var numberTooLargeButEnoughCharacters = 0;
        var orange = new Pen(Brushes.Orange);
        var red = new SolidBrush(Color.FromArgb(100, 139, 0, 0));
        var green = new SolidBrush(Color.FromArgb(100, byte.MaxValue, 165, 0));
        var greenp = new Pen(green);
        var pinkP = new Pen(Brushes.Pink);
        var font = new Font("Arial", 16);
        List<SnapZone> zones;
        int snapThreads;
        if (_settings.SnapMultiThreaded)
        {
            zones = _snapZoneDivider.DivideSnapZones(filteredImage, filteredImageClean, rowHits, colHits);
            // zones = DivideSnapZones(filteredImage, filteredImageClean, rowHits, colHits);
            snapThreads = 4;
        }
        else
        {
            zones =
            [
                new(filteredImageClean, new Rectangle(0, 0, filteredImageClean.Width, filteredImageClean.Height))
            ];
            snapThreads = 1;
        }

        var snapTasks = new Task<List<TextWithBounds>>[snapThreads];

        for (var i = 0; i < snapThreads; i++)
        {
            var tempI = i;
            snapTasks[i] = Task.Factory.StartNew(() =>
            {
                List<TextWithBounds> taskResults = [];
                for (var j = tempI; j < zones.Count; j += snapThreads)
                {
                    //process images
                    var currentResult = GetTextWithBoundsFromImage(
                        engine: _tesseractService.Engines[tempI],
                        image: zones[j].Bitmap,
                        rectXOffset: zones[j].Rectangle.X,
                        rectYOffset: zones[j].Rectangle.Y
                    );
                    taskResults.AddRange(currentResult);
                }

                return taskResults;
            });
        }

        Task.WaitAll(snapTasks);

        for (var threadNum = 0; threadNum < snapThreads; threadNum++)
        {
            // TODO (rudzen) : Insert space before capital letter if it's not the first letter?
            foreach (var (currentWord, bounds) in snapTasks[threadNum].Result)
            {
                //word is valid start comparing to others
                var verticalPad = bounds.Height / 2;
                var horizontalPad = (int)(bounds.Height * _settings.SnapItHorizontalNameMargin);
                var paddedBounds = new Rectangle(
                    x: bounds.X - horizontalPad,
                    y: bounds.Y - verticalPad,
                    width: bounds.Width + horizontalPad * 2,
                    height: bounds.Height + verticalPad * 2
                );
                //var paddedBounds = new Rectangle(bounds.X - bounds.Height / 3, bounds.Y - bounds.Height / 3, bounds.Width + bounds.Height, bounds.Height + bounds.Height / 2);

                using (var g = Graphics.FromImage(filteredImage))
                {
                    if (paddedBounds.Height > 50 * _window.ScreenScaling ||
                        paddedBounds.Width > 84 * _window.ScreenScaling)
                    {
                        //Determine whether or not the box is too large, false positives in OCR can scan items (such as neuroptics, chassis or systems) as a character(s).
                        if (currentWord.Length > 3)
                        {
                            // more than 3 characters in a box too large is likely going to be good, pass it but mark as potentially bad
                            g.DrawRectangle(orange, paddedBounds);
                            numberTooLargeButEnoughCharacters++;
                        }
                        else
                        {
                            g.FillRectangle(red, paddedBounds);
                            numberTooLarge++;
                            continue;
                        }
                    }
                    else if (currentWord.Length < 2 && _settings.Locale == "en")
                    {
                        g.FillRectangle(green, paddedBounds);
                        numberTooFewCharacters++;
                        continue;
                    }
                    else
                    {
                        g.DrawRectangle(pinkP, paddedBounds);
                    }

                    g.DrawRectangle(greenp, bounds);
                    g.DrawString(currentWord, font, Brushes.Pink, new Point(paddedBounds.X, paddedBounds.Y));
                }

                var i = foundItems.Count - 1;

                for (; i >= 0; i--)
                    if (foundItems[i].Rectangle.IntersectsWith(paddedBounds))
                        break;

                if (i == -1)
                {
                    //New entry added by creating a tuple. Item1 in tuple is list with just the newly found item, Item2 is its bounds
                    foundItems.Add(new FoundItem([new InventoryItem(currentWord, paddedBounds)], paddedBounds));
                }
                else
                {
                    var left = Math.Min(foundItems[i].Rectangle.Left, paddedBounds.Left);
                    var top = Math.Min(foundItems[i].Rectangle.Top, paddedBounds.Top);
                    var right = Math.Max(foundItems[i].Rectangle.Right, paddedBounds.Right);
                    var bot = Math.Max(foundItems[i].Rectangle.Bottom, paddedBounds.Bottom);

                    var combinedBounds = new Rectangle(left, top, right - left, bot - top);

                    List<InventoryItem> tempList =
                    [
                        ..foundItems[i].Items,
                        new InventoryItem(currentWord, paddedBounds)
                    ];
                    foundItems.RemoveAt(i);
                    foundItems.Add(new FoundItem(tempList, combinedBounds));
                }
            }
        }

        List<InventoryItem> results = [];

        foreach (var itemGroup in foundItems)
        {
            itemGroup.Items.Sort(InventoryItem.Comparer);

            // Combine into item name
            var name = itemGroup.Items.Aggregate(string.Empty, (current, i1) => current + $"{i1.Name} ");

            results.Add(new InventoryItem(name.Trim(), itemGroup.Rectangle));
        }

        if (_settings.DoSnapItCount)
        {
            GetItemCounts(filteredImage, filteredImageClean, unfilteredImage, results, font);
        }

        filteredImageClean.Dispose();
        red.Dispose();
        green.Dispose();
        orange.Dispose();
        pinkP.Dispose();
        greenp.Dispose();
        font.Dispose();
        if (numberTooLarge > .3 * foundItems.Count || numberTooFewCharacters > .4 * foundItems.Count)
        {
            //Log old noise level heuristics
            Logger.Debug(
                "numberTooLarge: {TooLarge}, numberTooFewCharacters: {TooFew}, numberTooLargeButEnoughCharacters: {TooLargeButEnough}, foundItems: {Found}",
                numberTooLarge, numberTooFewCharacters, numberTooLargeButEnoughCharacters, foundItems.Count);
        }

        filteredImage.Save(Path.Combine(ApplicationConstants.AppPathDebug, $"SnapItImageBounds {timestamp}.png"));
        return results;
    }

    /// <summary>
    /// Gets the item count of an item by estimating what regions should contain item counts. Filters out noise in the image, aggressiveness based on <c>threshold</c>
    /// </summary>
    /// <param name="filteredImage">Image to draw debug markings on</param>
    /// <param name="filteredImageClean">Image to use for scan</param>
    /// <param name="foundItems">Item list, used to deduce grid system. Modified to add Count for items</param>
    /// <param name="threshold">If the amount of adjacent black pixels (including itself) is below this number, it will be converted to white before the number scanning</param>
    /// <param name="font">Font used for debug marking</param>
    /// <returns>Nothing, but if successful <c>foundItems</c> will be modified</returns>
    private static unsafe void GetItemCounts(
        Bitmap filteredImage,
        Bitmap filteredImageClean,
        Bitmap unfilteredImage,
        List<InventoryItem> foundItems,
        Font font)
    {
        Logger.Debug("Starting Item Counting");
        using var g = Graphics.FromImage(filteredImage);
        // Sort for easier processing in loop below
        var foundItemsBottom = foundItems.OrderBy(o => o.Bounding.Bottom).ToList();

        // Filter out bad parts for more accurate grid
        var itemRemoved = false;
        for (var i = 0; i < foundItemsBottom.Count; i += itemRemoved ? 0 : 1)
        {
            itemRemoved = false;
            if (!PartNameValid(foundItemsBottom[i].Name))
            {
                foundItemsBottom.RemoveAt(i);
                itemRemoved = true;
            }
        }

        var foundItemsLeft = foundItemsBottom.OrderBy(o => o.Bounding.Left).ToList();

        //features of grid system
        List<Rectangle> rows = [];
        List<Rectangle> columns = [];

        const int pixelSize = 4;

        Span<byte> black = stackalloc byte[] { 0, 0, 0, byte.MaxValue };
        var white = Color.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);

        // Temporary buffers for color conversion
        Span<byte> currentPixelSpan;
        Span<byte> previousPixelSpan;
        Span<byte> nextPixelSpan;
        var bgra = 0;
        var argb = 0;

        for (var i = 0; i < foundItemsBottom.Count; i++)
        {
            var currRow = new Rectangle(
                x: 0,
                y: foundItemsBottom[i].Bounding.Y,
                width: 10000,
                height: foundItemsBottom[i].Bounding.Height
            );
            var currColumn = new Rectangle(
                x: foundItemsLeft[i].Bounding.X,
                y: 0,
                width: foundItemsLeft[i].Bounding.Width,
                height: 10000
            );

            // Find or improve latest ColumnsRight
            if (rows.Count == 0 || !currRow.IntersectsWith(rows[^1]))
            {
                rows.Add(currRow);
            }
            else
            {
                if (currRow.Bottom < rows[^1].Bottom)
                    rows[^1] = new Rectangle(
                        x: 0,
                        y: rows[^1].Y,
                        width: 10000,
                        height: currRow.Bottom - rows[^1].Top
                    );

                if (rows.Count != 1 && currColumn.Top > columns[^1].Top)
                    rows[^1] = new Rectangle(
                        x: 0,
                        y: currRow.Y,
                        width: 10000,
                        height: rows[^1].Bottom - currRow.Top
                    );
            }

            //find or improve latest ColumnsRight
            if (columns.Count == 0 || !currColumn.IntersectsWith(columns[^1]))
            {
                columns.Add(currColumn);
            }
            else
            {
                if (currColumn.Right < columns[^1].Right)
                    columns[^1] = new Rectangle(
                        x: columns[^1].X,
                        y: 0,
                        width: currColumn.Right - columns[^1].X,
                        height: 10000
                    );

                if (columns.Count != 1 && currColumn.Left > columns[^1].Left)
                    columns[^1] = new Rectangle(
                        x: currColumn.X,
                        y: 0,
                        width: columns[^1].Right - currColumn.X,
                        height: 10000
                    );
            }
        }

        // Draw debug markings for grid system
        foreach (var col in columns)
        {
            g.DrawLine(DarkCyanPen, col.Right, 0, col.Right, 10000);
            g.DrawLine(DarkCyanPen, col.X, 0, col.X, 10000);
        }

        foreach (var bottom in rows.Select(x => x.Bottom))
            g.DrawLine(DarkCyanPen, 0, bottom, 10000, bottom);

        // Set OCR to numbers only
        _tesseractService.FirstEngine.SetVariable("tessedit_char_whitelist", "0123456789");

        var widthMultiplier = _settings.DoCustomNumberBoxWidth ? _settings.SnapItNumberBoxWidth : 0.4;

        Bitmap cloneBitmap = null!;

        // Process grid system
        for (var i = 0; i < rows.Count; i++)
        {
            for (var j = 0; j < columns.Count; j++)
            {
                //edges of current area to scan
                var left = j == 0 ? 0 : (columns[j - 1].Right + columns[j].X) / 2;
                var top = i == 0 ? 0 : rows[i - 1].Bottom;
                var width = Math.Min((int)((columns[j].Right - left) * widthMultiplier), filteredImage.Size.Width - left);
                var height = Math.Min((rows[i].Bottom - top) / 3, filteredImage.Size.Height - top);

                var cloneRect = new Rectangle(left, top, width, height);
                g.DrawRectangle(CyanPen, cloneRect);
                cloneBitmap?.Dispose();
                cloneBitmap = filteredImageClean.Clone(cloneRect, filteredImageClean.PixelFormat);

                //get cloneBitmap as array for fast access
                var imgWidth = cloneBitmap.Width;
                var imgHeight = cloneBitmap.Height;

                var lockedBitmapData = cloneBitmap.LockBits(
                    rect: new Rectangle(0, 0, cloneBitmap.Width, cloneBitmap.Height),
                    flags: ImageLockMode.WriteOnly,
                    format: cloneBitmap.PixelFormat
                );
                var numBytes = Math.Abs(lockedBitmapData.Stride) * cloneBitmap.Height;
                //Format is ARGB, in order BGRA
                var lockedBitmapBytes = new Span<byte>(lockedBitmapData.Scan0.ToPointer(), numBytes);

                cloneBitmap.UnlockBits(lockedBitmapData);

                //find "center of mass" for black pixels in the area
                var x = 0;
                var y = 0;
                int index;
                var xCenter = 0;
                var yCenter = 0;
                var sumBlack = 1;
                for (index = 0; index < numBytes; index += pixelSize)
                {
                    if (!lockedBitmapBytes.Slice(index, pixelSize).SequenceEqual(black))
                        continue;

                    y = (index / pixelSize) / imgWidth;
                    x = (index / pixelSize) % imgWidth;
                    yCenter += y;
                    xCenter += x;
                    sumBlack++;
                }

                xCenter /= sumBlack;
                yCenter /= sumBlack;

                //not enough black = ignore and move on
                if (sumBlack < height)
                    continue;

                //mark first-pass center
                filteredImage.SetPixel(left + xCenter, top + yCenter, Color.Red);

                //get the distance to closest edge of image
                var minToEdge = Math.Min(Math.Min(xCenter, imgWidth - xCenter), Math.Min(yCenter, imgHeight - yCenter));

                //we're expected to be within the checkmark + circle, find closest black pixel to find some part of it to start at
                for (var dist = 0; dist < minToEdge; dist++)
                {
                    x = xCenter + dist;
                    y = yCenter;
                    index = pixelSize * (x + y * imgWidth);
                    if (lockedBitmapBytes.Slice(index, pixelSize).SequenceEqual(black))
                        break;

                    x = xCenter - dist;
                    y = yCenter;
                    index = pixelSize * (x + y * imgWidth);
                    if (lockedBitmapBytes.Slice(index, pixelSize).SequenceEqual(black))
                        break;

                    x = xCenter;
                    y = yCenter + dist;
                    index = pixelSize * (x + y * imgWidth);
                    if (lockedBitmapBytes.Slice(index, pixelSize).SequenceEqual(black))
                        break;

                    x = xCenter;
                    y = yCenter - dist;
                    index = pixelSize * (x + y * imgWidth);
                    if (lockedBitmapBytes.Slice(index, pixelSize).SequenceEqual(black))
                        break;
                }

                //find "center of mass" for just the circle+checkmark icon
                var xCenterNew = x;
                var yCenterNew = y;
                var rightmost = 0; //rightmost edge of circle+checkmark icon
                sumBlack = 1;
                //use "flood search" approach from the pixel found above to find the whole checkmark+circle icon
                var searchSpace = new Stack<Point>();
                var pixelsChecked = new Dictionary<Point, bool>();
                searchSpace.Push(new Point(x, y));
                while (searchSpace.Count > 0)
                {
                    var p = searchSpace.Pop();
                    ref var pixelCheckedValue = ref CollectionsMarshal.GetValueRefOrNullRef(pixelsChecked, p);
                    if (Unsafe.IsNullRef(ref pixelCheckedValue) || pixelCheckedValue)
                        continue;

                    pixelCheckedValue = true;
                    for (var xOff = -2; xOff <= 2; xOff++)
                    {
                        for (var yOff = -2; yOff <= 2; yOff++)
                        {
                            if (p.X + xOff <= 0 || p.X + xOff >= imgWidth || p.Y + yOff <= 0 || p.Y + yOff >= imgHeight)
                                continue;

                            index = pixelSize * (p.X + xOff + (p.Y + yOff) * imgWidth);

                            if (!lockedBitmapBytes.Slice(index, pixelSize).SequenceEqual(black))
                                continue;

                            searchSpace.Push(new Point(p.X + xOff, p.Y + yOff));
                            xCenterNew += p.X + xOff;
                            yCenterNew += p.Y + yOff;
                            sumBlack++;
                            if (p.X + xOff > rightmost)
                                rightmost = p.X + xOff;
                        }
                    }
                }

                // Not enough black = ignore and move on
                if (sumBlack < height)
                    continue;

                xCenterNew /= sumBlack;
                yCenterNew /= sumBlack;

                //Search slight bit up and down to get well within the long line of the checkmark
                var lowest = yCenterNew + 1000;
                var highest = yCenterNew - 1000;
                for (var yOff = -5; yOff < 5; yOff++)
                {
                    var checkY = yCenterNew + yOff;

                    if (checkY <= 0 || checkY >= imgHeight)
                        continue;

                    index = pixelSize * (xCenterNew + (checkY) * imgWidth);

                    if (!lockedBitmapBytes.Slice(index, pixelSize).SequenceEqual(black))
                        continue;

                    if (checkY > highest)
                        highest = checkY;

                    if (checkY < lowest)
                        lowest = checkY;
                }

                // can't overflow here, since the image is at most 10000x10000
                yCenterNew = (highest + lowest) / 2;

                //mark second-pass center
                filteredImage.SetPixel(left + xCenterNew, top + yCenterNew, Color.Magenta);

                var cloneBitmapColoured = unfilteredImage.Clone(cloneRect, filteredImageClean.PixelFormat);

                //debugging markings and save, uncomment as needed
                //cloneBitmap.SetPixel(xCenter, yCenter, Color.Red);
                //cloneBitmap.SetPixel(xCenterNew, yCenterNew, Color.Magenta);
                //cloneBitmap.Save(ApplicationConstants.AppPath + @"\Debug\NumberCenter_" + i + "_" + j + "_" + sumBlack + " " + timestamp + ".png");
                //cloneBitmapColoured.Save(ApplicationConstants.AppPath + @"\Debug\ColoredNumberCenter_" + i + "_" + j + "_" + sumBlack + " " + timestamp + ".png");

                //get cloneBitmapColoured as array for fast access
                imgHeight = cloneBitmapColoured.Height;
                imgWidth = cloneBitmapColoured.Width;

                lockedBitmapData = cloneBitmapColoured.LockBits(
                    rect: new Rectangle(0, 0, imgWidth, cloneBitmapColoured.Height),
                    flags: ImageLockMode.WriteOnly,
                    format: cloneBitmapColoured.PixelFormat
                );
                numBytes = Math.Abs(lockedBitmapData.Stride) * lockedBitmapData.Height;
                //Format is ARGB, in order BGRA
                lockedBitmapBytes = new Span<byte>(lockedBitmapData.Scan0.ToPointer(), numBytes);
                cloneBitmapColoured.UnlockBits(lockedBitmapData);

                // Search diagonally from second-pass center for colours frequently occuring 3 pixels in a row horizontally.
                // Most common one of these should be the "amount label background colour"
                var pointsToCheck = new Queue<Point>();
                pointsToCheck.Enqueue(new Point(xCenterNew, yCenterNew + 1));
                pointsToCheck.Enqueue(new Point(xCenterNew, yCenterNew - 1));
                var colorHits = new Dictionary<Color, int>();
                var stop = false;
                while (pointsToCheck.Count > 0)
                {
                    var p = pointsToCheck.Dequeue();
                    var offset = p.Y > yCenter ? 1 : -1;

                    // Keep going until we almost hit the edge of the image
                    if (p.X + 3 > width || p.X - 3 < 0 || p.Y + 3 > imgHeight || p.Y - 3 < 0)
                        stop = true;

                    if (!stop)
                        pointsToCheck.Enqueue(new Point(p.X + offset, p.Y + offset));

                    index = pixelSize * (p.X + p.Y * imgWidth);
                    currentPixelSpan = lockedBitmapBytes.Slice(index, pixelSize);
                    previousPixelSpan = lockedBitmapBytes.Slice(index - pixelSize, pixelSize);
                    nextPixelSpan = lockedBitmapBytes.Slice(index + pixelSize, pixelSize);

                    if (!currentPixelSpan.SequenceEqual(previousPixelSpan) || !currentPixelSpan.SequenceEqual(nextPixelSpan))
                        continue;

                    bgra = MemoryMarshal.Read<int>(currentPixelSpan);
                    argb = BinaryPrimitives.ReverseEndianness(bgra);
                    var color = Color.FromArgb(argb);
                    ref var colorHit = ref CollectionsMarshal.GetValueRefOrAddDefault(
                        dictionary: colorHits,
                        key: color,
                        exists: out var exists
                    );
                    if (!exists)
                        colorHit = 1;
                    else
                        colorHit++;
                }

                // detect highest occuring colour
                // TODO (rudzen) : refactor to own method(?)
                var topColor = white;
                var topColorScore = 0;
                foreach (var (key, value) in colorHits)
                {
                    if (value <= topColorScore)
                        continue;

                    topColor = key;
                    topColorScore = value;
                    //Debug.WriteLine("Color: " + key.ToString() + ", Value: " + colorHits[key]);
                }

                Logger.Debug("Top Color: {Color}, Value: {Value}", topColor, topColorScore);

                // If most common colour is our default value, ignore and move on
                if (topColor == white)
                    continue;

                // Get unfilteredImage as array for fast access
                imgWidth = unfilteredImage.Width;

                lockedBitmapData = unfilteredImage.LockBits(
                    rect: new Rectangle(0, 0, imgWidth, unfilteredImage.Height),
                    flags: ImageLockMode.WriteOnly,
                    format: unfilteredImage.PixelFormat
                );
                numBytes = Math.Abs(lockedBitmapData.Stride) * lockedBitmapData.Height;
                // Format is ARGB, in order BGRA
                lockedBitmapBytes = new Span<byte>(lockedBitmapData.Scan0.ToPointer(), numBytes);

                unfilteredImage.UnlockBits(lockedBitmapData);

                // Recalculate centers to be relative to whole image
                rightmost = rightmost + left + 1;
                xCenter += left;
                yCenter += top;
                xCenterNew += left;
                yCenterNew += top;
                Logger.Debug("Old Center {X}, {Y}", xCenter, yCenter);
                Logger.Debug("New Center {X}, {Y}", xCenterNew, yCenterNew);

                // Search diagonally (toward top-right) from second-pass center until we find the "amount label" colour
                x = xCenterNew;
                y = yCenterNew;
                index = 4 * (x + y * imgWidth);
                currentPixelSpan = lockedBitmapBytes.Slice(index, pixelSize);
                bgra = MemoryMarshal.Read<int>(currentPixelSpan);
                argb = BinaryPrimitives.ReverseEndianness(bgra);
                var currColor = Color.FromArgb(argb);
                while (x < imgWidth && y > 0 && topColor != currColor)
                {
                    index = pixelSize * (++x + --y * imgWidth);
                    currentPixelSpan = lockedBitmapBytes.Slice(index, pixelSize);
                    bgra = MemoryMarshal.Read<int>(currentPixelSpan);
                    argb = BinaryPrimitives.ReverseEndianness(bgra);
                    currColor = Color.FromArgb(argb);
                }

                // TODO (rudzen) : find a clever optimization for the following while loops

                // Then search for top edge
                top = y;
                while (topColor == Color.FromArgb(
                           alpha: lockedBitmapBytes[index + 3],
                           red: lockedBitmapBytes[index + 2],
                           green: lockedBitmapBytes[index + 1],
                           blue: lockedBitmapBytes[index]))
                {
                    index = pixelSize * (x + --top * imgWidth);
                }

                top += 2;
                index = pixelSize * (x + top * imgWidth);

                // Search for left edge
                left = x;
                while (topColor == Color.FromArgb(
                           alpha: lockedBitmapBytes[index + 3],
                           red: lockedBitmapBytes[index + 2],
                           green: lockedBitmapBytes[index + 1],
                           blue: lockedBitmapBytes[index]))
                {
                    index = pixelSize * (--left + top * imgWidth);
                }

                left += 2;
                index = pixelSize * (left + top * imgWidth);

                // Search for height (bottom edge)
                height = 0;
                while (topColor == Color.FromArgb(
                           lockedBitmapBytes[index + 3],
                           lockedBitmapBytes[index + 2],
                           lockedBitmapBytes[index + 1],
                           lockedBitmapBytes[index]))
                {
                    index = pixelSize * (left + (top + ++height) * imgWidth);
                }

                height -= 2;

                // Cut out checkmark+circle icon
                left = rightmost;
                index = pixelSize * (left + (top + height) * imgWidth);

                // Search for width
                width = 0;
                while (topColor == Color.FromArgb(
                           lockedBitmapBytes[index + 3],
                           lockedBitmapBytes[index + 2],
                           lockedBitmapBytes[index + 1],
                           lockedBitmapBytes[index]))
                {
                    index = pixelSize * (left + ++width + top * imgWidth);
                }

                width -= 2;

                // If extremely low width or height, ignore
                if (width < 5 || height < 5)
                    continue;

                cloneRect = new Rectangle(left, top, width, height);

                cloneBitmap.Dispose();

                // Load up "amount label" image and draw debug markings for the area
                cloneBitmap = filteredImageClean.Clone(cloneRect, filteredImageClean.PixelFormat);
                g.DrawRectangle(CyanPen, cloneRect);

                var rawPoint = new PointF(cloneRect.X, cloneRect.Y);
                // OCR
                using (var page = _tesseractService.FirstEngine.Process(cloneBitmap, PageSegMode.SingleLine))
                {
                    using (var iterator = page.GetIterator())
                    {
                        iterator.Begin();
                        var rawText = iterator.GetText(PageIteratorLevel.TextLine);
                        rawText = rawText?.Replace(" ", string.Empty);

                        // If no number found, 1 of item
                        if (!int.TryParse(rawText, out var itemCount))
                            itemCount = 1;

                        g.DrawString(rawText, font, Brushes.Cyan, rawPoint);

                        // Find what item the item belongs to
                        var itemLabel = new Rectangle(columns[j].X, rows[i].Top, columns[j].Width, rows[i].Height);
                        g.DrawRectangle(CyanPen, itemLabel);
                        for (var k = 0; k < foundItems.Count; k++)
                        {
                            var item = foundItems[k];

                            if (!item.Bounding.IntersectsWith(itemLabel))
                                continue;

                            item.Count = itemCount;
                            foundItems[k] = item;
                        }
                    }
                }

                // Mark first-pass and second-pass center of checkmark (in case they've been drawn over)
                filteredImage.SetPixel(xCenter, yCenter, Color.Red);
                filteredImage.SetPixel(xCenterNew, yCenterNew, Color.Magenta);

                cloneBitmapColoured.Dispose();
                cloneBitmap.Dispose();
            }
        }

        // Return OCR to any symbols
        _tesseractService.FirstEngine.SetVariable("tessedit_char_whitelist", string.Empty);
    }

    /// <summary>
    /// Process the profile screen to find owned items
    /// </summary>
    /// <param name="fullShot">Image to scan</param>
    internal static void ProcessProfileScreen(Bitmap fullShot)
    {
        var start = Stopwatch.GetTimestamp();

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff", ApplicationConstants.Culture);
        fullShot.Save(Path.Combine(ApplicationConstants.AppPathDebug, $"ProfileImage {timestamp}.png"));
        var foundParts = FindOwnedItems(fullShot, timestamp, in start);
        var parts = CollectionsMarshal.AsSpan(foundParts);
        foreach (var part in parts)
        {
            var partName = part.Name;
            if (!PartNameValid($"{partName} Blueprint"))
                continue;

            // add blueprint to name to check against prime drop table
            var name = Main.DataBase.GetPartName($"{partName} Blueprint", out var proximity, true, out _);

            // also add prime to check if that gives better match. If so, this is a non-prime
            var checkName = Main.DataBase.GetPartName($"{partName} prime Blueprint", out var primeProximity, true, out _);

            Logger.Debug("Checking \"{Part}\", ({Prox})\"{Name}\", +prime ({PrimeProx})\"{CheckName}\"", partName.Trim(), proximity, name, primeProximity, checkName);

            //Decide if item is an actual prime, if so mark as mastered
            if (proximity < 3 && proximity < primeProximity && partName.Length > 6 && name.Contains("Prime"))
            {
                //mark as mastered
                var nameParts = name.Split(["Prime"], 2, StringSplitOptions.None);
                var primeName = nameParts[0] + "Prime";

                if (Main.DataBase.EquipmentData[primeName].ToObject<JObject>().TryGetValue("mastered", out _))
                {
                    Main.DataBase.EquipmentData[primeName]["mastered"] = true;

                    Logger.Debug("Marked \"{PrimeName}\" as mastered", primeName);
                }
                else
                {
                    Logger.Debug("Failed to mark \"{PrimeName}\" as mastered", primeName);
                }
            }
        }

        Main.DataBase.SaveAll(DataTypes.All);
        Main.RunOnUIThread(() =>
        {
            EquipmentWindow.INSTANCE.ReloadItems();
        });

        var end = Stopwatch.GetElapsedTime(start);
        if (end < TimeSpan.FromSeconds(10))
        {
            Main.StatusUpdate($"Completed Profile Scanning({end})", StatusSeverity.None);
        }
        else
        {
            Main.StatusUpdate($"Lower brightness may increase speed({end})", StatusSeverity.Error);
        }
    }

    /// <summary>
    /// Probe pixel color to see if it's white enough for FindOwnedItems
    /// </summary>
    /// <param name="byteArr">Byte Array of image (ARGB). 4 bytes for ARGB, in order BGRA</param>
    /// <param name="width">Width of image</param>
    /// <param name="x">Pixel X coordiante</param>
    /// <param name="y">Pixel Y coordinate</param>
    /// <param name="lowSensitivity">Use lower threshold, mainly for finding black pixels instead</param>
    /// <returns>if pixel is above threshold for "white"</returns>
    private static bool ProbeProfilePixel(ReadOnlySpan<byte> byteArr, int width, int x, int y, bool lowSensitivity)
    {
        var a = byteArr[(x + y * width) * 4 + 3];
        var r = byteArr[(x + y * width) * 4 + 2];
        var g = byteArr[(x + y * width) * 4 + 1];
        var b = byteArr[(x + y * width) * 4];

        if (lowSensitivity)
        {
            return a > 80 && r > 80 && g > 80 && b > 80;
        }

        return a > 240 && r > 200 && g > 200 && b > 200;
    }

    /// <summary>
    /// Get owned items from profile screen
    /// </summary>
    /// <param name="profileImage">Image of profile screen to scan, debug markings will be drawn on this</param>
    /// <param name="timestamp">Time started at, used for file name</param>
    /// <param name="start"></param>
    /// <returns>List of found items</returns>
    private static unsafe List<InventoryItem> FindOwnedItems(Bitmap profileImage, string timestamp, in long start)
    {
        var font = new Font("Arial", 16);
        List<InventoryItem> foundItems = [];
        var profileImageClean = new Bitmap(profileImage);
        var probeInterval = profileImage.Width / 120;
        Logger.Debug("Using probe interval: {Interval}", probeInterval);

        var imgWidth = profileImageClean.Width;

        var lockedBitmapData = profileImageClean.LockBits(
            rect: new Rectangle(0, 0, profileImageClean.Width, profileImageClean.Height),
            flags: ImageLockMode.ReadWrite,
            format: profileImageClean.PixelFormat
        );
        var numBytes = Math.Abs(lockedBitmapData.Stride) * profileImageClean.Height;
        var lockedBitmapBytes = new Span<byte>(lockedBitmapData.Scan0.ToPointer(), numBytes);

        using (var g = Graphics.FromImage(profileImage))
        {
            var nextY = 0;
            var nextYCounter = -1;
            List<SkipZone> skipZones = []; //left edge, right edge, bottom edge
            for (var y = 0; y < profileImageClean.Height - 1; y = nextYCounter == 0 ? nextY : y + 1)
            {
                for (var x = 0; x < imgWidth; x += probeInterval) //probe every few pixels for performance
                {
                    if (!ProbeProfilePixel(lockedBitmapBytes, imgWidth, x, y, false))
                        continue;

                    //find left edge and check that the coloured area is at least as big as probe_interval
                    var leftEdge = -1;
                    var hits = 0;
                    var areaWidth = 0;
                    double hitRatio = 0;
                    for (var tempX = Math.Max(x - probeInterval, 0);
                         tempX < Math.Min(x + probeInterval, imgWidth);
                         tempX++)
                    {
                        areaWidth++;
                        if (ProbeProfilePixel(lockedBitmapBytes, imgWidth, tempX, y, false))
                        {
                            hits++;
                            leftEdge = leftEdge == -1 ? tempX : leftEdge;
                        }
                    }

                    hitRatio = (double)(hits) / areaWidth;
                    if (hitRatio < 0.5) //skip if too low hit ratio
                    {
                        g.DrawLine(OrangePen, x - probeInterval, y, x + probeInterval, y);
                        continue;
                    }

                    //find where the line ends
                    var rightEdge = leftEdge;
                    while (rightEdge + 2 < imgWidth &&
                           (ProbeProfilePixel(lockedBitmapBytes, imgWidth, rightEdge + 1, y, false)
                            || ProbeProfilePixel(lockedBitmapBytes, imgWidth, rightEdge + 2, y, false)))
                    {
                        rightEdge++;
                    }

                    //check that it isn't in an area already thoroughly searched
                    var failed = false;
                    foreach (var skipZone in skipZones)
                    {
                        if (y < skipZone.BottomEdge && ((leftEdge <= skipZone.LeftEdge && rightEdge >= skipZone.LeftEdge) ||
                                                        (leftEdge >= skipZone.LeftEdge && leftEdge <= skipZone.RightEdge) ||
                                                        (rightEdge >= skipZone.LeftEdge && rightEdge <= skipZone.RightEdge)))
                        {
                            g.DrawLine(DarkCyanPen, leftEdge, y, rightEdge, y);
                            x = Math.Max(x, skipZone.RightEdge);
                            failed = true;
                            break;
                        }
                    }

                    if (failed)
                        continue;

                    //find bottom edge and hit ratio of all rows
                    var topEdge = y;
                    var bottomEdge = y;
                    List<double> hitRatios = [1];
                    do
                    {
                        var rightMostHit = 0;
                        var leftMostHit = -1;
                        hits = 0;
                        bottomEdge++;
                        for (var i = leftEdge; i < rightEdge; i++)
                        {
                            if (ProbeProfilePixel(lockedBitmapBytes, imgWidth, i, bottomEdge, false))
                            {
                                hits++;
                                rightMostHit = i;
                                if (leftMostHit == -1)
                                {
                                    leftMostHit = i;
                                }
                            }
                        }

                        hitRatio = hits / (double)(rightEdge - leftEdge);
                        hitRatios.Add(hitRatio);

                        if (hitRatio > 0.2 && rightMostHit + 1 < rightEdge &&
                            rightEdge - leftEdge >
                            100) //make sure the innermost right edge is used (avoid bright part of frame overlapping with edge)
                        {
                            g.DrawLine(RedPen, rightEdge, bottomEdge, rightMostHit, bottomEdge);
                            rightEdge = rightMostHit;
                            bottomEdge = y;
                            hitRatios.Clear();
                            hitRatios.Add(1);
                        }

                        if (hitRatio > 0.2 && leftMostHit > leftEdge &&
                            rightEdge - leftEdge >
                            100) //make sure the innermost left edge is used (avoid bright part of frame overlapping with edge)
                        {
                            g.DrawLine(RedPen, leftEdge, bottomEdge, leftMostHit, bottomEdge);
                            leftEdge = leftMostHit;
                            bottomEdge = y;
                            hitRatios.Clear();
                            hitRatios.Add(1);
                        }
                    } while (bottomEdge + 2 < profileImageClean.Height && hitRatios[^1] > 0.2);

                    hitRatios.RemoveAt(hitRatios.Count - 1);
                    //find if/where it transitions from text (some misses) to no text (basically no misses) then back to text (some misses). This is proof it's an owned item and marks the bottom edge of the text
                    var ratioChanges = 0;
                    var prevMostlyHits = true;
                    var lineBreak = -1;
                    for (var i = 0; i < hitRatios.Count; i++)
                    {
                        if (hitRatios[i] > 0.99 == prevMostlyHits)
                            continue;

                        if (ratioChanges == 1)
                        {
                            lineBreak = i + 1;
                            g.DrawLine(CyanPen, rightEdge, topEdge + lineBreak, leftEdge, topEdge + lineBreak);
                        }

                        prevMostlyHits = !prevMostlyHits;
                        ratioChanges++;
                    }

                    var width = rightEdge - leftEdge;
                    var height = bottomEdge - topEdge;

                    if (ratioChanges != 4 || width < 2.4 * height || width > 4 * height)
                    {
                        g.DrawRectangle(PinkPen, leftEdge, topEdge, width, height);
                        x = Math.Max(rightEdge, x);
                        if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(10))
                        {
                            Main.StatusUpdate("High noise, this might be slow", StatusSeverity.Warning);
                        }

                        continue;
                    }

                    g.DrawRectangle(RedPen, leftEdge, topEdge, width, height);
                    skipZones.Add(new SkipZone(leftEdge, rightEdge, bottomEdge));
                    x = rightEdge;
                    nextY = bottomEdge + 1;
                    nextYCounter = Math.Max(height / 8, 3);

                    height = lineBreak;

                    var cloneRect = new Rectangle(leftEdge, topEdge, width, height);
                    var cloneBitmap = new Bitmap(cloneRect.Width * 3, cloneRect.Height);
                    using (var g2 = Graphics.FromImage(cloneBitmap))
                    {
                        g2.FillRectangle(Brushes.White, 0, 0, cloneBitmap.Width, cloneBitmap.Height);
                    }

                    var offset = 0;
                    var prevHit = false;
                    for (var i = 0; i < cloneRect.Width; i++)
                    {
                        var hitSomething = false;
                        for (var j = 0; j < cloneRect.Height; j++)
                        {
                            if (ProbeProfilePixel(lockedBitmapBytes, imgWidth, cloneRect.X + i, cloneRect.Y + j, true))
                                continue;
                            cloneBitmap.SetPixel(i + offset, j, Color.Black);
                            profileImage.SetPixel(cloneRect.X + i, cloneRect.Y + j, Color.Red);
                            hitSomething = true;
                        }

                        if (!hitSomething && prevHit)
                        {
                            //add empty columns between letters for better OCR accuracy
                            offset += 2;
                            g.FillRectangle(Brushes.Gray, cloneRect.X + i, cloneRect.Y, 1, cloneRect.Height);
                        }

                        prevHit = hitSomething;
                    }

                    //cloneBitmap.Save(ApplicationConstants.AppPath + @"\Debug\ProfileImageClone " + foundItems.Count + " " + timestamp + ".png");

                    //do OCR
                    _tesseractService.FirstEngine.SetVariable("tessedit_char_whitelist",
                        " ABCDEFGHIJKLMNOPQRSTUVWXYZ&");
                    using (var page = _tesseractService.FirstEngine.Process(cloneBitmap, PageSegMode.SingleLine))
                    {
                        using (var iterator = page.GetIterator())
                        {
                            iterator.Begin();
                            var rawText = iterator.GetText(PageIteratorLevel.TextLine);
                            rawText = SpaceRegEx().Replace(rawText, string.Empty);
                            foundItems.Add(new InventoryItem(rawText, cloneRect));

                            g.FillRectangle(Brushes.LightGray, cloneRect.X, cloneRect.Y + cloneRect.Height,
                                cloneRect.Width, cloneRect.Height);
                            g.DrawString(rawText, font, Brushes.DarkBlue,
                                new Point(cloneRect.X, cloneRect.Y + cloneRect.Height));
                        }
                    }

                    _tesseractService.FirstEngine.SetVariable("tessedit_char_whitelist", string.Empty);
                }

                if (nextYCounter >= 0)
                    nextYCounter--;
            }
        }

        profileImageClean.Dispose();
        profileImage.Save(Path.Combine(ApplicationConstants.AppPathDebug, $"ProfileImageBounds {timestamp}.png"));
        return foundItems;
    }

    public static unsafe Bitmap ScaleUpAndFilter(Bitmap image, WFtheme active, out int[] rowHits, out int[] colHits)
    {
        if (image.Height <= ScalingLimit)
        {
            _partialScreenshotExpanded = new Bitmap(image.Width * ScalingLimit / image.Height, ScalingLimit);
            _partialScreenshotExpanded.SetResolution(image.HorizontalResolution, image.VerticalResolution);

            using (var graphics = Graphics.FromImage(_partialScreenshotExpanded))
            {
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                graphics.DrawImage(image, 0, 0, _partialScreenshotExpanded.Width, _partialScreenshotExpanded.Height);
            }

            image = _partialScreenshotExpanded;
        }

        var filtered = new Bitmap(image);

        rowHits = new int[filtered.Height];
        colHits = new int[filtered.Width];

        var lockedBitmapData = filtered.LockBits(
            rect: new Rectangle(0, 0, filtered.Width, filtered.Height),
            flags: ImageLockMode.ReadWrite,
            format: filtered.PixelFormat
        );
        var numBytes = Math.Abs(lockedBitmapData.Stride) * filtered.Height;
        var lockedBitmapBytes = new Span<byte>(lockedBitmapData.Scan0.ToPointer(), numBytes);

        Span<byte> black = stackalloc byte[] { 0, 0, 0, byte.MaxValue };
        Span<byte> colorBuffer;

        const int pixelSize = 4; //ARGB, order in array is BGRA
        for (var i = 0; i < numBytes; i += pixelSize)
        {
            colorBuffer = lockedBitmapBytes.Slice(i, pixelSize);
            var clr = Color.FromArgb(colorBuffer[3], colorBuffer[2], colorBuffer[1], colorBuffer[0]);

            if (_themeDetector.ThemeThresholdFilter(in clr, active))
            {
                black.CopyTo(lockedBitmapBytes.Slice(i, pixelSize));
                var x = (i / pixelSize) % filtered.Width;
                var y = (i / pixelSize - x) / filtered.Width;
                rowHits[y]++;
                colHits[x]++;
            }
            else //White
            {
                lockedBitmapBytes.Slice(i, pixelSize).Fill(byte.MaxValue);
            }
        }

        filtered.UnlockBits(lockedBitmapData);
        return filtered;
    }

    // The parts of text
    // The top bit (upper case and dots/strings, bdfhijklt) > the juicy bit (lower case, acemnorsuvwxz) > the tails (gjpqy)
    // we ignore the "tippy top" because it has a lot of variance, so we just look at the "bottom half of the top"
    private static readonly int[] TextSegments = [2, 4, 16, 21];

    private static unsafe IEnumerable<Bitmap> ExtractPartBoxAutomatically(
        out WFtheme active,
        Bitmap fullScreen)
    {
        var start = Stopwatch.GetTimestamp();
        var beginning = start;

        var lineHeight = (int)(PixelRewardLineHeight / 2 * _window.ScreenScaling);

        var width = _window.Window.Width;
        var height = _window.Window.Height;
        var mostWidth = (int)(PixelRewardWidth * _window.ScreenScaling);
        var mostLeft = (width / 2) - (mostWidth / 2);
        // Most Top = pixleRewardYDisplay - pixleRewardHeight + pixelRewardLineHeight
        //                   (316          -        235        +       44)    *    1.1    =    137
        var mostTop = height / 2 - (int)((PixelRewardYDisplay - PixelRewardHeight + PixelRewardLineHeight) * _window.ScreenScaling);
        var mostBot = height / 2 - (int)((PixelRewardYDisplay - PixelRewardHeight) * _window.ScreenScaling * 0.5);

        var rectangle = new Rectangle(mostLeft, mostTop, mostWidth, mostBot - mostTop);

        Logger.Debug("Extracting part box automatically. scaling={Scaling},rectangle={Rect}", _window.ScreenScaling, rectangle);

        // TODO (rudzen) - replace the existing image manipulation with byte manipulation for performance
        // var bitmapData = fullScreen.LockBits(new Rectangle(0, 0, fullScreen.Width, fullScreen.Height), ImageLockMode.ReadWrite, fullScreen.PixelFormat);
        // var length = Math.Abs(bitmapData.Stride) * fullScreen.Height;
        // var byteSpan = new Span<byte>((void*)bitmapData.Scan0, length);

        Bitmap preFilter;

        try
        {
            Logger.Debug("Fullscreen is {FullScreen}:, trying to clone: {Size} at {Location}", fullScreen.Size, rectangle.Size, rectangle.Location);
            preFilter = fullScreen.Clone(new Rectangle(mostLeft, mostTop, mostWidth, mostBot - mostTop), fullScreen.PixelFormat);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Something went wrong with getting the starting image");
            throw;
        }

        var end = Stopwatch.GetElapsedTime(start);
        Logger.Debug("Grabbed image. time={Time}", end);
        start = Stopwatch.GetTimestamp();

        active = GetThemeWeighted(out var closest, fullScreen);

        end = Stopwatch.GetElapsedTime(start);
        Logger.Debug("Got theme. time={Time}", end);
        start = Stopwatch.GetTimestamp();

        var rows = new int[preFilter.Height];
        // 0 => 50   27 => 77   50 => 100

        for (var y = 0; y < preFilter.Height; y++)
        {
            rows[y] = 0;
            for (var x = 0; x < preFilter.Width; x++)
            {
                var clr = preFilter.GetPixel(x, y);
                if (_themeDetector.ThemeThresholdFilter(in clr, active))
                    rows[y]++;
            }
        }

        end = Stopwatch.GetElapsedTime(start);
        Logger.Debug("Filtered Image. time={Time}", end);
        start = Stopwatch.GetTimestamp();

        var percWeights = new double[51];
        var topWeights = new double[51];
        var midWeights = new double[51];
        var botWeights = new double[51];

        var topLine100 = preFilter.Height - lineHeight;
        var topLine50 = lineHeight / 2;

        var scaling = -1;
        double lowestWeight = 0;
        var uiDebug = new Rectangle(
            x: (topLine100 - topLine50) / 50 + topLine50,
            y: (int)(preFilter.Height / _window.ScreenScaling),
            width: preFilter.Width,
            height: 50
        );

        for (var i = 0; i <= 50; i++)
        {
            var yFromTop = preFilter.Height - (i * (topLine100 - topLine50) / 50 + topLine50);

            var scale = (50 + i);
            var scaleWidth = preFilter.Width * scale / 100;

            var textTop = (int)(_window.ScreenScaling * TextSegments[0] * scale / 100);
            var textTopBot = (int)(_window.ScreenScaling * TextSegments[1] * scale / 100);
            var textBothBot = (int)(_window.ScreenScaling * TextSegments[2] * scale / 100);
            var textTailBot = (int)(_window.ScreenScaling * TextSegments[3] * scale / 100);

            var loc = textTop;
            for (; loc <= textTopBot; loc++)
                topWeights[i] += Math.Abs(scaleWidth * 0.06 - rows[yFromTop + loc]);

            loc++;
            for (; loc < textBothBot; loc++)
            {
                if (rows[yFromTop + loc] < scaleWidth / 15)
                    midWeights[i] += (scaleWidth * 0.26 - rows[yFromTop + loc]) * 5;
                else
                    midWeights[i] += Math.Abs(scaleWidth * 0.24 - rows[yFromTop + loc]);
            }

            loc++;
            for (; loc < textTailBot; loc++)
                botWeights[i] += 10 * Math.Abs(scaleWidth * 0.007 - rows[yFromTop + loc]);

            topWeights[i] /= textTopBot - textTop + 1;
            midWeights[i] /= textBothBot - textTopBot - 2;
            botWeights[i] /= textTailBot - textBothBot - 1;
            percWeights[i] = topWeights[i] + midWeights[i] + botWeights[i];

            if (scaling == -1 || lowestWeight > percWeights[i])
            {
                scaling = scale;
                lowestWeight = percWeights[i];
            }
        }

        end = Stopwatch.GetElapsedTime(start);

        Logger.Debug("Got scaling. time={Time}", end);

        Span<int> topFive = stackalloc int[] { -1, -1, -1, -1, -1 };

        for (var i = 0; i <= 50; i++)
        {
            var match = 4;
            while (match != -1 && topFive[match] != -1 && percWeights[i] > percWeights[topFive[match]])
                match--;

            if (match != -1)
            {
                for (var move = 0; move < match; move++)
                    topFive[move] = topFive[move + 1];
                topFive[match] = i;
            }
        }

        for (var i = 0; i < 5; i++)
        {
            Logger.Debug("RANK {Rank} SCALE: {TopFive}%\t\t{PercW} -- {TopW}, {MidW}, {BotW}",
                5 - i,
                topFive[i] + 50,
                percWeights[topFive[i]].ToString("F2", ApplicationConstants.Culture),
                topWeights[topFive[i]].ToString("F2", ApplicationConstants.Culture),
                midWeights[topFive[i]].ToString("F2", ApplicationConstants.Culture),
                botWeights[topFive[i]].ToString("F2", ApplicationConstants.Culture));
        }

        using (var g = Graphics.FromImage(fullScreen))
        {
            g.DrawRectangle(Pens.Red, rectangle);
            g.DrawRectangle(Pens.Chartreuse, uiDebug);
        }

        fullScreen.Save(Path.Combine(ApplicationConstants.AppPathDebug, $"BorderScreenshot {_timestamp}.png"));
        preFilter.Save(Path.Combine(ApplicationConstants.AppPathDebug, $"FullPartArea {_timestamp}.png"));

        //scaling was sometimes going to 50 despite being set to 100, so taking the value from above that seems to be accurate.
        scaling = topFive[4] + 50;
        scaling /= 100;

        var highScaling = scaling < 1.0 ? scaling + 0.01 : scaling;
        var lowScaling = scaling > 0.5 ? scaling - 0.01 : scaling;

        var cropWidth = (int)(PixelRewardWidth * _window.ScreenScaling * highScaling);
        var cropLeft = (preFilter.Width / 2) - (cropWidth / 2);
        var cropTop = height / 2 - (int)((PixelRewardYDisplay - PixelRewardHeight + PixelRewardLineHeight) *
                                         _window.ScreenScaling * highScaling);
        var cropBot = height / 2 -
                      (int)((PixelRewardYDisplay - PixelRewardHeight) * _window.ScreenScaling * lowScaling);
        var cropHei = cropBot - cropTop;
        cropTop -= mostTop;
        try
        {
            _partialScreenshot?.Dispose();
            // var rect = new Rectangle(
            //     x: cropLeft,
            //     y: cropTop,
            //     width: cropWidth,
            //     height: cropHei
            // );
            var rect = new Rectangle(
                x: 0,
                y: 0,
                width: preFilter.Width,
                height: preFilter.Height
            );
            _partialScreenshot = preFilter.Clone(rect, PixelFormat.DontCare);
            if (_partialScreenshot.Height == 0 || _partialScreenshot.Width == 0)
                throw new ArithmeticException("New image was null");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Something went wrong while trying to copy the right part of the screen into partial screenshot");
            throw;
        }

        preFilter.Dispose();

        end = Stopwatch.GetElapsedTime(beginning);
        Logger.Debug("Finished function. time={End}", end);
        var file = Path.Combine(ApplicationConstants.AppPathDebug, $"PartialScreenshot{_timestamp}.png");
        _partialScreenshot.Save(file);

        UiScaling = scaling;

        return FilterAndSeparatePartsFromPartBox(_partialScreenshot, active);
    }

    /// <summary>
    /// Filters and separates the parts from the part box
    /// SIMD
    /// </summary>
    /// <param name="partBox">The input image which needs to be peeled</param>
    /// <param name="active">The current active (detected) theme</param>
    /// <returns>List of bitmaps which contains the reward parts</returns>
    /// <exception cref="Exception"></exception>
    [SkipLocalsInit]
    private static unsafe List<Bitmap> FilterAndSeparatePartsFromPartBox(Bitmap partBox, WFtheme active)
    {
        var width = partBox.Width;
        var height = partBox.Height;
        var counts = new int[height];
        var filtered = new Bitmap(width, height);

        const int pixelSize = 4; //ARGB, order in array is BGRA
        var lockedFilteredData = filtered.LockBits(
            rect: new Rectangle(0, 0, filtered.Width, filtered.Height),
            flags: ImageLockMode.WriteOnly,
            format: filtered.PixelFormat
        );
        var filteredHeight = Math.Abs(lockedFilteredData.Stride) * filtered.Height;
        //Format is ARGB, in order BGRA
        var filteredBytes = new Span<byte>((void*)lockedFilteredData.Scan0, filteredHeight);

        var lockedPartBoxData = partBox.LockBits(
            rect: new Rectangle(0, 0, partBox.Width, partBox.Height),
            flags: ImageLockMode.ReadOnly,
            format: partBox.PixelFormat
        );
        //Format is ARGB, in order BGRA
        var partBoxBytes = new Span<byte>((void*)lockedFilteredData.Scan0, filteredHeight);

        // pixel buffer
        Span<byte> buffer = stackalloc byte[pixelSize];

        // colors
        Span<byte> black = stackalloc byte[pixelSize] { 0, 0, 0, byte.MaxValue };

        double weight = 0;
        double totalEven = 0;
        double totalOdd = 0;

        // run through the image and filter out the parts that are not close to the theme color
        // order of loops doesn't matter since we use strides
        for (var x = 0; x < width; x++)
        {
            var count = 0;
            for (var y = 0; y < height; y++)
            {
                // get index based on stride
                var i = (y * width + x) * pixelSize;

                // get color from buffer
                var clr = Color.FromArgb(BinaryPrimitives.ReverseEndianness(MemoryMarshal.Read<int>(partBoxBytes.Slice(i, pixelSize))));

                // if the color is within the theme threshold, create a black pixel,
                // otherwise create a white pixel
                if (_themeDetector.ThemeThresholdFilter(in clr, active))
                {
                    black.CopyTo(filteredBytes.Slice(i, pixelSize));
                    counts[y]++;
                    count++;
                }
                else
                {
                    // pixelSize x byte.MaxValue = white
                    filteredBytes.Slice(i, pixelSize).Fill(byte.MaxValue);
                }
            }

            count = Math.Min(count, partBox.Height / 3);
            var sinVal = Math.Cos(8 * x * Math.PI / partBox.Width);
            sinVal = sinVal * sinVal * sinVal;
            weight += sinVal * count;

            if (sinVal < 0)
                totalEven -= sinVal * count;
            else if (sinVal > 0)
                totalOdd += sinVal * count;
        }

        // unlock the image bytes
        filtered.UnlockBits(lockedFilteredData);
        partBox.UnlockBits(lockedPartBoxData);

        // Rarely, the selection box on certain themes can get included in the detected reward area.
        // Therefore, we check the bottom 10% of the image for this potential issue
        var finalHeight = GetFinalPartBoxHeight(height, counts);

        // if height was changed, create a new filtered image
        if (finalHeight != height)
        {
            Logger.Debug("Final selection border in image, cropping height. to={Final},from={Height}", finalHeight, height);
            var tmp = filtered.Clone(new Rectangle(0, 0, width, finalHeight), filtered.PixelFormat);
            filtered.Dispose();
            filtered = tmp;
        }

        if (totalEven == 0 || totalOdd == 0)
        {
            // TODO (rudzen) : move this bullcrap UI interaction the hell away from this deep within the code
            Main.RunOnUIThread(() =>
            {
                Main.StatusUpdate(
                    "Unable to detect reward from selection screen\nScanning inventory? Hold down snap-it modifier",
                    StatusSeverity.Error);
            });
            ProcessingActive.GetAndSet(false);

            // since we are about to freak out
            filtered.Dispose();
            // TODO (rudzen) : throw a custom exception
            throw new Exception("Unable to find any parts");
        }

        var total = totalEven + totalOdd;
        Logger.Debug("EVEN DISTRIBUTION: {Dist}%", (totalEven / total * 100).ToString("F2", ApplicationConstants.Culture));
        Logger.Debug("ODD DISTRIBUTION: {Dist}%", (totalOdd / total * 100).ToString("F2", ApplicationConstants.Culture));

        var boxWidth = partBox.Width / 4;
        var boxHeight = filtered.Height;
        var destRegion = new Rectangle(0, 0, boxWidth, boxHeight);

        var currLeft = 0;
        var playerCount = 4;

        if (totalOdd > totalEven)
        {
            currLeft = boxWidth / 2;
            playerCount = 3;
        }

        var images = new List<Bitmap>(playerCount);

        for (var i = 0; i < playerCount; i++)
        {
            var srcRegion = new Rectangle(currLeft + i * boxWidth, 0, boxWidth, boxHeight);
            var newBox = new Bitmap(boxWidth, boxHeight);
            using (var grD = Graphics.FromImage(newBox))
                grD.DrawImage(filtered, destRegion, srcRegion, GraphicsUnit.Pixel);
            newBox.Save(Path.Combine(ApplicationConstants.AppPathDebug, $"PartBox({i}) {_timestamp}.png"));
            images.Add(newBox);
        }

        filtered.Dispose();

        return images;
    }

    /// <summary>
    /// Aligns height because certain themes can have a selection box included in the detected reward area.
    /// </summary>
    /// <param name="height">The original height</param>
    /// <param name="counts">The pixel counts</param>
    /// <returns>The final height</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetFinalPartBoxHeight(int height, ReadOnlySpan<int> counts)
    {
        var finalHeight = height;
        for (var y = height - 1; y > height * 0.9; --y)
        {
            // Assumed to be this issue if both the following criteria are met:
            // 1. A lot more black pixels than the next line up, going with 5x for the moment. The issue is almost entirely on a single line in the cases I've seen so far
            // 2. The problematic line should have a meaningful amount of black pixels. At least twice the height should be pretty good. (We don't yet know the number of players, so can't directly base it on width)
            if (counts[y] > 5 * counts[y - 1] && counts[y] > height * 2)
            {
                finalHeight = y;
                Logger.Debug("Possible selection border in image, cropping. to={Y},from={Height}", y, height);
            }
        }

        return finalHeight;
    }

    private static string GetTextFromImage(Bitmap image, TesseractEngine engine)
    {
        var ret = string.Empty;
        using (var page = engine.Process(image))
        {
            ret = page.GetText();
        }

        var s = ret.AsSpan().Trim();

        if (s.IsEmpty)
            return ret;

        if (s.Length != ret.Length)
            ret = s.ToString();

        return Re.Replace(ret, string.Empty).Trim();
    }

    internal static async Task<Bitmap> CaptureScreenshot()
    {
        await _window.UpdateWindow();

        var screenshot = GetScreenshotService();
        var screenshots = await screenshot.CaptureScreenshot();
        var image = screenshots[0];
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff", ApplicationConstants.Culture);
        var fileName = Path.Combine(ApplicationConstants.AppPathDebug, $"FullScreenShot {date}.png");
        image.Save(fileName);
        return image;
    }

    private static IScreenshotService GetScreenshotService()
    {
        // W8.1 and lower
        if (_windowsScreenshot is null)
            return _gdiScreenshot;

        switch (_settings.HdrSupport)
        {
            case HdrSupportEnum.On:
                Logger.Debug("HDR capturing set, using window based capturing");
                return _windowsScreenshot;
                break;
            case HdrSupportEnum.Off:
                Logger.Debug("HDR capturing *not* set, Using GDI based capturing (SDR)");
                return _gdiScreenshot;
                break;
            case HdrSupportEnum.Auto:
                var isHdr = _hdrDetector.IsHdr();
                Logger.Debug("Auto selecting capturing. HDR={Hdr}", isHdr);
                return isHdr ? _windowsScreenshot : _gdiScreenshot;
                break;
            default:
                throw new NotImplementedException($"HDR support option '{_settings.HdrSupport}' does not have a corresponding screenshot service");
        }
    }

    internal static async Task SnapScreenshot()
    {
        var image = await CaptureScreenshot();
        Application.Current.Dispatcher.InvokeIfRequired(() =>
        {
            Main.SnapItOverlayWindow.Populate(image);
            Main.SnapItOverlayWindow.Left = _window.Window.Left / _window.DpiScaling;
            Main.SnapItOverlayWindow.Top = _window.Window.Top / _window.DpiScaling;
            Main.SnapItOverlayWindow.Width = _window.Window.Width / _window.DpiScaling;
            Main.SnapItOverlayWindow.Height = _window.Window.Height / _window.DpiScaling;
            Main.SnapItOverlayWindow.Topmost = true;
            Main.SnapItOverlayWindow.Focusable = true;
            Main.SnapItOverlayWindow.Show();
            Main.SnapItOverlayWindow.Focus();
        });
    }

    public static async Task UpdateEngineAsync()
    {
        _tesseractService.ReloadEngines();
    }

    [GeneratedRegex("[^a-z가-힣]", RegexOptions.IgnoreCase | RegexOptions.Compiled, "da-DK")]
    private static partial Regex WordTrimRegEx();

    [GeneratedRegex(@"\s")]
    private static partial Regex SpaceRegEx();
}

public struct InventoryItem(string itemName, Rectangle boundingbox, bool showWarning = false)
{
    public static readonly IComparer<InventoryItem> Comparer = new InventoryItemComparer();

    public string Name { get; set; } = itemName;
    public Rectangle Bounding { get; set; } = boundingbox;
    public int Count { get; set; } = 1; //if no label found, assume 1
    public bool Warning { get; set; } = showWarning;
}

/// <summary>
/// Sort order for component words to appear in.
/// If large height difference, sort vertically.
/// If small height difference, sort horizontally
/// </summary>
public sealed class InventoryItemComparer : IComparer<InventoryItem>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Compare(InventoryItem i1, InventoryItem i2)
    {
        var topDiff = i1.Bounding.Top - i2.Bounding.Top;
        return Math.Abs(topDiff) > i1.Bounding.Height / 8
            ? topDiff
            : i1.Bounding.Left - i2.Bounding.Left;
    }
}
