using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Serilog;
using Tesseract;
using WFInfo.Services.HDRDetection;
using WFInfo.Services.Screenshot;
using WFInfo.Services.WindowInfo;
using WFInfo.Settings;
using Brushes = System.Drawing.Brushes;
using Clipboard = System.Windows.Forms.Clipboard;
using Color = System.Drawing.Color;
using Pen = System.Drawing.Pen;
using Point = System.Drawing.Point;
using Rect = Tesseract.Rect;

namespace WFInfo.Services.OpticalCharacterRecognition;

internal partial class OCR
{
    private static readonly ILogger Logger = Log.Logger.ForContext<OCR>();

    #region variabels and sizzle

    private static int numberOfRewardsDisplayed;

    private const NumberStyles Styles = NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands |
                                        NumberStyles.AllowExponent;

    // UI - Scaling used in Warframe
    private static double uiScaling;

    private static readonly Regex RE = WordTrimRegEx();

    // Pixel measurements for reward screen @ 1920 x 1080 with 100% scale https://docs.google.com/drawings/d/1Qgs7FU2w1qzezMK-G1u9gMTsQZnDKYTEU36UPakNRJQ/edit
    public const int pixleRewardWidth = 968;
    public const int pixleRewardHeight = 235;
    public const int pixleRewardYDisplay = 316;
    public const int pixelRewardLineHeight = 48;

    public const int SCALING_LIMIT = 100;
    public static bool processingActive;

    private static Bitmap bigScreenshot;
    private static Bitmap? partialScreenshot;
    private static Bitmap partialScreenshotExpanded;

    private static string[]? firstChecks;
#pragma warning disable IDE0044 // Add readonly modifier
    private static int[]? firstProximity = [-1, -1, -1, -1];
#pragma warning restore IDE0044 // Add readonly modifier
    private static string timestamp;

    private static string clipboard;

    #endregion

    private static ITesseractService _tesseractService;
    private static ISoundPlayer _soundPlayer;
    private static ApplicationSettings _settings;
    private static IWindowInfoService _window;
    private static IHDRDetectorService _hdrDetector;
    private static IThemeDetector ThemeDetector;

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
        var hdrDetector = sp.GetRequiredService<IHDRDetectorService>();
        var gdiScreenshot = sp.GetRequiredKeyedService<IScreenshotService>(ScreenshotTypes.Gdi);
        var windowsScreenshot = sp.GetKeyedService<IScreenshotService>(ScreenshotTypes.WindowCapture);
        Init(
            tesseractService: tesseractService,
            soundPlayer: soundPlayer,
            settings: settings,
            window: window,
            themeDetector: themeDetector,
            hdrDetector: hdrDetector,
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
        IWindowInfoService window,
        IHDRDetectorService hdrDetector,
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
        _window = window;
        _hdrDetector = hdrDetector;
        _overlays = overlays;

        _gdiScreenshot = gdiScreenshot;
        _windowsScreenshot = windowsScreenshot;
    }

    public static void DisposeTesseract()
    {
        _tesseractService?.Dispose();
    }

    internal static void ProcessRewardScreen(Bitmap? file = null)
    {
        #region initializers

        if (processingActive)
        {
            Main.StatusUpdate("Still Processing Reward Screen", 2);
            return;
        }

        var primeRewards = new List<string>();

        processingActive = true;
        Main.StatusUpdate("Processing...", 0);
        Logger.Debug(
            "----  Triggered Reward Screen Processing  ------------------------------------------------------------------");

        DateTime time = DateTime.UtcNow;
        timestamp = time.ToString("yyyy-MM-dd HH-mm-ssff", Main.Culture);
        long start = Stopwatch.GetTimestamp();

        List<Bitmap> parts = new List<Bitmap>();

        bigScreenshot = file ?? CaptureScreenshot();
        try
        {
            parts.AddRange(ExtractPartBoxAutomatically(out uiScaling, out _, bigScreenshot));
        }
        catch (Exception e)
        {
            processingActive = false;
            Debug.WriteLine(e);
            return;
        }

        firstChecks = new string[parts.Count];
        Task[] tasks = new Task[parts.Count];
        for (int i = 0; i < parts.Count; i++)
        {
            int tempI = i;
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
            processingActive = false;
            var end = Stopwatch.GetElapsedTime(start);
            Logger.Debug(
                "----  Partial Processing Time, couldn't find rewards {Time}  ------------------------------------------------------------------------------------------"[..108],
                end);
            Main.StatusUpdate("Couldn't find any rewards to display", 2);
            if (firstChecks == null)
            {
                Main.RunOnUIThread(() =>
                {
                    Main.SpawnErrorPopup(time);
                });
            }
        }

        double bestPlat = 0;
        int bestDucat = 0;
        int bestPlatItem = 0;
        int bestDucatItem = 0;
        List<int> unownedItems = [];

        #endregion

        #region processing data

        if (firstChecks.Length > 0)
        {
            numberOfRewardsDisplayed = firstChecks.Length;
            clipboard = string.Empty;
            int width = (int)(pixleRewardWidth * _window.ScreenScaling * uiScaling) + 10;
            int startX = _window.Center.X - width / 2 + (int)(width * 0.004);

            if (firstChecks.Length % 2 == 1)
                startX += width / 8;

            if (firstChecks.Length <= 2)
                startX += 2 * (width / 8);

            int overWid = (int)(width / (4.1 * _window.DpiScaling));
            int startY = (int)(_window.Center.Y / _window.DpiScaling - 20 * _window.ScreenScaling * uiScaling);
            int partNumber = 0;
            bool hideRewardInfo = false;
            for (int i = 0; i < firstChecks.Length; i++)
            {
                string part = firstChecks[i];

                #region found a part

                string correctName = Main.DataBase.GetPartName(part, out firstProximity[i], false, out _);
                string primeSetName = Data.GetSetName(correctName);
                JObject job = (JObject)Main.DataBase.MarketData.GetValue(correctName);
                JObject primeSet = (JObject)Main.DataBase.MarketData.GetValue(primeSetName);
                string ducats = job["ducats"].ToObject<string>();
                if (int.Parse(ducats, Main.Culture) == 0)
                {
                    hideRewardInfo = true;
                }

                //else if (correctName != "Kuva" || correctName != "Exilus Weapon Adapter Blueprint" || correctName != "Riven Sliver" || correctName != "Ayatan Amber Star")
                primeRewards.Add(correctName);
                string plat = job["plat"].ToObject<string>();
                string primeSetPlat = null;
                if (primeSet != null)
                {
                    primeSetPlat = (string)primeSet["plat"];
                }

                double platinum = double.Parse(plat, Styles, Main.Culture);
                string volume = job["volume"].ToObject<string>();
                bool vaulted = Main.DataBase.IsPartVaulted(correctName);
                bool mastered = Main.DataBase.IsPartMastered(correctName);
                string partsOwned = Main.DataBase.PartsOwned(correctName);
                string partsCount = Main.DataBase.PartsCount(correctName);
                int duc = int.Parse(ducats, Main.Culture);

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

                if (duc > 0 && !mastered && int.Parse(partsOwned, Main.Culture) < int.Parse(partsCount, Main.Culture))
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
                    Overlay.rewardsDisplaying = true;

                    if (_settings.IsOverlaySelected)
                    {
                        _overlays[partNumber].LoadTextData(correctName, plat, primeSetPlat, ducats, volume, vaulted,
                            mastered, $"{partsOwned} / {partsCount}", "", hideRewardInfo, false);
                        _overlays[partNumber].Resize(overWid);
                        _overlays[partNumber]
                            .Display(
                                (int)((startX + width / 4 * partNumber + _settings.OverlayXOffsetValue) /
                                      _window.DpiScaling),
                                startY + (int)(_settings.OverlayYOffsetValue / _window.DpiScaling), _settings.Delay);
                    }
                    else if (!_settings.IsLightSelected)
                    {
                        Main.Window.loadTextData(correctName, plat, primeSetPlat, ducats, volume, vaulted, mastered,
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
            Main.StatusUpdate($"Completed processing ({end})", 0);

            if (Main.ListingHelper.PrimeRewards.Count == 0 ||
                Main.ListingHelper.PrimeRewards[^1].Except(primeRewards).ToList().Count != 0)
            {
                Main.ListingHelper.PrimeRewards.Add(primeRewards);
            }

            if (_settings.HighlightRewards)
            {
                Main.RunOnUIThread(() =>
                {
                    foreach (int item in unownedItems)
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
        {
            _soundPlayer.Play();
        }

        var path = Path.Combine(ApplicationConstants.AppPath, "Debug");
        var directory = new DirectoryInfo(path);
        directory.GetFiles()
                 .Where(f => f.CreationTime < DateTime.Now.AddHours(-1 * _settings.ImageRetentionTime))
                 .ToList().ForEach(f => f.Delete());

        if (partialScreenshot is not null)
        {
            path = Path.Combine(path, $"PartBox {timestamp}.png");
            partialScreenshot.Save(path);
            partialScreenshot.Dispose();
            partialScreenshot = null;
        }

        processingActive = false;
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
            clipboard = sb.ToString();
    }

    #endregion clipboard

    internal static int GetSelectedReward(Point lastClick)
    {
        Debug.WriteLine(lastClick.ToString());
        var primeRewardIndex = 0;
        lastClick.Offset(-_window.Window.X, -_window.Window.Y);
        var width = _window.Window.Width * (int)_window.DpiScaling;
        var height = _window.Window.Height * (int)_window.DpiScaling;
        var mostWidth = (int)(pixleRewardWidth * _window.ScreenScaling * uiScaling);
        var mostLeft = (width / 2) - (mostWidth / 2);
        var bottom = height / 2 -
                     (int)((pixleRewardYDisplay - pixleRewardHeight) * _window.ScreenScaling * 0.5 * uiScaling);
        var top = height / 2 - (int)((pixleRewardYDisplay) * _window.ScreenScaling * uiScaling);
        var selectionRectangle = new Rectangle(mostLeft, top, mostWidth, bottom / 2);
        if (numberOfRewardsDisplayed == 3)
        {
            var offset = selectionRectangle.Width / 8;
            selectionRectangle = selectionRectangle with
            {
                X = selectionRectangle.X + offset, Width = selectionRectangle.Width - offset * 2
            };
        }

        if (!selectionRectangle.Contains(lastClick))
            return -1;

        var middelHeight = top + bottom / 4;
        var length = mostWidth / 8;

        var RewardPoints4 = new List<Point>()
        {
            new Point(mostLeft + length, middelHeight),
            new Point(mostLeft + 3 * length, middelHeight),
            new Point(mostLeft + 5 * length, middelHeight),
            new Point(mostLeft + 7 * length, middelHeight)
        };

        var RewardPoints3 = new List<Point>()
        {
            new Point(mostLeft + 2 * length, middelHeight),
            new Point(mostLeft + 4 * length, middelHeight),
            new Point(mostLeft + 6 * length, middelHeight)
        };

        var lowestDistance = int.MaxValue;
        var lowestDistancePoint = new Point();
        if (numberOfRewardsDisplayed == 1) //rare, but can happen if others don't get enough traces
        {
            primeRewardIndex = 0;
        }
        else if (numberOfRewardsDisplayed != 3)
        {
            foreach (var pnt in RewardPoints4)
            {
                var distanceToLastClick = ((lastClick.X - pnt.X) * (lastClick.X - pnt.X) +
                                           (lastClick.Y - pnt.Y) * (lastClick.Y - pnt.Y));
                Debug.WriteLine($"current point: {pnt}, with distance: {distanceToLastClick}");

                if (distanceToLastClick >= lowestDistance)
                    continue;

                lowestDistance = distanceToLastClick;
                lowestDistancePoint = pnt;
                primeRewardIndex = RewardPoints4.IndexOf(pnt);
            }

            if (numberOfRewardsDisplayed == 2)
            {
                if (primeRewardIndex == 1)
                    primeRewardIndex = 0;
                if (primeRewardIndex >= 2)
                    primeRewardIndex = 1;
            }
        }
        else
        {
            foreach (var pnt in RewardPoints3)
            {
                var distanceToLastClick = ((lastClick.X - pnt.X) * (lastClick.X - pnt.X) +
                                           (lastClick.Y - pnt.Y) * (lastClick.Y - pnt.Y));
                Debug.WriteLine($"current point: {pnt}, with distance: {distanceToLastClick}");

                if (distanceToLastClick >= lowestDistance) continue;
                lowestDistance = distanceToLastClick;
                lowestDistancePoint = pnt;
                primeRewardIndex = RewardPoints3.IndexOf(pnt);
            }
        }

        #region debuging image

        /*Debug.WriteLine($"Closest point: {lowestDistancePoint}, with distance: {lowestDistance}");

        timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff", Main.Culture);
        var img = CaptureScreenshot();
        var pinkP = new Pen(Brushes.Pink);
        var blackP = new Pen(Brushes.Black);
        using (Graphics g = Graphics.FromImage(img))
        {
            g.DrawRectangle(blackP, selectionRectangle);
            if (numberOfRewardsDisplayed != 3)
            {
                foreach (var pnt in RewardPoints4)
                {
                    pnt.Offset(-5, -5);
                    g.DrawEllipse(blackP, new Rectangle(pnt, new Size(10, 10)));
                }
            }
            else
            {
                foreach (var pnt in RewardPoints3)
                {
                    pnt.Offset(-5, -5);
                    g.DrawEllipse(blackP, new Rectangle(pnt, new Size(10, 10)));
                }
            }

            g.DrawString($"User selected reward nr{primeRewardIndex}", new Font(FontFamily.GenericMonospace, 16), Brushes.Chartreuse, lastClick);
            g.DrawLine(pinkP, lastClick, lowestDistancePoint);
            lastClick.Offset(-5, -5);

            g.DrawEllipse(pinkP, new Rectangle(lastClick, new Size(10, 10)));
        }
        img.Save(ApplicationConstants.AppPath + @"\Debug\GetSelectedReward " + timestamp + ".png");
        pinkP.Dispose();
        blackP.Dispose();
        img.Dispose();*/

        #endregion

        return primeRewardIndex;
    }

    private const double ERROR_DETECTION_THRESH = 0.25;

    private static bool CheckIfError()
    {
        if (firstChecks == null || firstProximity == null)
            return false;

        int max = Math.Min(firstChecks.Length, firstProximity.Length);
        for (int i = 0; i < max; i++)
            if (firstProximity[i] > ERROR_DETECTION_THRESH * firstChecks[i].Length)
                return true;

        return false;
    }

    public static WFtheme GetThemeWeighted(out double closestThresh, Bitmap? image = null)
    {
        image ??= CaptureScreenshot();
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
        if ((partName.Length < 13 && _settings.Locale == "en") ||
            (partName.Replace(" ", string.Empty).Length < 6 &&
             _settings.Locale == "ko")) // if part name is smaller than "Bo prime handle" skip current part
            //TODO: Add a min character for other locale here.
            return false;
        return true;
    }

    /// <summary>
    /// Processes the image the user cropped in the selection
    /// </summary>
    /// <param name="snapItImage"></param>
    internal static void ProcessSnapIt(Bitmap snapItImage, Bitmap fullShot, Point snapItOrigin)
    {
        var watch = new Stopwatch();
        watch.Start();
        long start = watch.ElapsedMilliseconds;

        //timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff", Main.Culture);
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff", Main.Culture);
        WFtheme theme = GetThemeWeighted(out _, fullShot);
        snapItImage.Save(ApplicationConstants.AppPath + @"\Debug\SnapItImage " + timestamp + ".png");
        Bitmap snapItImageFiltered = ScaleUpAndFilter(snapItImage, theme, out int[] rowHits, out int[] colHits);
        snapItImageFiltered.Save(ApplicationConstants.AppPath + @"\Debug\SnapItImageFiltered " + timestamp + ".png");
        List<InventoryItem> foundParts = FindAllParts(snapItImageFiltered, snapItImage, rowHits, colHits);
        long end = watch.ElapsedMilliseconds;
        Main.StatusUpdate("Completed snapit Processing(" + (end - start) + "ms)", 0);
        string csv = string.Empty;
        snapItImage.Dispose();
        snapItImageFiltered.Dispose();
        if (!File.Exists(ApplicationConstants.AppPath + @"\export " + DateTime.UtcNow.ToString("yyyy-MM-dd", Main.Culture) +
                         ".csv") && _settings.SnapitExport)
            csv += "ItemName,Plat,Ducats,Volume,Vaulted,Owned,partsDetected" +
                   DateTime.UtcNow.ToString("yyyy-MM-dd", Main.Culture) + Environment.NewLine;
        int resultCount = foundParts.Count;
        for (int i = 0; i < foundParts.Count; i++)
        {
            var part = foundParts[i];
            if (!PartNameValid(part.Name))
            {
                foundParts.RemoveAt(
                    i--); //remove invalid part from list to not clog VerifyCount. Decrement to not skip any entries
                resultCount--;
                continue;
            }

            Debug.WriteLine($"Part  {foundParts.IndexOf(part)} out of {foundParts.Count}");
            string name = Main.DataBase.GetPartName(part.Name, out int levenDist, false, out bool multipleLowest);
            string primeSetName = Data.GetSetName(name);
            if (levenDist > Math.Min(part.Name.Length, name.Length) / 3 || multipleLowest)
            {
                //show warning triangle if the result is of questionable accuracy. The limit is basically arbitrary
                part.Warning = true;
            }

            bool doWarn = part.Warning;
            part.Name = name;
            foundParts[i] = part;
            JObject job = Main.DataBase.MarketData.GetValue(name).ToObject<JObject>();
            JObject primeSet = (JObject)Main.DataBase.MarketData.GetValue(primeSetName);
            string plat = job["plat"].ToObject<string>();
            string primeSetPlat = null;
            if (primeSet != null)
            {
                primeSetPlat = (string)primeSet["plat"];
            }

            string ducats = job["ducats"].ToObject<string>();
            string volume = job["volume"].ToObject<string>();
            bool vaulted = Main.DataBase.IsPartVaulted(name);
            bool mastered = Main.DataBase.IsPartMastered(name);
            string partsOwned = Main.DataBase.PartsOwned(name);
            string partsDetected = part.Count.ToString();

            if (_settings.SnapitExport)
            {
                var owned = string.IsNullOrEmpty(partsOwned) ? "0" : partsOwned;
                csv += name + "," + plat + "," + ducats + "," + volume + "," + vaulted.ToString(Main.Culture) + "," +
                       owned + "," + partsDetected + ", \"\"" + Environment.NewLine;
            }

            int width = (int)(part.Bounding.Width * _window.ScreenScaling);
            if (width < _settings.MinOverlayWidth)
            {
                //if (width < 50)
                //    continue;
                width = _settings.MinOverlayWidth;
            }
            else if (width > _settings.MaxOverlayWidth)
            {
                width = _settings.MaxOverlayWidth;
            }

            Main.RunOnUIThread(() =>
            {
                Overlay itemOverlay = new Overlay(_settings);
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
            Main.StatusUpdate("Couldn't find any items to display (took " + (end - start) + "ms) ", 1);
            Main.RunOnUIThread(() =>
            {
                Main.SpawnErrorPopup(DateTime.UtcNow);
            });
        }
        else
        {
            Main.StatusUpdate("Completed snapit Displaying(" + (end - start) + "ms)", 0);
        }

        watch.Stop();
        Logger.Debug("Snap-it finished, displayed reward count:{Count}, time: {Time}ms", resultCount, end - start);
        if (_settings.SnapitExport)
        {
            var file = Path.Combine(ApplicationConstants.AppPath,
                $"export {DateTime.UtcNow.ToString("yyyy-MM-dd", Main.Culture)}.csv");
            File.AppendAllText(file, csv);
        }
    }

    private static List<(Bitmap, Rectangle)> DivideSnapZones(
        Bitmap filteredImage,
        Bitmap filteredImageClean,
        int[] rowHits,
        int[] colHits)
    {
        Pen brown = new Pen(Brushes.Brown);
        Pen white = new Pen(Brushes.White);

        //find rows
        List<Tuple<int, int>> rows = []; //item1 = row top, item2 = row height
        int i = 0;
        int rowHeight = 0;
        while (i < filteredImage.Height)
        {
            if ((double)(rowHits[i]) / filteredImage.Width > _settings.SnapRowTextDensity)
            {
                int j = 0;
                while (i + j < filteredImage.Height &&
                       (double)(rowHits[i + j]) / filteredImage.Width > _settings.SnapRowEmptyDensity)
                {
                    j++;
                }

                if (j > 3) //only add "rows" of reasonable height
                {
                    rows.Add(Tuple.Create(i, j));
                    rowHeight += j;
                }

                i += j;
            }
            else
            {
                i++;
            }
        }

        rowHeight /= Math.Max(rows.Count, 1);

        //combine adjacent rows into one block of text
        i = 0;

        using (Graphics g = Graphics.FromImage(filteredImage))
        {
            using (Graphics gClean = Graphics.FromImage(filteredImageClean))
            {
                while (i + 1 < rows.Count)
                {
                    g.DrawLine(brown, 0, rows[i].Item1 + rows[i].Item2, 10000, rows[i].Item1 + rows[i].Item2);
                    gClean.DrawLine(white, 0, rows[i].Item1 + rows[i].Item2, 10000, rows[i].Item1 + rows[i].Item2);
                    if (rows[i].Item1 + rows[i].Item2 + rowHeight > rows[i + 1].Item1)
                    {
                        rows[i + 1] = Tuple.Create(rows[i].Item1,
                            rows[i + 1].Item1 - rows[i].Item1 + rows[i + 1].Item2);
                        rows.RemoveAt(i);
                    }
                    else
                    {
                        i++;
                    }
                }
            }
        }

        //find columns
        List<(int, int)> cols = []; //item1 = col start, item2 = col width

        int colStart = 0;
        i = 0;
        while (i + 1 < filteredImage.Width)
        {
            if ((double)(colHits[i]) / filteredImage.Height < _settings.SnapColEmptyDensity)
            {
                int j = 0;
                while (i + j + 1 < filteredImage.Width &&
                       (double)(colHits[i + j]) / filteredImage.Width < _settings.SnapColEmptyDensity)
                {
                    j++;
                }

                if (j > rowHeight / 2)
                {
                    if (i != 0)
                    {
                        cols.Add((colStart, i - colStart));
                    }

                    colStart = i + j + 1;
                }

                i += j;
            }

            i += 1;
        }

        if (i != colStart)
        {
            cols.Add((colStart, i - colStart));
        }

        List<(Bitmap, Rectangle)> zones = [];

        //divide image into text blocks
        for (i = 0; i < rows.Count; i++)
        {
            for (int j = 0; j < cols.Count; j++)
            {
                int top = Math.Max(rows[i].Item1 - (rowHeight / 2), 0);
                int height = Math.Min(rows[i].Item2 + rowHeight, filteredImageClean.Height - top - 1);
                int left = Math.Max(cols[j].Item1 - (rowHeight / 4), 0);
                int width = Math.Min(cols[j].Item2 + (rowHeight / 2), filteredImageClean.Width - left - 1);
                Rectangle cloneRect = new Rectangle(left, top, width, height);
                var temp = (filteredImageClean.Clone(cloneRect, filteredImageClean.PixelFormat), cloneRect);
                zones.Add(temp);
            }
        }

        using (Graphics g = Graphics.FromImage(filteredImage))
        {
            foreach (var (bitMap, rect) in zones)
            {
                g.DrawRectangle(brown, rect);
            }

            g.DrawRectangle(brown, 0, 0, rowHeight / 2, rowHeight);
        }

        brown.Dispose();
        white.Dispose();
        return zones;
    }

    private static List<Tuple<string, Rectangle>> GetTextWithBoundsFromImage(
        TesseractEngine engine, Bitmap image,
        int rectXOffset, int rectYOffset)
    {
        List<Tuple<string, Rectangle>> data = [];

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
            data.Add(Tuple.Create(currentWord, bounds));
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
        Bitmap filteredImageClean = new Bitmap(filteredImage);
        DateTime time = DateTime.UtcNow;
        string timestamp = time.ToString("yyyy-MM-dd HH-mm-ssff", Main.Culture);
        List<Tuple<List<InventoryItem>, Rectangle>>
            foundItems = []; //List containing Tuples of overlapping InventoryItems and their combined bounds
        int numberTooLarge = 0;
        int numberTooFewCharacters = 0;
        int numberTooLargeButEnoughCharacters = 0;
        var orange = new Pen(Brushes.Orange);
        var red = new SolidBrush(Color.FromArgb(100, 139, 0, 0));
        var green = new SolidBrush(Color.FromArgb(100, 255, 165, 0));
        var greenp = new Pen(green);
        var pinkP = new Pen(Brushes.Pink);
        var font = new Font("Arial", 16);
        List<(Bitmap, Rectangle)> zones;
        int snapThreads;
        if (_settings.SnapMultiThreaded)
        {
            zones = DivideSnapZones(filteredImage, filteredImageClean, rowHits, colHits);
            snapThreads = 4;
        }
        else
        {
            zones =
            [
                (filteredImageClean, new Rectangle(0, 0, filteredImageClean.Width, filteredImageClean.Height))
            ];
            snapThreads = 1;
        }

        Task<List<Tuple<string, Rectangle>>>[] snapTasks = new Task<List<Tuple<string, Rectangle>>>[snapThreads];
        for (int i = 0; i < snapThreads; i++)
        {
            int tempI = i;
            snapTasks[i] = Task.Factory.StartNew(() =>
            {
                List<Tuple<string, Rectangle>> taskResults = [];
                for (int j = tempI; j < zones.Count; j += snapThreads)
                {
                    //process images
                    List<Tuple<string, Rectangle>> currentResult =
                        GetTextWithBoundsFromImage(_tesseractService.Engines[tempI], zones[j].Item1, zones[j].Item2.X,
                            zones[j].Item2.Y);
                    taskResults.AddRange(currentResult);
                }

                return taskResults;
            });
        }

        Task.WaitAll(snapTasks);

        for (int threadNum = 0; threadNum < snapThreads; threadNum++)
        {
            foreach (Tuple<string, Rectangle> wordResult in snapTasks[threadNum].Result)
            {
                string currentWord = wordResult.Item1;
                Rectangle bounds = wordResult.Item2;
                //word is valid start comparing to others
                int VerticalPad = bounds.Height / 2;
                int HorizontalPad = (int)(bounds.Height * _settings.SnapItHorizontalNameMargin);
                var paddedBounds = new Rectangle(bounds.X - HorizontalPad, bounds.Y - VerticalPad,
                    bounds.Width + HorizontalPad * 2, bounds.Height + VerticalPad * 2);
                //var paddedBounds = new Rectangle(bounds.X - bounds.Height / 3, bounds.Y - bounds.Height / 3, bounds.Width + bounds.Height, bounds.Height + bounds.Height / 2);

                using (Graphics g = Graphics.FromImage(filteredImage))
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

                int i = foundItems.Count - 1;

                for (; i >= 0; i--)
                    if (foundItems[i].Item2.IntersectsWith(paddedBounds))
                        break;

                if (i == -1)
                {
                    //New entry added by creating a tuple. Item1 in tuple is list with just the newly found item, Item2 is its bounds
                    foundItems.Add(Tuple.Create(
                        new List<InventoryItem> { new InventoryItem(currentWord, paddedBounds) }, paddedBounds));
                }
                else
                {
                    int left = Math.Min(foundItems[i].Item2.Left, paddedBounds.Left);
                    int top = Math.Min(foundItems[i].Item2.Top, paddedBounds.Top);
                    int right = Math.Max(foundItems[i].Item2.Right, paddedBounds.Right);
                    int bot = Math.Max(foundItems[i].Item2.Bottom, paddedBounds.Bottom);

                    Rectangle combinedBounds = new Rectangle(left, top, right - left, bot - top);

                    List<InventoryItem> tempList =
                    [
                        ..foundItems[i].Item1,
                        new InventoryItem(currentWord, paddedBounds)
                    ];
                    foundItems.RemoveAt(i);
                    foundItems.Add(Tuple.Create(tempList, combinedBounds));
                }
            }
        }

        List<InventoryItem> results = [];

        foreach (Tuple<List<InventoryItem>, Rectangle> itemGroup in foundItems)
        {
            //Sort order for component words to appear in. If large height difference, sort vertically. If small height difference, sort horizontally
            itemGroup.Item1.Sort((i1, i2) =>
            {
                return Math.Abs(i1.Bounding.Top - i2.Bounding.Top) > i1.Bounding.Height / 8
                    ? i1.Bounding.Top - i2.Bounding.Top
                    : i1.Bounding.Left - i2.Bounding.Left;
            });

            //Combine into item name
            string name = itemGroup.Item1.Aggregate(string.Empty, (current, i1) => current + $"{i1.Name} ");

            results.Add(new InventoryItem(name.Trim(), itemGroup.Item2));
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
        Pen darkCyan = new Pen(Brushes.DarkCyan);
        Pen red = new Pen(Brushes.Red);
        Pen cyan = new Pen(Brushes.Cyan);
        using (Graphics g = Graphics.FromImage(filteredImage))
        {
            //sort for easier processing in loop below
            List<InventoryItem> foundItemsBottom = foundItems.OrderBy(o => o.Bounding.Bottom).ToList();
            //filter out bad parts for more accurate grid
            bool itemRemoved = false;
            for (int i = 0; i < foundItemsBottom.Count; i += (itemRemoved ? 0 : 1))
            {
                itemRemoved = false;
                if (!PartNameValid(foundItemsBottom[i].Name))
                {
                    foundItemsBottom.RemoveAt(i);
                    itemRemoved = true;
                }
            }

            List<InventoryItem> foundItemsLeft = foundItemsBottom.OrderBy(o => o.Bounding.Left).ToList();

            //features of grid system
            List<Rectangle> rows = [];
            List<Rectangle> columns = [];

            for (int i = 0; i < foundItemsBottom.Count; i++)
            {
                Rectangle currRow = new Rectangle(0, foundItemsBottom[i].Bounding.Y, 10000,
                    foundItemsBottom[i].Bounding.Height);
                Rectangle currColumn = new Rectangle(foundItemsLeft[i].Bounding.X, 0, foundItemsLeft[i].Bounding.Width,
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
                g.DrawLine(darkCyan, col.Right, 0, col.Right, 10000);
                g.DrawLine(darkCyan, col.X, 0, col.X, 10000);
            }

            foreach (var bottom in rows.Select(x => x.Bottom))
                g.DrawLine(darkCyan, 0, bottom, 10000, bottom);

            //set OCR to numbers only
            _tesseractService.FirstEngine.SetVariable("tessedit_char_whitelist", "0123456789");


            double widthMultiplier = (_settings.DoCustomNumberBoxWidth ? _settings.SnapItNumberBoxWidth : 0.4);
            //Process grid system
            for (int i = 0; i < rows.Count; i++)
            {
                for (int j = 0; j < columns.Count; j++)
                {
                    //edges of current area to scan
                    int Left = (j == 0 ? 0 : (columns[j - 1].Right + columns[j].X) / 2);
                    int Top = (i == 0 ? 0 : rows[i - 1].Bottom);
                    int Width = Math.Min((int)((columns[j].Right - Left) * widthMultiplier),
                        filteredImage.Size.Width - Left);
                    int Height = Math.Min((rows[i].Bottom - Top) / 3, filteredImage.Size.Height - Top);

                    Rectangle cloneRect = new Rectangle(Left, Top, Width, Height);
                    g.DrawRectangle(cyan, cloneRect);
                    Bitmap cloneBitmap = filteredImageClean.Clone(cloneRect, filteredImageClean.PixelFormat);
                    Bitmap cloneBitmapColoured = unfilteredImage.Clone(cloneRect, filteredImageClean.PixelFormat);


                    //get cloneBitmap as array for fast access
                    int imgWidth = cloneBitmap.Width;
                    int imgHeight = cloneBitmap.Height;
                    BitmapData lockedBitmapData = cloneBitmap.LockBits(
                        new Rectangle(0, 0, imgWidth, cloneBitmap.Height), ImageLockMode.WriteOnly,
                        cloneBitmap.PixelFormat);
                    int numbytes = Math.Abs(lockedBitmapData.Stride) * lockedBitmapData.Height;
                    byte[] LockedBitmapBytes = new byte[numbytes]; //Format is ARGB, in order BGRA
                    Marshal.Copy(lockedBitmapData.Scan0, LockedBitmapBytes, 0, numbytes);
                    cloneBitmap.UnlockBits(lockedBitmapData);

                    //find "center of mass" for black pixels in the area
                    int x = 0;
                    int y = 0;
                    int index;
                    int xCenter = 0;
                    int yCenter = 0;
                    int sumBlack = 1;
                    for (index = 0; index < numbytes; index += 4)
                    {
                        if (LockedBitmapBytes[index] == 0 && LockedBitmapBytes[index + 1] == 0 &&
                            LockedBitmapBytes[index + 2] == 0 && LockedBitmapBytes[index + 3] == 255)
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


                    int minToEdge = Math.Min(Math.Min(xCenter, imgWidth - xCenter),
                        Math.Min(yCenter, imgHeight - yCenter)); //get the distance to closest edge of image
                    //we're expected to be within the checkmark + circle, find closest black pixel to find some part of it to start at
                    for (int dist = 0; dist < minToEdge; dist++)
                    {
                        x = xCenter + dist;
                        y = yCenter;
                        index = 4 * (x + y * imgWidth);
                        if (LockedBitmapBytes[index] == 0 && LockedBitmapBytes[index + 1] == 0 &&
                            LockedBitmapBytes[index + 2] == 0 && LockedBitmapBytes[index + 3] == 255)
                        {
                            break;
                        }

                        x = xCenter - dist;
                        y = yCenter;
                        index = 4 * (x + y * imgWidth);
                        if (LockedBitmapBytes[index] == 0 && LockedBitmapBytes[index + 1] == 0 &&
                            LockedBitmapBytes[index + 2] == 0 && LockedBitmapBytes[index + 3] == 255)
                        {
                            break;
                        }

                        x = xCenter;
                        y = yCenter + dist;
                        index = 4 * (x + y * imgWidth);
                        if (LockedBitmapBytes[index] == 0 && LockedBitmapBytes[index + 1] == 0 &&
                            LockedBitmapBytes[index + 2] == 0 && LockedBitmapBytes[index + 3] == 255)
                        {
                            break;
                        }

                        x = xCenter;
                        y = yCenter - dist;
                        index = 4 * (x + y * imgWidth);
                        if (LockedBitmapBytes[index] == 0 && LockedBitmapBytes[index + 1] == 0 &&
                            LockedBitmapBytes[index + 2] == 0 && LockedBitmapBytes[index + 3] == 255)
                        {
                            break;
                        }
                    }

                    //find "center of mass" for just the circle+checkmark icon
                    int xCenterNew = x;
                    int yCenterNew = y;
                    int rightmost = 0; //rightmost edge of circle+checkmark icon
                    sumBlack = 1;
                    //use "flood search" approach from the pixel found above to find the whole checkmark+circle icon
                    Stack<Point> searchSpace = new Stack<Point>();
                    Dictionary<Point, bool> pixelChecked = new Dictionary<Point, bool>();
                    searchSpace.Push(new Point(x, y));
                    while (searchSpace.Count > 0)
                    {
                        Point p = searchSpace.Pop();
                        if (!pixelChecked.TryGetValue(p, out bool val) || !val)
                        {
                            pixelChecked[p] = true;
                            for (int xOff = -2; xOff <= 2; xOff++)
                            {
                                for (int yOff = -2; yOff <= 2; yOff++)
                                {
                                    if (p.X + xOff > 0 && p.X + xOff < imgWidth && p.Y + yOff > 0 &&
                                        p.Y + yOff < imgHeight)
                                    {
                                        index = 4 * (p.X + xOff + (p.Y + yOff) * imgWidth);
                                        if (LockedBitmapBytes[index] == 0 && LockedBitmapBytes[index + 1] == 0 &&
                                            LockedBitmapBytes[index + 2] == 0 && LockedBitmapBytes[index + 3] == 255)
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
                    int lowest = yCenterNew + 1000;
                    int highest = yCenterNew - 1000;
                    for (int yOff = -5; yOff < 5; yOff++)
                    {
                        int checkY = yCenterNew + yOff;
                        if (checkY > 0 && checkY < imgHeight)
                        {
                            index = 4 * (xCenterNew + (checkY) * imgWidth);
                            if (LockedBitmapBytes[index] == 0 && LockedBitmapBytes[index + 1] == 0 &&
                                LockedBitmapBytes[index + 2] == 0 && LockedBitmapBytes[index + 3] == 255)
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
                    numbytes = Math.Abs(lockedBitmapData.Stride) * lockedBitmapData.Height;
                    LockedBitmapBytes = new byte[numbytes]; //Format is ARGB, in order BGRA
                    Marshal.Copy(lockedBitmapData.Scan0, LockedBitmapBytes, 0, numbytes);
                    cloneBitmapColoured.UnlockBits(lockedBitmapData);

                    //search diagonally from second-pass center for colours frequently occuring 3 pixels in a row horizontally. Most common one of these should be the "amount label background colour"
                    Queue<Point> pointsToCheck = new Queue<Point>();
                    Dictionary<Color, int> colorHits = new Dictionary<Color, int>();
                    pointsToCheck.Enqueue(new Point(xCenterNew, yCenterNew + 1));
                    pointsToCheck.Enqueue(new Point(xCenterNew, yCenterNew - 1));
                    bool stop = false;
                    while (pointsToCheck.Count > 0)
                    {
                        Point p = pointsToCheck.Dequeue();
                        int offset = (p.Y > yCenter ? 1 : -1);
                        if (p.X + 3 > Width || p.X - 3 < 0 || p.Y + 3 > imgHeight || p.Y - 3 < 0)
                        {
                            stop = true; //keep going until we almost hit the edge of the image
                        }

                        if (!stop)
                        {
                            pointsToCheck.Enqueue(new Point(p.X + offset, p.Y + offset));
                        }

                        index = 4 * (p.X + p.Y * imgWidth);
                        if (LockedBitmapBytes[index] == LockedBitmapBytes[index - 4] &&
                            LockedBitmapBytes[index] == LockedBitmapBytes[index + 4]
                            && LockedBitmapBytes[index + 1] == LockedBitmapBytes[index + 1 - 4] &&
                            LockedBitmapBytes[index + 1] == LockedBitmapBytes[index + 1 + 4]
                            && LockedBitmapBytes[index + 2] == LockedBitmapBytes[index + 2 - 4] &&
                            LockedBitmapBytes[index + 2] == LockedBitmapBytes[index + 2 + 4]
                            && LockedBitmapBytes[index + 3] == LockedBitmapBytes[index + 3 - 4] &&
                            LockedBitmapBytes[index + 3] == LockedBitmapBytes[index + 3 + 4])
                        {
                            Color color = Color.FromArgb(LockedBitmapBytes[index + 3], LockedBitmapBytes[index + 2],
                                LockedBitmapBytes[index + 1], LockedBitmapBytes[index]);
                            if (!colorHits.TryAdd(color, 1))
                            {
                                colorHits[color]++;
                            }
                        }
                    }

                    Color topColor = Color.FromArgb(255, 255, 255, 255);
                    int topColorScore = 0;
                    foreach (Color key in colorHits.Keys)
                    {
                        if (colorHits[key] > topColorScore)
                        {
                            topColor = key;
                            topColorScore = colorHits[key];
                        }
                        //Debug.WriteLine("Color: " + key.ToString() + ", Value: " + colorHits[key]);
                    }

                    Debug.WriteLine("Top Color: " + topColor.ToString() + ", Value: " + topColorScore);

                    if (topColor == Color.FromArgb(255, 255, 255, 255))
                        continue; //if most common colour is our default value, ignore and move on

                    //get unfilteredImage as array for fast access
                    imgWidth = unfilteredImage.Width;
                    lockedBitmapData = unfilteredImage.LockBits(new Rectangle(0, 0, imgWidth, unfilteredImage.Height),
                        ImageLockMode.WriteOnly, unfilteredImage.PixelFormat);
                    numbytes = Math.Abs(lockedBitmapData.Stride) * lockedBitmapData.Height;
                    LockedBitmapBytes = new byte[numbytes]; //Format is ARGB, in order BGRA
                    Marshal.Copy(lockedBitmapData.Scan0, LockedBitmapBytes, 0, numbytes);
                    unfilteredImage.UnlockBits(lockedBitmapData);

                    //recalculate centers to be relative to whole image
                    rightmost = rightmost + Left + 1;
                    xCenter += Left;
                    yCenter += Top;
                    xCenterNew += Left;
                    yCenterNew += Top;
                    Debug.WriteLine("Old Center" + xCenter + ", " + yCenter);
                    Debug.WriteLine("New Center" + xCenterNew + ", " + yCenterNew);

                    //search diagonally (toward top-right) from second-pass center until we find the "amount label" colour
                    x = xCenterNew;
                    y = yCenterNew;
                    index = 4 * (x + y * imgWidth);
                    Color currColor = Color.FromArgb(LockedBitmapBytes[index + 3], LockedBitmapBytes[index + 2],
                        LockedBitmapBytes[index + 1], LockedBitmapBytes[index]);
                    while (x < imgWidth && y > 0 && topColor != currColor)
                    {
                        x++;
                        y--;
                        index = 4 * (x + y * imgWidth);
                        currColor = Color.FromArgb(LockedBitmapBytes[index + 3], LockedBitmapBytes[index + 2],
                            LockedBitmapBytes[index + 1], LockedBitmapBytes[index]);
                    }

                    //then search for top edge
                    Top = y;
                    while (topColor == Color.FromArgb(LockedBitmapBytes[index + 3], LockedBitmapBytes[index + 2],
                               LockedBitmapBytes[index + 1], LockedBitmapBytes[index]))
                    {
                        Top--;
                        index = 4 * (x + Top * imgWidth);
                    }

                    Top += 2;
                    index = 4 * (x + Top * imgWidth);

                    //search for left edge
                    Left = x;
                    while (topColor == Color.FromArgb(LockedBitmapBytes[index + 3], LockedBitmapBytes[index + 2],
                               LockedBitmapBytes[index + 1], LockedBitmapBytes[index]))
                    {
                        Left--;
                        index = 4 * (Left + Top * imgWidth);
                    }

                    Left += 2;
                    index = 4 * (Left + Top * imgWidth);

                    //search for height (bottom edge)
                    Height = 0;
                    while (topColor == Color.FromArgb(LockedBitmapBytes[index + 3], LockedBitmapBytes[index + 2],
                               LockedBitmapBytes[index + 1], LockedBitmapBytes[index]))
                    {
                        Height++;
                        index = 4 * (Left + (Top + Height) * imgWidth);
                    }

                    Height -= 2;

                    Left = rightmost; // cut out checkmark+circle icon
                    index = 4 * (Left + (Top + Height) * imgWidth);

                    //search for width
                    Width = 0;
                    while (topColor == Color.FromArgb(LockedBitmapBytes[index + 3], LockedBitmapBytes[index + 2],
                               LockedBitmapBytes[index + 1], LockedBitmapBytes[index]))
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
                    g.DrawRectangle(cyan, cloneRect);

                    //do OCR
                    using (var page = _tesseractService.FirstEngine.Process(cloneBitmap, PageSegMode.SingleLine))
                    {
                        using (var iterator = page.GetIterator())
                        {
                            iterator.Begin();
                            var rawText = iterator.GetText(PageIteratorLevel.TextLine);
                            rawText = rawText?.Replace(" ", string.Empty);
                            //if no number found, 1 of item
                            if (!int.TryParse(rawText, out int itemCount))
                            {
                                itemCount = 1;
                            }

                            g.DrawString(rawText, font, Brushes.Cyan, new Point(cloneRect.X, cloneRect.Y));

                            //find what item the item belongs to
                            Rectangle itemLabel = new Rectangle(columns[j].X, rows[i].Top, columns[j].Width,
                                rows[i].Height);
                            g.DrawRectangle(cyan, itemLabel);
                            for (int k = 0; k < foundItems.Count; k++)
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

        darkCyan.Dispose();
        red.Dispose();
        cyan.Dispose();
    }

    /// <summary>
    /// Process the profile screen to find owned items
    /// </summary>
    /// <param name="fullShot">Image to scan</param>
    internal static void ProcessProfileScreen(Bitmap fullShot)
    {
        long start = Stopwatch.GetTimestamp();

        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff", Main.Culture);
        fullShot.Save(Path.Combine(ApplicationConstants.AppPathDebug, $"ProfileImage {timestamp}.png"));
        List<InventoryItem> foundParts = FindOwnedItems(fullShot, timestamp, in start);
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
                string[] nameParts = name.Split(["Prime"], 2, StringSplitOptions.None);
                string primeName = nameParts[0] + "Prime";

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

        Main.DataBase.SaveAllJSONs();
        Main.RunOnUIThread(() =>
        {
            EquipmentWindow.INSTANCE.ReloadItems();
        });

        var end = Stopwatch.GetElapsedTime(start);
        if (end < TimeSpan.FromSeconds(10))
        {
            Main.StatusUpdate($"Completed Profile Scanning({end})", 0);
        }
        else
        {
            Main.StatusUpdate($"Lower brightness may increase speed({end})", 1);
        }
    }

    /// <summary>
    /// Probe pixel color to see if it's white enough for FindOwnedItems
    /// </summary>
    /// <param name="byteArr">Byte Array of image (ARGB)</param>
    /// <param name="width">Width of image</param>
    /// <param name="x">Pixel X coordiante</param>
    /// <param name="y">Pixel Y coordinate</param>
    /// <param name="lowSensitivity">Use lower threshold, mainly for finding black pixels instead</param>
    /// <returns>if pixel is above threshold for "white"</returns>
    private static bool probeProfilePixel(byte[] byteArr, int width, int x, int y, bool lowSensitivity)
    {
        int a = byteArr[(x + y * width) * 4 + 3]; //4 bytes for ARGB, in order BGRA in the array
        int r = byteArr[(x + y * width) * 4 + 2];
        int g = byteArr[(x + y * width) * 4 + 1];
        int b = byteArr[(x + y * width) * 4];

        if (lowSensitivity)
        {
            return a > 80 && r > 80 && g > 80 && b > 80;
        }

        return a > 240 && r > 200 && g > 200 && b > 200;
    }

    /// <summary>
    /// Get owned items from profile screen
    /// </summary>
    /// <param name="ProfileImage">Image of profile screen to scan, debug markings will be drawn on this</param>
    /// <param name="timestamp">Time started at, used for file name</param>
    /// <returns>List of found items</returns>
    private static List<InventoryItem> FindOwnedItems(Bitmap ProfileImage, string timestamp, in long start)
    {
        Pen orange = new Pen(Brushes.Orange);
        Pen red = new Pen(Brushes.Red);
        Pen cyan = new Pen(Brushes.Cyan);
        Pen pink = new Pen(Brushes.Pink);
        Pen darkCyan = new Pen(Brushes.DarkCyan);
        var font = new Font("Arial", 16);
        List<InventoryItem> foundItems = [];
        Bitmap ProfileImageClean = new Bitmap(ProfileImage);
        int probe_interval = ProfileImage.Width / 120;
        Logger.Debug("Using probe interval: {Interval}", probe_interval);

        int imgWidth = ProfileImageClean.Width;
        BitmapData lockedBitmapData = ProfileImageClean.LockBits(
            new Rectangle(0, 0, imgWidth, ProfileImageClean.Height), ImageLockMode.WriteOnly,
            ProfileImageClean.PixelFormat);
        int numbytes = Math.Abs(lockedBitmapData.Stride) * lockedBitmapData.Height;
        byte[] LockedBitmapBytes = new byte[numbytes]; //Format is ARGB, in order BGRA
        Marshal.Copy(lockedBitmapData.Scan0, LockedBitmapBytes, 0, numbytes);

        using (Graphics g = Graphics.FromImage(ProfileImage))
        {
            int nextY = 0;
            int nextYCounter = -1;
            List<Tuple<int, int, int>> skipZones = []; //left edge, right edge, bottom edge
            for (int y = 0; y < ProfileImageClean.Height - 1; y = (nextYCounter == 0 ? nextY : y + 1))
            {
                for (int x = 0; x < imgWidth; x += probe_interval) //probe every few pixels for performance
                {
                    if (probeProfilePixel(LockedBitmapBytes, imgWidth, x, y, false))
                    {
                        //find left edge and check that the coloured area is at least as big as probe_interval
                        int leftEdge = -1;
                        int hits = 0;
                        int areaWidth = 0;
                        double hitRatio = 0;
                        for (int tempX = Math.Max(x - probe_interval, 0);
                             tempX < Math.Min(x + probe_interval, imgWidth);
                             tempX++)
                        {
                            areaWidth++;
                            if (probeProfilePixel(LockedBitmapBytes, imgWidth, tempX, y, false))
                            {
                                hits++;
                                leftEdge = (leftEdge == -1 ? tempX : leftEdge);
                            }
                        }

                        hitRatio = (double)(hits) / areaWidth;
                        if (hitRatio < 0.5) //skip if too low hit ratio
                        {
                            g.DrawLine(orange, x - probe_interval, y, x + probe_interval, y);
                            continue;
                        }

                        //find where the line ends
                        int rightEdge = leftEdge;
                        while (rightEdge + 2 < imgWidth &&
                               (probeProfilePixel(LockedBitmapBytes, imgWidth, rightEdge + 1, y, false)
                                || probeProfilePixel(LockedBitmapBytes, imgWidth, rightEdge + 2, y, false)))
                        {
                            rightEdge++;
                        }

                        //check that it isn't in an area already thoroughly searched
                        bool failed = false;
                        foreach (Tuple<int, int, int> skipZone in skipZones)
                        {
                            if (y < skipZone.Item3 && ((leftEdge <= skipZone.Item1 && rightEdge >= skipZone.Item1) ||
                                                       (leftEdge >= skipZone.Item1 && leftEdge <= skipZone.Item2) ||
                                                       (rightEdge >= skipZone.Item1 && rightEdge <= skipZone.Item2)))
                            {
                                g.DrawLine(darkCyan, leftEdge, y, rightEdge, y);
                                x = Math.Max(x, skipZone.Item2);
                                failed = true;
                                break;
                            }
                        }

                        if (failed)
                        {
                            continue;
                        }


                        //find bottom edge and hit ratio of all rows
                        int topEdge = y;
                        int bottomEdge = y;
                        List<double> hitRatios =
                        [
                            1
                        ];
                        do
                        {
                            int rightMostHit = 0;
                            int leftMostHit = -1;
                            hits = 0;
                            bottomEdge++;
                            for (int i = leftEdge; i < rightEdge; i++)
                            {
                                if (probeProfilePixel(LockedBitmapBytes, imgWidth, i, bottomEdge, false))
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
                                g.DrawLine(red, rightEdge, bottomEdge, rightMostHit, bottomEdge);
                                rightEdge = rightMostHit;
                                bottomEdge = y;
                                hitRatios.Clear();
                                hitRatios.Add(1);
                            }

                            if (hitRatio > 0.2 && leftMostHit > leftEdge &&
                                rightEdge - leftEdge >
                                100) //make sure the innermost left edge is used (avoid bright part of frame overlapping with edge)
                            {
                                g.DrawLine(red, leftEdge, bottomEdge, leftMostHit, bottomEdge);
                                leftEdge = leftMostHit;
                                bottomEdge = y;
                                hitRatios.Clear();
                                hitRatios.Add(1);
                            }
                        } while (bottomEdge + 2 < ProfileImageClean.Height && hitRatios[^1] > 0.2);

                        hitRatios.RemoveAt(hitRatios.Count - 1);
                        //find if/where it transitions from text (some misses) to no text (basically no misses) then back to text (some misses). This is proof it's an owned item and marks the bottom edge of the text
                        int ratioChanges = 0;
                        bool prevMostlyHits = true;
                        int lineBreak = -1;
                        for (int i = 0; i < hitRatios.Count; i++)
                        {
                            if ((hitRatios[i] > 0.99) != prevMostlyHits)
                            {
                                if (ratioChanges == 1)
                                {
                                    lineBreak = i + 1;
                                    g.DrawLine(cyan, rightEdge, topEdge + lineBreak, leftEdge, topEdge + lineBreak);
                                }

                                prevMostlyHits = !prevMostlyHits;
                                ratioChanges++;
                            }
                        }

                        int width = rightEdge - leftEdge;
                        int height = bottomEdge - topEdge;

                        if (ratioChanges != 4 || width < 2.4 * height || width > 4 * height)
                        {
                            g.DrawRectangle(pink, leftEdge, topEdge, width, height);
                            x = Math.Max(rightEdge, x);
                            if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(10))
                            {
                                Main.StatusUpdate("High noise, this might be slow", 3);
                            }

                            continue;
                        }

                        g.DrawRectangle(red, leftEdge, topEdge, width, height);
                        skipZones.Add(new Tuple<int, int, int>(leftEdge, rightEdge, bottomEdge));
                        x = rightEdge;
                        nextY = bottomEdge + 1;
                        nextYCounter = Math.Max(height / 8, 3);

                        height = lineBreak;

                        Rectangle cloneRect = new Rectangle(leftEdge, topEdge, width, height);
                        Bitmap cloneBitmap = new Bitmap(cloneRect.Width * 3, cloneRect.Height);
                        using (Graphics g2 = Graphics.FromImage(cloneBitmap))
                        {
                            g2.FillRectangle(Brushes.White, 0, 0, cloneBitmap.Width, cloneBitmap.Height);
                        }

                        int offset = 0;
                        bool prevHit = false;
                        for (int i = 0; i < cloneRect.Width; i++)
                        {
                            bool hitSomething = false;
                            for (int j = 0; j < cloneRect.Height; j++)
                            {
                                if (!probeProfilePixel(LockedBitmapBytes, imgWidth, cloneRect.X + i, cloneRect.Y + j,
                                        true))
                                {
                                    cloneBitmap.SetPixel(i + offset, j, Color.Black);
                                    ProfileImage.SetPixel(cloneRect.X + i, cloneRect.Y + j, Color.Red);
                                    hitSomething = true;
                                }
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
                                string rawText = iterator.GetText(PageIteratorLevel.TextLine);
                                rawText = Regex.Replace(rawText, @"\s", string.Empty);
                                foundItems.Add(new InventoryItem(rawText, cloneRect));

                                g.FillRectangle(Brushes.LightGray, cloneRect.X, cloneRect.Y + cloneRect.Height,
                                    cloneRect.Width, cloneRect.Height);
                                g.DrawString(rawText, font, Brushes.DarkBlue,
                                    new Point(cloneRect.X, cloneRect.Y + cloneRect.Height));
                            }
                        }

                        _tesseractService.FirstEngine.SetVariable("tessedit_char_whitelist", string.Empty);
                    }
                }

                if (nextYCounter >= 0)
                {
                    nextYCounter--;
                }
            }
        }

        ProfileImageClean.Dispose();
        ProfileImage.Save(Path.Combine(ApplicationConstants.AppPathDebug, $"ProfileImageBounds {timestamp}.png"));
        darkCyan.Dispose();
        pink.Dispose();
        cyan.Dispose();
        red.Dispose();
        orange.Dispose();
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

        switch (theme)
        {
            case WFtheme.VITRUVIAN: // TO CHECK
                return Math.Abs(test.GetHue() - primary.GetHue()) < 4 && test.GetSaturation() >= 0.25 &&
                       test.GetBrightness() >= 0.42;
            case WFtheme.LOTUS:
                return Math.Abs(test.GetHue() - primary.GetHue()) < 5 && test.GetSaturation() >= 0.65 &&
                       Math.Abs(test.GetBrightness() - primary.GetBrightness()) <= 0.1
                       || (Math.Abs(test.GetHue() - secondary.GetHue()) < 15 && test.GetBrightness() >= 0.65);
            case WFtheme.OROKIN: // TO CHECK
                return (Math.Abs(test.GetHue() - primary.GetHue()) < 5 && test.GetBrightness() <= 0.42 &&
                        test.GetSaturation() >= 0.1)
                       || (Math.Abs(test.GetHue() - secondary.GetHue()) < 5 && test.GetBrightness() <= 0.5 &&
                           test.GetBrightness() >= 0.25 && test.GetSaturation() >= 0.25);
            case WFtheme.STALKER:
                return ((Math.Abs(test.GetHue() - primary.GetHue()) < 4 && test.GetSaturation() >= 0.55)
                        || (Math.Abs(test.GetHue() - secondary.GetHue()) < 4 && test.GetSaturation() >= 0.66)) &&
                       test.GetBrightness() >= 0.25;
            case WFtheme.CORPUS:
                return Math.Abs(test.GetHue() - primary.GetHue()) < 3 && test.GetBrightness() >= 0.42 &&
                       test.GetSaturation() >= 0.35;
            case WFtheme.EQUINOX:
                return test.GetSaturation() <= 0.2 && test.GetBrightness() >= 0.55;
            case WFtheme.DARK_LOTUS:
                return (Math.Abs(test.GetHue() - secondary.GetHue()) < 20 && test.GetBrightness() >= 0.35 &&
                        test.GetBrightness() <= 0.55 && test.GetSaturation() <= 0.25 && test.GetSaturation() >= 0.05)
                       || (Math.Abs(test.GetHue() - secondary.GetHue()) < 4 && test.GetBrightness() >= 0.50 &&
                           test.GetSaturation() >= 0.20);
            case WFtheme.FORTUNA:
                return ((Math.Abs(test.GetHue() - primary.GetHue()) < 3 && test.GetBrightness() >= 0.35) ||
                        (Math.Abs(test.GetHue() - secondary.GetHue()) < 4 && test.GetBrightness() >= 0.15)) &&
                       test.GetSaturation() >= 0.20;
            case WFtheme.HIGH_CONTRAST:
                return (Math.Abs(test.GetHue() - primary.GetHue()) < 3 ||
                        Math.Abs(test.GetHue() - secondary.GetHue()) < 2) && test.GetSaturation() >= 0.75 &&
                       test.GetBrightness() >= 0.35; // || Math.Abs(test.GetHue() - secondary.GetHue()) < 2;
            case WFtheme.LEGACY:                     // TO CHECK
                return (test.GetBrightness() >= 0.65)
                       || (Math.Abs(test.GetHue() - secondary.GetHue()) < 6 && test.GetBrightness() >= 0.5 &&
                           test.GetSaturation() >= 0.5);
            case WFtheme.NIDUS:
                return (Math.Abs(test.GetHue() - (primary.GetHue() + 6)) < 8 && test.GetSaturation() >= 0.30)
                       || (Math.Abs(test.GetHue() - secondary.GetHue()) < 15 && test.GetSaturation() >= 0.55);
            case WFtheme.TENNO:
                return (Math.Abs(test.GetHue() - primary.GetHue()) < 3 ||
                        Math.Abs(test.GetHue() - secondary.GetHue()) < 2) && test.GetSaturation() >= 0.38 &&
                       test.GetBrightness() <= 0.55;
            case WFtheme.BARUUK:
                return (Math.Abs(test.GetHue() - primary.GetHue()) < 2) && test.GetSaturation() > 0.25 &&
                       test.GetBrightness() > 0.5;
            case WFtheme.GRINEER:
                return (Math.Abs(test.GetHue() - primary.GetHue()) < 5 && test.GetBrightness() > 0.5)
                       || (Math.Abs(test.GetHue() - secondary.GetHue()) < 6 && test.GetBrightness() > 0.55);
            case WFtheme.ZEPHYR:
                return ((Math.Abs(test.GetHue() - primary.GetHue()) < 4 && test.GetSaturation() >= 0.55)
                        || (Math.Abs(test.GetHue() - secondary.GetHue()) < 4 && test.GetSaturation() >= 0.66)) &&
                       test.GetBrightness() >= 0.25;
            default:
                // This shouldn't be ran
                //   Only for initial testing
                return Math.Abs(test.GetHue() - primary.GetHue()) < 2 ||
                       Math.Abs(test.GetHue() - secondary.GetHue()) < 2;
        }
    }

    public static Bitmap ScaleUpAndFilter(Bitmap image, WFtheme active, out int[] rowHits, out int[] colHits)
    {
        if (image.Height <= SCALING_LIMIT)
        {
            partialScreenshotExpanded = new Bitmap(image.Width * SCALING_LIMIT / image.Height, SCALING_LIMIT);
            partialScreenshotExpanded.SetResolution(image.HorizontalResolution, image.VerticalResolution);

            using (Graphics graphics = Graphics.FromImage(partialScreenshotExpanded))
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
        BitmapData lockedBitmapData = filtered.LockBits(new Rectangle(0, 0, filtered.Width, filtered.Height),
            ImageLockMode.ReadWrite, filtered.PixelFormat);
        int numbytes = Math.Abs(lockedBitmapData.Stride) * lockedBitmapData.Height;
        byte[] LockedBitmapBytes = new byte[numbytes];
        Marshal.Copy(lockedBitmapData.Scan0, LockedBitmapBytes, 0, numbytes);
        int PixelSize = 4; //ARGB, order in array is BGRA
        for (int i = 0; i < numbytes; i += PixelSize)
        {
            var clr = Color.FromArgb(LockedBitmapBytes[i + 3], LockedBitmapBytes[i + 2], LockedBitmapBytes[i + 1],
                LockedBitmapBytes[i]);
            if (ThemeThresholdFilter(clr, active))
            {
                LockedBitmapBytes[i] = 0;
                LockedBitmapBytes[i + 1] = 0;
                LockedBitmapBytes[i + 2] = 0;
                LockedBitmapBytes[i + 3] = 255;
                //Black
                int x = (i / PixelSize) % filtered.Width;
                int y = (i / PixelSize - x) / filtered.Width;
                rowHits[y]++;
                colHits[x]++;
            }
            else
            {
                LockedBitmapBytes[i] = 255;
                LockedBitmapBytes[i + 1] = 255;
                LockedBitmapBytes[i + 2] = 255;
                LockedBitmapBytes[i + 3] = 255;
                //White
            }
        }

        Marshal.Copy(LockedBitmapBytes, 0, lockedBitmapData.Scan0, numbytes);
        filtered.UnlockBits(lockedBitmapData);
        return filtered;
    }

    // The parts of text
    // The top bit (upper case and dots/strings, bdfhijklt) > the juicy bit (lower case, acemnorsuvwxz) > the tails (gjpqy)
    // we ignore the "tippy top" because it has a lot of variance, so we just look at the "bottom half of the top"
    private static readonly int[] TextSegments = [2, 4, 16, 21];

    private static IEnumerable<Bitmap> ExtractPartBoxAutomatically(
        out double scaling, out WFtheme active,
        Bitmap fullScreen)
    {
        var start = Stopwatch.GetTimestamp();
        long beginning = start;

        int lineHeight = (int)(pixelRewardLineHeight / 2 * _window.ScreenScaling);

        int width = _window.Window.Width;
        int height = _window.Window.Height;
        int mostWidth = (int)(pixleRewardWidth * _window.ScreenScaling);
        int mostLeft = (width / 2) - (mostWidth / 2);
        // Most Top = pixleRewardYDisplay - pixleRewardHeight + pixelRewardLineHeight
        //                   (316          -        235        +       44)    *    1.1    =    137
        int mostTop = height / 2 -
                      (int)((pixleRewardYDisplay - pixleRewardHeight + pixelRewardLineHeight) * _window.ScreenScaling);
        int mostBot = height / 2 - (int)((pixleRewardYDisplay - pixleRewardHeight) * _window.ScreenScaling * 0.5);
        //Bitmap postFilter = new Bitmap(mostWidth, mostBot - mostTop);
        var rectangle = new Rectangle((int)(mostLeft), (int)(mostTop), mostWidth, mostBot - mostTop);
        Bitmap preFilter;

        try
        {
            Logger.Debug(
                $"Fullscreen is {fullScreen.Size}:, trying to clone: {rectangle.Size} at {rectangle.Location}");
            preFilter = fullScreen.Clone(new Rectangle(mostLeft, mostTop, mostWidth, mostBot - mostTop),
                fullScreen.PixelFormat);
        }
        catch (Exception ex)
        {
            Logger.Debug("Something went wrong with getting the starting image: " + ex.ToString());
            throw;
        }

        var end = Stopwatch.GetElapsedTime(start);
        Logger.Debug("Grabbed images " + end);
        start = Stopwatch.GetTimestamp();

        active = GetThemeWeighted(out var closest, fullScreen);

        end = Stopwatch.GetElapsedTime(start);
        Logger.Debug("Got theme " + end);
        start = Stopwatch.GetTimestamp();

        int[] rows = new int[preFilter.Height];
        // 0 => 50   27 => 77   50 => 100

        //Logger.Debug("ROWS: 0 to " + preFilter.Height);
        //var postFilter = preFilter;
        for (int y = 0; y < preFilter.Height; y++)
        {
            rows[y] = 0;
            for (int x = 0; x < preFilter.Width; x++)
            {
                var clr = preFilter.GetPixel(x, y);
                if (ThemeThresholdFilter(clr, active))
                    //{
                    rows[y]++;
                //postFilter.SetPixel(x, y, Color.Black);
                //} else
                //postFilter.SetPixel(x, y, Color.White);
            }
            //Debug.Write(rows[y] + " ");
        }

        //postFilter.Save(ApplicationConstants.AppPath + @"\Debug\PostFilter" + timestamp + ".png");

        end = Stopwatch.GetElapsedTime(start);
        Logger.Debug("Filtered Image " + end);
        start = Stopwatch.GetTimestamp();

        double[] percWeights = new double[51];
        double[] topWeights = new double[51];
        double[] midWeights = new double[51];
        double[] botWeights = new double[51];

        int topLine_100 = preFilter.Height - lineHeight;
        int topLine_50 = lineHeight / 2;

        scaling = -1;
        double lowestWeight = 0;
        Rectangle uidebug = new Rectangle((topLine_100 - topLine_50) / 50 + topLine_50,
            (int)(preFilter.Height / _window.ScreenScaling), preFilter.Width, 50);
        for (int i = 0; i <= 50; i++)
        {
            int yFromTop = preFilter.Height - (i * (topLine_100 - topLine_50) / 50 + topLine_50);

            int scale = (50 + i);
            int scaleWidth = preFilter.Width * scale / 100;

            int textTop = (int)(_window.ScreenScaling * TextSegments[0] * scale / 100);
            int textTopBot = (int)(_window.ScreenScaling * TextSegments[1] * scale / 100);
            int textBothBot = (int)(_window.ScreenScaling * TextSegments[2] * scale / 100);
            int textTailBot = (int)(_window.ScreenScaling * TextSegments[3] * scale / 100);

            int loc = textTop;
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

        Logger.Debug("Got scaling " + end);

        int[] topFive = [-1, -1, -1, -1, -1];

        for (int i = 0; i <= 50; i++)
        {
            int match = 4;
            while (match != -1 && topFive[match] != -1 && percWeights[i] > percWeights[topFive[match]])
                match--;

            if (match != -1)
            {
                for (int move = 0; move < match; move++)
                    topFive[move] = topFive[move + 1];
                topFive[match] = i;
            }
        }

        for (int i = 0; i < 5; i++)
        {
            Logger.Debug("RANK " + (5 - i) + " SCALE: " + (topFive[i] + 50) + "%\t\t" +
                         percWeights[topFive[i]].ToString("F2", Main.Culture) + " -- " +
                         topWeights[topFive[i]].ToString("F2", Main.Culture) + ", " +
                         midWeights[topFive[i]].ToString("F2", Main.Culture) + ", " +
                         botWeights[topFive[i]].ToString("F2", Main.Culture));
        }

        using (Graphics g = Graphics.FromImage(fullScreen))
        {
            g.DrawRectangle(Pens.Red, rectangle);
            g.DrawRectangle(Pens.Chartreuse, uidebug);
        }

        fullScreen.Save(ApplicationConstants.AppPath + @"\Debug\BorderScreenshot " + timestamp + ".png");


        //postFilter.Save(ApplicationConstants.AppPath + @"\Debug\DebugBox1 " + timestamp + ".png");
        preFilter.Save(ApplicationConstants.AppPath + @"\Debug\FullPartArea " + timestamp + ".png");
        scaling = topFive[4] +
                  50; //scaling was sometimes going to 50 despite being set to 100, so taking the value from above that seems to be accurate.

        scaling /= 100;
        double highScaling = scaling < 1.0 ? scaling + 0.01 : scaling;
        double lowScaling = scaling > 0.5 ? scaling - 0.01 : scaling;

        int cropWidth = (int)(pixleRewardWidth * _window.ScreenScaling * highScaling);
        int cropLeft = (preFilter.Width / 2) - (cropWidth / 2);
        int cropTop = height / 2 - (int)((pixleRewardYDisplay - pixleRewardHeight + pixelRewardLineHeight) *
                                         _window.ScreenScaling * highScaling);
        int cropBot = height / 2 -
                      (int)((pixleRewardYDisplay - pixleRewardHeight) * _window.ScreenScaling * lowScaling);
        int cropHei = cropBot - cropTop;
        cropTop -= mostTop;
        try
        {
            Rectangle rect = new Rectangle(cropLeft, cropTop, cropWidth, cropHei);
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
        Logger.Debug("Finished function " + end);
        partialScreenshot.Save(ApplicationConstants.AppPath + @"\Debug\PartialScreenshot" + timestamp + ".png");
        return FilterAndSeparatePartsFromPartBox(partialScreenshot, active);
    }

    private static IEnumerable<Bitmap> FilterAndSeparatePartsFromPartBox(Bitmap partBox, WFtheme active)
    {
        double weight = 0;
        double totalEven = 0;
        double totalOdd = 0;

        int width = partBox.Width;
        int height = partBox.Height;
        int[] counts = new int[height];
        Bitmap filtered = new Bitmap(width, height);
        for (int x = 0; x < width; x++)
        {
            int count = 0;
            for (int y = 0; y < height; y++)
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
            double sinVal = Math.Cos(8 * x * Math.PI / partBox.Width);
            sinVal = sinVal * sinVal * sinVal;
            weight += sinVal * count;

            if (sinVal < 0)
                totalEven -= sinVal * count;
            else if (sinVal > 0)
                totalOdd += sinVal * count;
        }

        // Rarely, the selection box on certain themes can get included in the detected reward area.
        // Therefore, we check the bottom 10% of the image for this potential issue
        for (int y = height - 1; y > height * 0.9; --y)
        {
            // Assumed to be this issue if both the following criteria are met:
            // 1. A lot more black pixels than the next line up, going with 5x for the moment. The issue is almost entirely on a single line in the cases I've seen so far
            // 2. The problematic line should have a meaningful amount of black pixels. At least twice the height should be pretty good. (We don't yet know the number of players, so can't directly base it on width)
            if (counts[y] > 5 * counts[y - 1] && counts[y] > height * 2)
            {
                Bitmap tmp = filtered.Clone(new Rectangle(0, 0, width, y), filtered.PixelFormat);
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
                    1);
            });
            processingActive = false;
            throw new Exception("Unable to find any parts");
        }

        double total = totalEven + totalOdd;
        Logger.Debug("EVEN DISTRIBUTION: " + (totalEven / total * 100).ToString("F2", Main.Culture) + "%");
        Logger.Debug("ODD DISTRIBUTION: " + (totalOdd / total * 100).ToString("F2", Main.Culture) + "%");

        int boxWidth = partBox.Width / 4;
        int boxHeight = filtered.Height;
        Rectangle destRegion = new Rectangle(0, 0, boxWidth, boxHeight);

        int currLeft = 0;
        int playerCount = 4;

        if (totalOdd > totalEven)
        {
            currLeft = boxWidth / 2;
            playerCount = 3;
        }

        for (int i = 0; i < playerCount; i++)
        {
            Rectangle srcRegion = new Rectangle(currLeft + i * boxWidth, 0, boxWidth, boxHeight);
            Bitmap newBox = new Bitmap(boxWidth, boxHeight);
            using (Graphics grD = Graphics.FromImage(newBox))
                grD.DrawImage(filtered, destRegion, srcRegion, GraphicsUnit.Pixel);
            newBox.Save(ApplicationConstants.AppPath + @"\Debug\PartBox(" + i + ") " + timestamp + ".png");
            yield return newBox;
        }

        filtered.Dispose();
    }

    private static string GetTextFromImage(Bitmap image, TesseractEngine engine)
    {
        string ret = string.Empty;
        using (Page page = engine.Process(image))
        {
            var text = page.GetText();
            var s = text.AsSpan().Trim();
            ret = s.ToString();
        }

        return RE.Replace(ret, string.Empty).Trim();
    }

    internal static Bitmap CaptureScreenshot()
    {
        _window.UpdateWindow();

        // HACK: Should be already injected instead of switching here
        IScreenshotService screenshot;
        if (_windowsScreenshot == null)
        {
            // W8.1 and lower
            screenshot = _gdiScreenshot;
        }
        else
        {
            switch (_settings.HdrSupport)
            {
                case HdrSupportEnum.On:
                    Logger.Debug("Using new windows capture API for an HDR screenshot");
                    screenshot = _windowsScreenshot;
                    break;
                case HdrSupportEnum.Off:
                    Logger.Debug("Using old GDI service for a SDR screenshot");
                    screenshot = _gdiScreenshot;
                    break;
                case HdrSupportEnum.Auto:
                    var isHdr = _hdrDetector.IsHdr();
                    Logger.Debug($"Automatically determining HDR status: {isHdr} | Using corresponding service");
                    screenshot = isHdr ? _windowsScreenshot : _gdiScreenshot;
                    break;
                default:
                    throw new NotImplementedException(
                        $"HDR support option '{_settings.HdrSupport}' does not have a corresponding screenshot service.");
            }
        }

        var image = screenshot.CaptureScreenshot().GetAwaiter().GetResult()[0];
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff", Main.Culture);
        var fileName = Path.Combine(ApplicationConstants.AppPath, "Debug", $"FullScreenShot {date}.png");
        image.Save(fileName);
        return image;
    }

    internal static void SnapScreenshot()
    {
        Main.SnapItOverlayWindow.Populate(CaptureScreenshot());
        Main.SnapItOverlayWindow.Left = _window.Window.Left / _window.DpiScaling;
        Main.SnapItOverlayWindow.Top = _window.Window.Top / _window.DpiScaling;
        Main.SnapItOverlayWindow.Width = _window.Window.Width / _window.DpiScaling;
        Main.SnapItOverlayWindow.Height = _window.Window.Height / _window.DpiScaling;
        Main.SnapItOverlayWindow.Topmost = true;
        Main.SnapItOverlayWindow.Focusable = true;
        Main.SnapItOverlayWindow.Show();
        Main.SnapItOverlayWindow.Focus();
    }

    public static async Task updateEngineAsync()
    {
        _tesseractService.ReloadEngines();
    }

    [GeneratedRegex("[^a-z가-힣]", RegexOptions.IgnoreCase | RegexOptions.Compiled, "da-DK")]
    private static partial Regex WordTrimRegEx();
}

public struct InventoryItem(string itemName, Rectangle boundingbox, bool showWarning = false)
{
    public string Name { get; set; } = itemName;
    public Rectangle Bounding { get; set; } = boundingbox;
    public int Count { get; set; } = 1; //if no label found, assume 1
    public bool Warning { get; set; } = showWarning;
}
