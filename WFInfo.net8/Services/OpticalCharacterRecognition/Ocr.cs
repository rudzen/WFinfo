using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Akka.Util;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Serilog;
using Tesseract;
using WFInfo.Domain;
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

    private static readonly Regex RE = WordTrimRegEx();

    // Pixel measurements for reward screen @ 1920 x 1080 with 100% scale https://docs.google.com/drawings/d/1Qgs7FU2w1qzezMK-G1u9gMTsQZnDKYTEU36UPakNRJQ/edit
    private const int PixelRewardWidth = 968;
    private const int PixelRewardHeight = 235;
    private const int PixelRewardYDisplay = 316;
    private const int PixelRewardLineHeight = 48;

    private const int SCALING_LIMIT = 100;

    private static readonly Pen OrangePen = new(Brushes.Orange);
    private static readonly Pen PinkPen = new(Brushes.Pink);
    private static readonly Pen DarkCyanPen = new(Brushes.DarkCyan);
    private static readonly Pen RedPen = new(Brushes.Red);
    private static readonly Pen CyanPen = new(Brushes.Cyan);

    public static AtomicBoolean ProcessingActive { get; set; } = new(false);

    private static Bitmap bigScreenshot;
    private static Bitmap? partialScreenshot;
    private static Bitmap partialScreenshotExpanded;

    private static string[]? firstChecks;
    private static int[]? firstProximity = [-1, -1, -1, -1];
    private static string timestamp;

    private static string clipboard;

    #endregion

    private static ITesseractService _tesseractService;
    private static ISoundPlayer _soundPlayer;
    private static ApplicationSettings _settings;
    private static IWindowInfoService _window;
    private static IHDRDetectorService _hdrDetector;
    private static IThemeDetector ThemeDetector;
    private static ISnapZoneDivider SnapZoneDivider;
    private static IMediator _mediator;

    private static Overlay[] _overlays;

    private static IScreenshotService _gdiScreenshot;
    private static IScreenshotService? _windowsScreenshot;

    public static void Init(IServiceProvider sp, Overlay[] overlays)
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
            overlays: overlays,
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
        Overlay[] overlays,
        IScreenshotService gdiScreenshot,
        IScreenshotService? windowsScreenshot = null)
    {
        Directory.CreateDirectory(ApplicationConstants.AppPathDebug);
        _tesseractService = tesseractService;
        _tesseractService.Init();
        _soundPlayer = soundPlayer;
        _settings = settings;
        ThemeDetector = themeDetector;
        SnapZoneDivider = snapZoneDivider;
        _window = window;
        _hdrDetector = hdrDetector;
        _mediator = mediator;
        _overlays = overlays;

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
        timestamp = time.ToString("yyyy-MM-dd HH-mm-ssff", ApplicationConstants.Culture);
        var start = Stopwatch.GetTimestamp();

        var parts = new List<Bitmap>();

        bigScreenshot = file ?? await CaptureScreenshot();
        try
        {
            parts.AddRange(ExtractPartBoxAutomatically(out var _, bigScreenshot));
        }
        catch (Exception e)
        {
            ProcessingActive.GetAndSet(false);
            Logger.Error(e, "Error while extracting part boxes");
            return;
        }

        firstChecks = new string[parts.Count];
        var tasks = new Task[parts.Count];
        for (var i = 0; i < parts.Count; i++)
        {
            var tempI = i;
            tasks[i] = Task.Factory.StartNew(() =>
            {
                firstChecks[tempI] = GetTextFromImage(parts[tempI], _tesseractService.Engines[tempI]);
            });
        }

        Task.WaitAll(tasks);

        // Remove any empty (or suspiciously short) items from the array
        firstChecks = firstChecks
                      .Where(s => !string.IsNullOrEmpty(s) && s.Replace(" ", string.Empty).Length > 6)
                      .ToArray();
        if (firstChecks == null || firstChecks.Length == 0 || CheckIfError())
        {
            ProcessingActive.GetAndSet(false);
            var end = Stopwatch.GetElapsedTime(start);
            Logger.Debug(
                "----  Partial Processing Time, couldn't find rewards {Time}  ------------------------------------------------------------------------------------------"[..108],
                end);
            await _mediator.Publish(new UpdateStatus("Couldn't find any rewards to display", StatusSeverity.Warning));
            if (firstChecks == null)
            {
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

        if (firstChecks.Length > 0)
        {
            NumberOfRewardsDisplayed = firstChecks.Length;
            clipboard = string.Empty;
            var width = (int)(PixelRewardWidth * _window.ScreenScaling * UiScaling) + 10;
            var startX = _window.Center.X - width / 2 + (int)(width * 0.004);

            if (firstChecks.Length % 2 == 1)
                startX += width / 8;

            if (firstChecks.Length <= 2)
                startX += 2 * (width / 8);

            var overWid = (int)(width / (4.1 * _window.DpiScaling));
            var startY = (int)(_window.Center.Y / _window.DpiScaling - 20 * _window.ScreenScaling * UiScaling);
            var partNumber = 0;
            var hideRewardInfo = false;
            for (var i = 0; i < firstChecks.Length; i++)
            {
                var part = firstChecks[i];

                #region found a part

                var correctName = Main.DataBase.GetPartName(part, out firstProximity[i], false, out _);
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
                {
                    primeSetPlat = (string)primeSet["plat"];
                }

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

                if (duc > 0 && !mastered && int.Parse(partsOwned, ApplicationConstants.Culture) < int.Parse(partsCount, ApplicationConstants.Culture))
                {
                    unownedItems.Add(i);
                }

                #endregion

                RewardScreenClipboard(
                    platinum: in platinum,
                    correctName: correctName,
                    plat: plat,
                    primeSetPlat: primeSetPlat,
                    ducats: ducats,
                    vaulted: vaulted,
                    partNumber: partNumber
                );

                #region display part

                Main.RunOnUIThread(() =>
                {
                    Overlay.RewardsDisplaying = true;

                    if (_settings.IsOverlaySelected)
                    {
                        var overlay = _overlays[partNumber];
                        overlay.LoadTextData(correctName, plat, primeSetPlat, ducats, volume, vaulted,
                            mastered, $"{partsOwned} / {partsCount}", string.Empty, hideRewardInfo, false);
                        overlay.Resize(overWid);
                        overlay.Display(
                            x: (int)((startX + width / 4 * partNumber + _settings.OverlayXOffsetValue) / _window.DpiScaling),
                            y: startY + (int)(_settings.OverlayYOffsetValue / _window.DpiScaling),
                            wait: _settings.Delay
                        );
                    }
                    else if (!_settings.IsLightSelected)
                    {
                        // TODO (rudzen) : Add event
                        Main.RewardWindow.loadTextData(correctName, plat, primeSetPlat, ducats, volume, vaulted, mastered,
                            $"{partsOwned} / {partsCount}", partNumber, true, hideRewardInfo);
                    }
                    //else
                    //Main.window.loadTextData(correctName, plat, ducats, volume, vaulted, $"{partsOwned} / {partsCount}", partNumber, false, hideRewardInfo);

                    if (_settings.Clipboard && !string.IsNullOrEmpty(clipboard))
                        Clipboard.SetText(clipboard);
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
                Main.RunOnUIThread(() =>
                {
                    foreach (var item in unownedItems)
                    {
                        _overlays[item].BestOwnedChoice();
                    }

                    _overlays[bestDucatItem].BestDucatChoice();
                    _overlays[bestPlatItem].BestPlatChoice();
                });
            }

            Logger.Debug(("----  Total Processing Time " + end + " ------------------------------------------------------------------------------------------")[..108]);
        }

        #endregion

        // light mode doesn't have any visual confirmation that the ocr has finished, thus we use a sound to indicate this.
        if (_settings.IsLightSelected && clipboard.Length > 3)
            _soundPlayer.Play();

        var directory = new DirectoryInfo(ApplicationConstants.AppPathDebug);
        var fileCreationTimeThreshold = DateTime.Now.AddHours(-1 * _settings.ImageRetentionTime);
        var filesToDelete = directory
                            .GetFiles()
                            .Where(f => f.CreationTime < fileCreationTimeThreshold);

        foreach (var fileToDelete in filesToDelete)
            fileToDelete.Delete();

        if (partialScreenshot is not null)
        {
            var path = Path.Combine(ApplicationConstants.AppPathDebug, $"PartBox {timestamp}.png");
            partialScreenshot.Save(path);
            partialScreenshot.Dispose();
            partialScreenshot = null;
        }

        ProcessingActive.GetAndSet(false);
    }

    #region clipboard

    private static void RewardScreenClipboard(
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
            if (!string.IsNullOrEmpty(clipboard))
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

        if (partNumber == firstChecks.Length - 1 && sb.Length > 0)
            sb.Append(_settings.ClipboardTemplate);

        if (sb.Length > 0)
        {
            var clip = sb.ToString();
            clipboard = clip;
            Logger.Debug("Clipboard msg: {Clip}", clip);
        }
    }

    #endregion clipboard

    private static bool CheckIfError()
    {
        if (firstChecks == null || firstProximity == null)
            return false;

        const double errorDetectionThreshold = 0.25;

        var max = Math.Min(firstChecks.Length, firstProximity.Length);
        for (var i = 0; i < max; i++)
            if (firstProximity[i] > errorDetectionThreshold * firstChecks[i].Length)
                return true;

        return false;
    }

    public static WFtheme GetThemeWeighted(out double closestThresh, Bitmap? image = null)
    {
        image ??= CaptureScreenshot().GetAwaiter().GetResult();
        var theme = ThemeDetector.GetThemeWeighted(out closestThresh, image);
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
    internal static async Task ProcessSnapIt(Bitmap snapItImage, Bitmap fullShot, Point snapItOrigin)
    {
        var watch = new Stopwatch();
        watch.Start();
        var start = watch.ElapsedMilliseconds;

        var now = DateTime.UtcNow;
        var timestamp = now.ToString("yyyy-MM-dd HH-mm-ssff", ApplicationConstants.Culture);
        var theme = GetThemeWeighted(out _, fullShot);
        snapItImage.Save(Path.Combine(ApplicationConstants.AppPathDebug, $"SnapItImage {timestamp}.png"));
        var snapItImageFiltered = ScaleUpAndFilter(snapItImage, theme, out var rowHits, out var colHits);
        snapItImageFiltered.Save(Path.Combine(ApplicationConstants.AppPathDebug, $"SnapItImageFiltered {timestamp}.png"));
        var foundParts = FindAllParts(snapItImageFiltered, snapItImage, rowHits, colHits);
        var end = watch.ElapsedMilliseconds;
        snapItImage.Dispose();
        snapItImageFiltered.Dispose();

        await _mediator.Publish(new UpdateStatus("Completed snapit Processing(" + (end - start) + "ms)"));
        var csv = string.Empty;
        if (!File.Exists(Path.Combine(ApplicationConstants.AppPath, "export " + now.ToString("yyyy-MM-dd", ApplicationConstants.Culture) + ".csv")) && _settings.SnapitExport)
            csv += "ItemName,Plat,Ducats,Volume,Vaulted,Owned,partsDetected" +
                   now.ToString("yyyy-MM-dd", ApplicationConstants.Culture) + Environment.NewLine;
        var resultCount = foundParts.Count;
        for (var i = 0; i < foundParts.Count; i++)
        {
            var part = foundParts[i];
            if (!PartNameValid(part.Name))
            {
                foundParts.RemoveAt(
                    i--); //remove invalid part from list to not clog VerifyCount. Decrement to not skip any entries
                resultCount--;
                continue;
            }

            Logger.Debug("Processing part {Part} out of {Count}", i, foundParts.Count);
            var name = Main.DataBase.GetPartName(part.Name, out var levenDist, false, out var multipleLowest);
            var primeSetName = Data.GetSetName(name);
            if (levenDist > Math.Min(part.Name.Length, name.Length) / 3 || multipleLowest)
            {
                //show warning triangle if the result is of questionable accuracy. The limit is basically arbitrary
                part.Warning = true;
            }

            var doWarn = part.Warning;
            part.Name = name;
            foundParts[i] = part;
            var job = Main.DataBase.MarketData.GetValue(name).ToObject<JObject>();
            var primeSet = (JObject)Main.DataBase.MarketData.GetValue(primeSetName);
            var plat = job["plat"].ToObject<string>();

            string primeSetPlat = null;
            if (primeSet != null)
                primeSetPlat = (string)primeSet["plat"];

            var ducats = job["ducats"].ToObject<string>();
            var volume = job["volume"].ToObject<string>();
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

        if (_settings.DoSnapItCount && resultCount > 0)
            Main.RunOnUIThread(() =>
            {
                VerifyCount.ShowVerifyCount(foundParts);
            });

        if (Main.SnapItOverlayWindow.tempImage != null)
            Main.SnapItOverlayWindow.tempImage.Dispose();
        end = watch.ElapsedMilliseconds;
        if (resultCount == 0)
        {
            await _mediator.Publish(new UpdateStatus("Couldn't find any items to display (took " + (end - start) + "ms) ", StatusSeverity.Error));
            Main.RunOnUIThread(() =>
            {
                Main.SpawnErrorPopup(DateTime.UtcNow);
            });
        }
        else
        {
            await _mediator.Publish(new UpdateStatus("Completed snapit Displaying(" + (end - start) + "ms)"));
        }

        watch.Stop();
        Logger.Debug("Snap-it finished, displayed reward count:{Count}, time: {Time}ms", resultCount, end - start);
        if (_settings.SnapitExport)
        {
            var file = Path.Combine(ApplicationConstants.AppPath,
                $"export {DateTime.UtcNow.ToString("yyyy-MM-dd", ApplicationConstants.Culture)}.csv");
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

            currentWord = RE.Replace(currentWord, string.Empty).Trim();

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
        Bitmap filteredImage, Bitmap unfilteredImage, int[] rowHits,
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
        var green = new SolidBrush(Color.FromArgb(100, 255, 165, 0));
        var greenp = new Pen(green);
        var pinkP = new Pen(Brushes.Pink);
        var font = new Font("Arial", 16);
        List<SnapZone> zones;
        int snapThreads;
        if (_settings.SnapMultiThreaded)
        {
            zones = SnapZoneDivider.DivideSnapZones(filteredImage, filteredImageClean, rowHits, colHits);
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
                    List<TextWithBounds> currentResult =
                        GetTextWithBoundsFromImage(_tesseractService.Engines[tempI], zones[j].Bitmap, zones[j].Rectangle.X,
                            zones[j].Rectangle.Y);
                    taskResults.AddRange(currentResult);
                }

                return taskResults;
            });
        }

        Task.WaitAll(snapTasks);

        for (var threadNum = 0; threadNum < snapThreads; threadNum++)
        {
            foreach (var (currentWord, bounds) in snapTasks[threadNum].Result)
            {
                //word is valid start comparing to others
                var verticalPad = bounds.Height / 2;
                var horizontalPad = (int)(bounds.Height * _settings.SnapItHorizontalNameMargin);
                var paddedBounds = new Rectangle(bounds.X - horizontalPad, bounds.Y - verticalPad,
                    bounds.Width + horizontalPad * 2, bounds.Height + verticalPad * 2);
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
            //Sort order for component words to appear in. If large height difference, sort vertically. If small height difference, sort horizontally
            itemGroup.Items.Sort((i1, i2) =>
            {
                return Math.Abs(i1.Bounding.Top - i2.Bounding.Top) > i1.Bounding.Height / 8
                    ? i1.Bounding.Top - i2.Bounding.Top
                    : i1.Bounding.Left - i2.Bounding.Left;
            });

            //Combine into item name
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
    private static void GetItemCounts(
        Bitmap filteredImage,
        Bitmap filteredImageClean,
        Bitmap unfilteredImage,
        List<InventoryItem> foundItems,
        Font font)
    {
        Logger.Debug("Starting Item Counting");
        using (var g = Graphics.FromImage(filteredImage))
        {
            //sort for easier processing in loop below
            var foundItemsBottom = foundItems.OrderBy(o => o.Bounding.Bottom).ToList();
            //filter out bad parts for more accurate grid
            var itemRemoved = false;
            for (var i = 0; i < foundItemsBottom.Count; i += (itemRemoved ? 0 : 1))
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

            for (var i = 0; i < foundItemsBottom.Count; i++)
            {
                var currRow = new Rectangle(0, foundItemsBottom[i].Bounding.Y, 10000,
                    foundItemsBottom[i].Bounding.Height);
                var currColumn = new Rectangle(foundItemsLeft[i].Bounding.X, 0, foundItemsLeft[i].Bounding.Width,
                    10000);

                //find or improve latest ColumnsRight
                if (rows.Count == 0 || !currRow.IntersectsWith(rows[^1]))
                {
                    rows.Add(currRow);
                }
                else
                {
                    if (currRow.Bottom < rows[^1].Bottom)
                    {
                        rows[^1] = new Rectangle(0, rows[^1].Y, 10000, currRow.Bottom - rows[^1].Top);
                    }

                    if (rows.Count != 1 && currColumn.Top > columns[^1].Top)
                    {
                        rows[^1] = new Rectangle(0, currRow.Y, 10000, rows[^1].Bottom - currRow.Top);
                    }
                }

                //find or improve latest ColumnsRight
                if (columns.Count == 0 || !currColumn.IntersectsWith(columns[^1]))
                {
                    columns.Add(currColumn);
                }
                else
                {
                    if (currColumn.Right < columns[^1].Right)
                    {
                        columns[^1] = new Rectangle(columns[^1].X, 0,
                            currColumn.Right - columns[^1].X, 10000);
                    }

                    if (columns.Count != 1 && currColumn.Left > columns[^1].Left)
                    {
                        columns[^1] =
                            new Rectangle(currColumn.X, 0, columns[^1].Right - currColumn.X, 10000);
                    }
                }
            }

            //draw debug markings for grid system
            foreach (var col in columns)
            {
                g.DrawLine(DarkCyanPen, col.Right, 0, col.Right, 10000);
                g.DrawLine(DarkCyanPen, col.X, 0, col.X, 10000);
            }

            foreach (var bottom in rows.Select(x => x.Bottom))
                g.DrawLine(DarkCyanPen, 0, bottom, 10000, bottom);

            //set OCR to numbers only
            _tesseractService.FirstEngine.SetVariable("tessedit_char_whitelist", "0123456789");


            var widthMultiplier = (_settings.DoCustomNumberBoxWidth ? _settings.SnapItNumberBoxWidth : 0.4);
            //Process grid system
            for (var i = 0; i < rows.Count; i++)
            {
                for (var j = 0; j < columns.Count; j++)
                {
                    //edges of current area to scan
                    var Left = (j == 0 ? 0 : (columns[j - 1].Right + columns[j].X) / 2);
                    var Top = (i == 0 ? 0 : rows[i - 1].Bottom);
                    var Width = Math.Min((int)((columns[j].Right - Left) * widthMultiplier),
                        filteredImage.Size.Width - Left);
                    var Height = Math.Min((rows[i].Bottom - Top) / 3, filteredImage.Size.Height - Top);

                    var cloneRect = new Rectangle(Left, Top, Width, Height);
                    g.DrawRectangle(CyanPen, cloneRect);
                    var cloneBitmap = filteredImageClean.Clone(cloneRect, filteredImageClean.PixelFormat);
                    var cloneBitmapColoured = unfilteredImage.Clone(cloneRect, filteredImageClean.PixelFormat);


                    //get cloneBitmap as array for fast access
                    var imgWidth = cloneBitmap.Width;
                    var imgHeight = cloneBitmap.Height;
                    var lockedBitmapData = cloneBitmap.LockBits(new Rectangle(0, 0, imgWidth, cloneBitmap.Height), ImageLockMode.WriteOnly, cloneBitmap.PixelFormat);
                    var numBytes = Math.Abs(lockedBitmapData.Stride) * lockedBitmapData.Height;
                    var lockedBitmapBytes = new byte[numBytes]; //Format is ARGB, in order BGRA
                    Marshal.Copy(lockedBitmapData.Scan0, lockedBitmapBytes, 0, numBytes);
                    cloneBitmap.UnlockBits(lockedBitmapData);

                    //find "center of mass" for black pixels in the area
                    var x = 0;
                    var y = 0;
                    int index;
                    var xCenter = 0;
                    var yCenter = 0;
                    var sumBlack = 1;
                    for (index = 0; index < numBytes; index += 4)
                    {
                        if (lockedBitmapBytes[index] == 0 && lockedBitmapBytes[index + 1] == 0 &&
                            lockedBitmapBytes[index + 2] == 0 && lockedBitmapBytes[index + 3] == 255)
                        {
                            y = (index / 4) / imgWidth;
                            x = (index / 4) % imgWidth;
                            yCenter += y;
                            xCenter += x;
                            sumBlack++;
                        }
                    }

                    xCenter /= sumBlack;
                    yCenter /= sumBlack;


                    if (sumBlack < Height) continue; //not enough black = ignore and move on

                    //mark first-pass center
                    filteredImage.SetPixel(Left + xCenter, Top + yCenter, Color.Red);


                    var minToEdge = Math.Min(Math.Min(xCenter, imgWidth - xCenter),
                        Math.Min(yCenter, imgHeight - yCenter)); //get the distance to closest edge of image
                    //we're expected to be within the checkmark + circle, find closest black pixel to find some part of it to start at
                    for (var dist = 0; dist < minToEdge; dist++)
                    {
                        x = xCenter + dist;
                        y = yCenter;
                        index = 4 * (x + y * imgWidth);
                        if (lockedBitmapBytes[index] == 0 && lockedBitmapBytes[index + 1] == 0 &&
                            lockedBitmapBytes[index + 2] == 0 && lockedBitmapBytes[index + 3] == 255)
                        {
                            break;
                        }

                        x = xCenter - dist;
                        y = yCenter;
                        index = 4 * (x + y * imgWidth);
                        if (lockedBitmapBytes[index] == 0 && lockedBitmapBytes[index + 1] == 0 &&
                            lockedBitmapBytes[index + 2] == 0 && lockedBitmapBytes[index + 3] == 255)
                        {
                            break;
                        }

                        x = xCenter;
                        y = yCenter + dist;
                        index = 4 * (x + y * imgWidth);
                        if (lockedBitmapBytes[index] == 0 && lockedBitmapBytes[index + 1] == 0 &&
                            lockedBitmapBytes[index + 2] == 0 && lockedBitmapBytes[index + 3] == 255)
                        {
                            break;
                        }

                        x = xCenter;
                        y = yCenter - dist;
                        index = 4 * (x + y * imgWidth);
                        if (lockedBitmapBytes[index] == 0 && lockedBitmapBytes[index + 1] == 0 &&
                            lockedBitmapBytes[index + 2] == 0 && lockedBitmapBytes[index + 3] == 255)
                        {
                            break;
                        }
                    }

                    //find "center of mass" for just the circle+checkmark icon
                    var xCenterNew = x;
                    var yCenterNew = y;
                    var rightmost = 0; //rightmost edge of circle+checkmark icon
                    sumBlack = 1;
                    //use "flood search" approach from the pixel found above to find the whole checkmark+circle icon
                    var searchSpace = new Stack<Point>();
                    var pixelChecked = new Dictionary<Point, bool>();
                    searchSpace.Push(new Point(x, y));
                    while (searchSpace.Count > 0)
                    {
                        var p = searchSpace.Pop();
                        if (!pixelChecked.TryGetValue(p, out var val) || !val)
                        {
                            pixelChecked[p] = true;
                            for (var xOff = -2; xOff <= 2; xOff++)
                            {
                                for (var yOff = -2; yOff <= 2; yOff++)
                                {
                                    if (p.X + xOff > 0 && p.X + xOff < imgWidth && p.Y + yOff > 0 &&
                                        p.Y + yOff < imgHeight)
                                    {
                                        index = 4 * (p.X + xOff + (p.Y + yOff) * imgWidth);
                                        if (lockedBitmapBytes[index] == 0 && lockedBitmapBytes[index + 1] == 0 &&
                                            lockedBitmapBytes[index + 2] == 0 && lockedBitmapBytes[index + 3] == 255)
                                        {
                                            searchSpace.Push(new Point(p.X + xOff, p.Y + yOff));
                                            //cloneBitmap.SetPixel(p.X + xOff, p.Y + yOff, Color.Green); //debugging markings, uncomment as needed
                                            xCenterNew += p.X + xOff;
                                            yCenterNew += p.Y + yOff;
                                            sumBlack++;
                                            if (p.X + xOff > rightmost)
                                                rightmost = p.X + xOff;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (sumBlack < Height) continue; //not enough black = ignore and move on

                    xCenterNew /= sumBlack;
                    yCenterNew /= sumBlack;

                    //Search slight bit up and down to get well within the long line of the checkmark
                    var lowest = yCenterNew + 1000;
                    var highest = yCenterNew - 1000;
                    for (var yOff = -5; yOff < 5; yOff++)
                    {
                        var checkY = yCenterNew + yOff;
                        if (checkY > 0 && checkY < imgHeight)
                        {
                            index = 4 * (xCenterNew + (checkY) * imgWidth);
                            if (lockedBitmapBytes[index] == 0 && lockedBitmapBytes[index + 1] == 0 &&
                                lockedBitmapBytes[index + 2] == 0 && lockedBitmapBytes[index + 3] == 255)
                            {
                                if (checkY > highest)
                                    highest = checkY;

                                if (checkY < lowest)
                                    lowest = checkY;
                            }
                        }
                    }

                    yCenterNew = (highest + lowest) / 2;


                    //mark second-pass center
                    filteredImage.SetPixel(Left + xCenterNew, Top + yCenterNew, Color.Magenta);

                    //debugging markings and save, uncomment as needed
                    //cloneBitmap.SetPixel(xCenter, yCenter, Color.Red);
                    //cloneBitmap.SetPixel(xCenterNew, yCenterNew, Color.Magenta);
                    //cloneBitmap.Save(ApplicationConstants.AppPath + @"\Debug\NumberCenter_" + i + "_" + j + "_" + sumBlack + " " + timestamp + ".png");
                    //cloneBitmapColoured.Save(ApplicationConstants.AppPath + @"\Debug\ColoredNumberCenter_" + i + "_" + j + "_" + sumBlack + " " + timestamp + ".png");

                    //get cloneBitmapColoured as array for fast access
                    imgHeight = cloneBitmapColoured.Height;
                    imgWidth = cloneBitmapColoured.Width;
                    lockedBitmapData = cloneBitmapColoured.LockBits(
                        new Rectangle(0, 0, imgWidth, cloneBitmapColoured.Height), ImageLockMode.WriteOnly,
                        cloneBitmapColoured.PixelFormat);
                    numBytes = Math.Abs(lockedBitmapData.Stride) * lockedBitmapData.Height;
                    lockedBitmapBytes = new byte[numBytes]; //Format is ARGB, in order BGRA
                    Marshal.Copy(lockedBitmapData.Scan0, lockedBitmapBytes, 0, numBytes);
                    cloneBitmapColoured.UnlockBits(lockedBitmapData);

                    //search diagonally from second-pass center for colours frequently occuring 3 pixels in a row horizontally. Most common one of these should be the "amount label background colour"
                    var pointsToCheck = new Queue<Point>();
                    var colorHits = new Dictionary<Color, int>();
                    pointsToCheck.Enqueue(new Point(xCenterNew, yCenterNew + 1));
                    pointsToCheck.Enqueue(new Point(xCenterNew, yCenterNew - 1));
                    var stop = false;
                    while (pointsToCheck.Count > 0)
                    {
                        var p = pointsToCheck.Dequeue();
                        var offset = (p.Y > yCenter ? 1 : -1);
                        if (p.X + 3 > Width || p.X - 3 < 0 || p.Y + 3 > imgHeight || p.Y - 3 < 0)
                        {
                            stop = true; //keep going until we almost hit the edge of the image
                        }

                        if (!stop)
                        {
                            pointsToCheck.Enqueue(new Point(p.X + offset, p.Y + offset));
                        }

                        index = 4 * (p.X + p.Y * imgWidth);
                        if (lockedBitmapBytes[index] == lockedBitmapBytes[index - 4] &&
                            lockedBitmapBytes[index] == lockedBitmapBytes[index + 4]
                            && lockedBitmapBytes[index + 1] == lockedBitmapBytes[index + 1 - 4] &&
                            lockedBitmapBytes[index + 1] == lockedBitmapBytes[index + 1 + 4]
                            && lockedBitmapBytes[index + 2] == lockedBitmapBytes[index + 2 - 4] &&
                            lockedBitmapBytes[index + 2] == lockedBitmapBytes[index + 2 + 4]
                            && lockedBitmapBytes[index + 3] == lockedBitmapBytes[index + 3 - 4] &&
                            lockedBitmapBytes[index + 3] == lockedBitmapBytes[index + 3 + 4])
                        {
                            var color = Color.FromArgb(lockedBitmapBytes[index + 3], lockedBitmapBytes[index + 2],
                                lockedBitmapBytes[index + 1], lockedBitmapBytes[index]);
                            if (!colorHits.TryAdd(color, 1))
                            {
                                colorHits[color]++;
                            }
                        }
                    }

                    var topColor = Color.FromArgb(255, 255, 255, 255);
                    var topColorScore = 0;
                    foreach (var key in colorHits.Keys)
                    {
                        if (colorHits[key] > topColorScore)
                        {
                            topColor = key;
                            topColorScore = colorHits[key];
                        }
                        //Debug.WriteLine("Color: " + key.ToString() + ", Value: " + colorHits[key]);
                    }

                    Logger.Debug("Top Color: {Color}, Value: {Value}", topColor, topColorScore);

                    if (topColor == Color.FromArgb(255, 255, 255, 255))
                        continue; //if most common colour is our default value, ignore and move on

                    //get unfilteredImage as array for fast access
                    imgWidth = unfilteredImage.Width;

                    lockedBitmapData = unfilteredImage.LockBits(new Rectangle(0, 0, imgWidth, unfilteredImage.Height),
                        ImageLockMode.WriteOnly, unfilteredImage.PixelFormat);
                    numBytes = Math.Abs(lockedBitmapData.Stride) * lockedBitmapData.Height;
                    lockedBitmapBytes = new byte[numBytes]; //Format is ARGB, in order BGRA
                    Marshal.Copy(lockedBitmapData.Scan0, lockedBitmapBytes, 0, numBytes);

                    unfilteredImage.UnlockBits(lockedBitmapData);

                    //recalculate centers to be relative to whole image
                    rightmost = rightmost + Left + 1;
                    xCenter += Left;
                    yCenter += Top;
                    xCenterNew += Left;
                    yCenterNew += Top;
                    Logger.Debug("Old Center {X}, {Y}", xCenter, yCenter);
                    Logger.Debug("New Center {X}, {Y}", xCenterNew, yCenterNew);

                    //search diagonally (toward top-right) from second-pass center until we find the "amount label" colour
                    x = xCenterNew;
                    y = yCenterNew;
                    index = 4 * (x + y * imgWidth);
                    var currColor = Color.FromArgb(lockedBitmapBytes[index + 3], lockedBitmapBytes[index + 2],
                        lockedBitmapBytes[index + 1], lockedBitmapBytes[index]);
                    while (x < imgWidth && y > 0 && topColor != currColor)
                    {
                        x++;
                        y--;
                        index = 4 * (x + y * imgWidth);
                        currColor = Color.FromArgb(lockedBitmapBytes[index + 3], lockedBitmapBytes[index + 2],
                            lockedBitmapBytes[index + 1], lockedBitmapBytes[index]);
                    }

                    //then search for top edge
                    Top = y;
                    while (topColor == Color.FromArgb(lockedBitmapBytes[index + 3], lockedBitmapBytes[index + 2],
                               lockedBitmapBytes[index + 1], lockedBitmapBytes[index]))
                    {
                        Top--;
                        index = 4 * (x + Top * imgWidth);
                    }

                    Top += 2;
                    index = 4 * (x + Top * imgWidth);

                    //search for left edge
                    Left = x;
                    while (topColor == Color.FromArgb(lockedBitmapBytes[index + 3], lockedBitmapBytes[index + 2],
                               lockedBitmapBytes[index + 1], lockedBitmapBytes[index]))
                    {
                        Left--;
                        index = 4 * (Left + Top * imgWidth);
                    }

                    Left += 2;
                    index = 4 * (Left + Top * imgWidth);

                    //search for height (bottom edge)
                    Height = 0;
                    while (topColor == Color.FromArgb(lockedBitmapBytes[index + 3], lockedBitmapBytes[index + 2],
                               lockedBitmapBytes[index + 1], lockedBitmapBytes[index]))
                    {
                        Height++;
                        index = 4 * (Left + (Top + Height) * imgWidth);
                    }

                    Height -= 2;

                    Left = rightmost; // cut out checkmark+circle icon
                    index = 4 * (Left + (Top + Height) * imgWidth);

                    //search for width
                    Width = 0;
                    while (topColor == Color.FromArgb(lockedBitmapBytes[index + 3], lockedBitmapBytes[index + 2],
                               lockedBitmapBytes[index + 1], lockedBitmapBytes[index]))
                    {
                        Width++;
                        index = 4 * (Left + Width + Top * imgWidth);
                    }

                    Width -= 2;

                    if (Width < 5 || Height < 5) continue; //if extremely low width or height, ignore

                    cloneRect = new Rectangle(Left, Top, Width, Height);

                    cloneBitmap.Dispose();
                    //load up "amount label" image and draw debug markings for the area
                    cloneBitmap = filteredImageClean.Clone(cloneRect, filteredImageClean.PixelFormat);
                    g.DrawRectangle(CyanPen, cloneRect);

                    //do OCR
                    using (var page = _tesseractService.FirstEngine.Process(cloneBitmap, PageSegMode.SingleLine))
                    {
                        using (var iterator = page.GetIterator())
                        {
                            iterator.Begin();
                            var rawText = iterator.GetText(PageIteratorLevel.TextLine);
                            rawText = rawText?.Replace(" ", string.Empty);
                            //if no number found, 1 of item
                            if (!int.TryParse(rawText, out var itemCount))
                            {
                                itemCount = 1;
                            }

                            g.DrawString(rawText, font, Brushes.Cyan, new Point(cloneRect.X, cloneRect.Y));

                            //find what item the item belongs to
                            var itemLabel = new Rectangle(columns[j].X, rows[i].Top, columns[j].Width, rows[i].Height);
                            g.DrawRectangle(CyanPen, itemLabel);
                            for (var k = 0; k < foundItems.Count; k++)
                            {
                                var item = foundItems[k];
                                if (item.Bounding.IntersectsWith(itemLabel))
                                {
                                    item.Count = itemCount;
                                    foundItems[k] = item;
                                }
                            }
                        }
                    }

                    //mark first-pass and second-pass center of checkmark (in case they've been drawn over)
                    filteredImage.SetPixel(xCenter, yCenter, Color.Red);
                    filteredImage.SetPixel(xCenterNew, yCenterNew, Color.Magenta);

                    cloneBitmapColoured.Dispose();
                    cloneBitmap.Dispose();
                }
            }

            //return OCR to any symbols
            _tesseractService.FirstEngine.SetVariable("tessedit_char_whitelist", string.Empty);
        }
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
        var lockedBitmapBytes = new Span<byte>((void*)lockedBitmapData.Scan0, numBytes);

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
                            leftEdge = (leftEdge == -1 ? tempX : leftEdge);
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

    private static bool CustomThresholdFilter(Color test)
    {
        if (_settings.CF_usePrimaryHSL)
        {
            if (_settings.CF_pHueMax >= test.GetHue() && test.GetHue() >= _settings.CF_pHueMin &&
                _settings.CF_pSatMax >= test.GetSaturation() && test.GetSaturation() >= _settings.CF_pSatMin &&
                _settings.CF_pBrightMax >= test.GetBrightness() && test.GetBrightness() >= _settings.CF_pBrightMin)
                return true;
        }

        if (_settings.CF_usePrimaryRGB)
        {
            if (_settings.CF_pRMax >= test.R && test.R >= _settings.CF_pRMin &&
                _settings.CF_pGMax >= test.G && test.G >= _settings.CF_pGMin &&
                _settings.CF_pBMax >= test.B && test.B >= _settings.CF_pBMin)
                return true;
        }

        if (_settings.CF_useSecondaryHSL)
        {
            if (_settings.CF_sHueMax >= test.GetHue() && test.GetHue() >= _settings.CF_sHueMin &&
                _settings.CF_sSatMax >= test.GetSaturation() && test.GetSaturation() >= _settings.CF_sSatMin &&
                _settings.CF_sBrightMax >= test.GetBrightness() && test.GetBrightness() >= _settings.CF_sBrightMin)
                return true;
        }

        if (_settings.CF_useSecondaryRGB)
        {
            if (_settings.CF_sRMax >= test.R && test.R >= _settings.CF_sRMin &&
                _settings.CF_sGMax >= test.G && test.G >= _settings.CF_sGMin &&
                _settings.CF_sBMax >= test.B && test.B >= _settings.CF_sBMin)
                return true;
        }


        return false;
    }

    private static bool ThemeThresholdFilter(Color test, WFtheme theme)
    {
        //treat unknown as custom, for safety
        if (theme is WFtheme.CUSTOM or WFtheme.UNKNOWN)
            return CustomThresholdFilter(test);

        var primary = ThemeDetector.PrimaryThemeColor(theme);
        var secondary = ThemeDetector.SecondaryThemeColor(theme);

        return theme switch
        {
            // TO CHECK
            WFtheme.VITRUVIAN => Math.Abs(test.GetHue() - primary.GetHue()) < 4 && test.GetSaturation() >= 0.25 && test.GetBrightness() >= 0.42,
            WFtheme.LOTUS => Math.Abs(test.GetHue() - primary.GetHue()) < 5 && test.GetSaturation() >= 0.65 && Math.Abs(test.GetBrightness() - primary.GetBrightness()) <= 0.1
                             || (Math.Abs(test.GetHue() - secondary.GetHue()) < 15 && test.GetBrightness() >= 0.65),
            // TO CHECK
            WFtheme.OROKIN => (Math.Abs(test.GetHue() - primary.GetHue()) < 5 && test.GetBrightness() <= 0.42 && test.GetSaturation() >= 0.1) || (Math.Abs(test.GetHue() - secondary.GetHue()) < 5
                && test.GetBrightness() <= 0.5 && test.GetBrightness() >= 0.25
                && test.GetSaturation() >= 0.25),
            WFtheme.STALKER => ((Math.Abs(test.GetHue() - primary.GetHue()) < 4 && test.GetSaturation() >= 0.55) || (Math.Abs(test.GetHue() - secondary.GetHue()) < 4 && test.GetSaturation() >= 0.66))
                               && test.GetBrightness() >= 0.25,
            WFtheme.CORPUS  => Math.Abs(test.GetHue() - primary.GetHue()) < 3 && test.GetBrightness() >= 0.42 && test.GetSaturation() >= 0.35,
            WFtheme.EQUINOX => test.GetSaturation() <= 0.2 && test.GetBrightness() >= 0.55,
            WFtheme.DARK_LOTUS => (Math.Abs(test.GetHue() - secondary.GetHue()) < 20 && test.GetBrightness() >= 0.35 && test.GetBrightness() <= 0.55 && test.GetSaturation() <= 0.25
                                   && test.GetSaturation() >= 0.05) || (Math.Abs(test.GetHue() - secondary.GetHue()) < 4 && test.GetBrightness() >= 0.50 && test.GetSaturation() >= 0.20),
            WFtheme.FORTUNA => ((Math.Abs(test.GetHue() - primary.GetHue()) < 3 && test.GetBrightness() >= 0.35) || (Math.Abs(test.GetHue() - secondary.GetHue()) < 4 && test.GetBrightness() >= 0.15))
                               && test.GetSaturation() >= 0.20,
            WFtheme.HIGH_CONTRAST => (Math.Abs(test.GetHue() - primary.GetHue()) < 3 || Math.Abs(test.GetHue() - secondary.GetHue()) < 2) && test.GetSaturation() >= 0.75
                && test.GetBrightness() >= 0.35 // || Math.Abs(test.GetHue() - secondary.GetHue()) < 2;
            ,
            // TO CHECK
            WFtheme.LEGACY => (test.GetBrightness() >= 0.65) || (Math.Abs(test.GetHue() - secondary.GetHue()) < 6 && test.GetBrightness() >= 0.5 && test.GetSaturation() >= 0.5),
            WFtheme.NIDUS => (Math.Abs(test.GetHue() - (primary.GetHue() + 6)) < 8 && test.GetSaturation() >= 0.30)
                             || (Math.Abs(test.GetHue() - secondary.GetHue()) < 15 && test.GetSaturation() >= 0.55),
            WFtheme.TENNO   => (Math.Abs(test.GetHue() - primary.GetHue()) < 3 || Math.Abs(test.GetHue() - secondary.GetHue()) < 2) && test.GetSaturation() >= 0.38 && test.GetBrightness() <= 0.55,
            WFtheme.BARUUK  => (Math.Abs(test.GetHue() - primary.GetHue()) < 2) && test.GetSaturation() > 0.25 && test.GetBrightness() > 0.5,
            WFtheme.GRINEER => (Math.Abs(test.GetHue() - primary.GetHue()) < 5 && test.GetBrightness() > 0.5) || (Math.Abs(test.GetHue() - secondary.GetHue()) < 6 && test.GetBrightness() > 0.55),
            WFtheme.ZEPHYR => ((Math.Abs(test.GetHue() - primary.GetHue()) < 4 && test.GetSaturation() >= 0.55) || (Math.Abs(test.GetHue() - secondary.GetHue()) < 4 && test.GetSaturation() >= 0.66))
                              && test.GetBrightness() >= 0.25,
            var _ => Math.Abs(test.GetHue() - primary.GetHue()) < 2 || Math.Abs(test.GetHue() - secondary.GetHue()) < 2
        };
    }

    public static unsafe Bitmap ScaleUpAndFilter(Bitmap image, WFtheme active, out int[] rowHits, out int[] colHits)
    {
        if (image.Height <= SCALING_LIMIT)
        {
            partialScreenshotExpanded = new Bitmap(image.Width * SCALING_LIMIT / image.Height, SCALING_LIMIT);
            partialScreenshotExpanded.SetResolution(image.HorizontalResolution, image.VerticalResolution);

            using (var graphics = Graphics.FromImage(partialScreenshotExpanded))
            {
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                graphics.DrawImage(image, 0, 0, partialScreenshotExpanded.Width, partialScreenshotExpanded.Height);
            }

            image = partialScreenshotExpanded;
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
        var lockedBitmapBytes = new Span<byte>((void*)lockedBitmapData.Scan0, numBytes);

        Span<byte> alpha = stackalloc byte[] { 0, 0, 0, 255 };

        const int pixelSize = 4; //ARGB, order in array is BGRA
        for (var i = 0; i < numBytes; i += pixelSize)
        {
            var clr = Color.FromArgb(
                alpha: lockedBitmapBytes[i + 3],
                red: lockedBitmapBytes[i + 2],
                green: lockedBitmapBytes[i + 1],
                blue: lockedBitmapBytes[i]
            );

            if (ThemeThresholdFilter(clr, active))
            {
                alpha.CopyTo(lockedBitmapBytes.Slice(i, pixelSize));
                //Black
                var x = (i / pixelSize) % filtered.Width;
                var y = (i / pixelSize - x) / filtered.Width;
                rowHits[y]++;
                colHits[x]++;
            }
            else //White
            {
                lockedBitmapBytes.Slice(i, pixelSize).Fill(255);
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
                if (ThemeThresholdFilter(clr, active))
                    rows[y]++;
            }
        }

        end = Stopwatch.GetElapsedTime(start);
        Logger.Debug("Filtered Image. time={Time}", end);
        start = Stopwatch.GetTimestamp();

        var percWeights = new Span<double>(new double[51]);
        var topWeights = new Span<double>(new double[51]);
        var midWeights = new Span<double>(new double[51]);
        var botWeights = new Span<double>(new double[51]);

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

        fullScreen.Save(Path.Combine(ApplicationConstants.AppPathDebug, $"BorderScreenshot {timestamp}.png"));
        preFilter.Save(Path.Combine(ApplicationConstants.AppPathDebug, $"FullPartArea {timestamp}.png"));

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
            var rect = new Rectangle(cropLeft, cropTop, cropWidth, cropHei);
            partialScreenshot = preFilter.Clone(rect, PixelFormat.DontCare);
            if (partialScreenshot.Height == 0 || partialScreenshot.Width == 0)
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
        var file = Path.Combine(ApplicationConstants.AppPathDebug, "PartialScreenshot" + timestamp + ".png");
        partialScreenshot.Save(file);

        UiScaling = scaling;

        return FilterAndSeparatePartsFromPartBox(partialScreenshot, active);
    }

    private static IEnumerable<Bitmap> FilterAndSeparatePartsFromPartBox(Bitmap partBox, WFtheme active)
    {
        double weight = 0;
        double totalEven = 0;
        double totalOdd = 0;

        var width = partBox.Width;
        var height = partBox.Height;
        var counts = new int[height];
        var filtered = new Bitmap(width, height);
        for (var x = 0; x < width; x++)
        {
            var count = 0;
            for (var y = 0; y < height; y++)
            {
                var clr = partBox.GetPixel(x, y);
                if (ThemeThresholdFilter(clr, active))
                {
                    filtered.SetPixel(x, y, Color.Black);
                    counts[y]++;
                    count++;
                }
                else
                    filtered.SetPixel(x, y, Color.White);
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

        // Rarely, the selection box on certain themes can get included in the detected reward area.
        // Therefore, we check the bottom 10% of the image for this potential issue
        for (var y = height - 1; y > height * 0.9; --y)
        {
            // Assumed to be this issue if both the following criteria are met:
            // 1. A lot more black pixels than the next line up, going with 5x for the moment. The issue is almost entirely on a single line in the cases I've seen so far
            // 2. The problematic line should have a meaningful amount of black pixels. At least twice the height should be pretty good. (We don't yet know the number of players, so can't directly base it on width)
            if (counts[y] > 5 * counts[y - 1] && counts[y] > height * 2)
            {
                var tmp = filtered.Clone(new Rectangle(0, 0, width, y), filtered.PixelFormat);
                Logger.Debug("Possible selection border in image, cropping height to: " + y + " (was " + height + ")");
                filtered.Dispose();
                filtered = tmp;
            }
        }

        if (totalEven == 0 || totalOdd == 0)
        {
            Main.RunOnUIThread(() =>
            {
                Main.StatusUpdate(
                    "Unable to detect reward from selection screen\nScanning inventory? Hold down snap-it modifier",
                    StatusSeverity.Error);
            });
            ProcessingActive.GetAndSet(false);
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

        for (var i = 0; i < playerCount; i++)
        {
            var srcRegion = new Rectangle(currLeft + i * boxWidth, 0, boxWidth, boxHeight);
            var newBox = new Bitmap(boxWidth, boxHeight);
            using (var grD = Graphics.FromImage(newBox))
                grD.DrawImage(filtered, destRegion, srcRegion, GraphicsUnit.Pixel);
            newBox.Save(ApplicationConstants.AppPath + @"\Debug\PartBox(" + i + ") " + timestamp + ".png");
            yield return newBox;
        }

        filtered.Dispose();
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

        return RE.Replace(ret, string.Empty).Trim();
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
                Logger.Debug("Using new windows capture API for an HDR screenshot");
                return _windowsScreenshot;
                break;
            case HdrSupportEnum.Off:
                Logger.Debug("Using old GDI service for a SDR screenshot");
                return _gdiScreenshot;
                break;
            case HdrSupportEnum.Auto:
                var isHdr = _hdrDetector.IsHdr();
                Logger.Debug($"Automatically determining HDR status: {isHdr} | Using corresponding service");
                return isHdr ? _windowsScreenshot : _gdiScreenshot;
                break;
            default:
                throw new NotImplementedException(
                    $"HDR support option '{_settings.HdrSupport}' does not have a corresponding screenshot service.");
        }
    }

    internal static void SnapScreenshot()
    {
        Main.SnapItOverlayWindow.Populate(CaptureScreenshot().GetAwaiter().GetResult());
        Main.SnapItOverlayWindow.Left = _window.Window.Left / _window.DpiScaling;
        Main.SnapItOverlayWindow.Top = _window.Window.Top / _window.DpiScaling;
        Main.SnapItOverlayWindow.Width = _window.Window.Width / _window.DpiScaling;
        Main.SnapItOverlayWindow.Height = _window.Window.Height / _window.DpiScaling;
        Main.SnapItOverlayWindow.Topmost = true;
        Main.SnapItOverlayWindow.Focusable = true;
        Main.SnapItOverlayWindow.Show();
        Main.SnapItOverlayWindow.Focus();
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
    public string Name { get; set; } = itemName;
    public Rectangle Bounding { get; set; } = boundingbox;
    public int Count { get; set; } = 1; //if no label found, assume 1
    public bool Warning { get; set; } = showWarning;
}
