using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Serilog;
using WFInfo.Services.WindowInfo;
using Point = System.Drawing.Point;

namespace WFInfo.Services.OpticalCharacterRecognition;

public sealed class RewardSelector(IWindowInfoService window) : IRewardSelector
{
    // Pixel measurements for reward screen @ 1920 x 1080 with 100% scale https://docs.google.com/drawings/d/1Qgs7FU2w1qzezMK-G1u9gMTsQZnDKYTEU36UPakNRJQ/edit
    private const int PixelRewardWidth = 968;
    private const int PixelRewardHeight = 235;
    private const int PixelRewardYDisplay = 316;

    private static readonly ILogger Logger = Log.Logger.ForContext<RewardSelector>();

    public int GetSelectedReward(
        ref Point lastClick,
        in double uiScaling,
        int numberOfRewardsDisplayed,
        bool dumpToImage = false)
    {
        Logger.Debug("Last click: {LastClick}", lastClick.ToString());
        lastClick.Offset(-window.Window.X, -window.Window.Y);
        var width = window.Window.Width * (int)window.DpiScaling;
        var height = window.Window.Height * (int)window.DpiScaling;
        var mostWidth = (int)(PixelRewardWidth * window.ScreenScaling * uiScaling);
        var mostLeft = (width / 2) - (mostWidth / 2);
        var bottom = height / 2 - (int)((PixelRewardYDisplay - PixelRewardHeight) * window.ScreenScaling * 0.5 * uiScaling);
        var top = height / 2 - (int)((PixelRewardYDisplay) * window.ScreenScaling * uiScaling);
        var selectionRectangle = SelectionRectangle(numberOfRewardsDisplayed, mostLeft, top, mostWidth, bottom);

        if (!selectionRectangle.Contains(lastClick))
            return -1;

        var middleHeight = top + bottom / 4;
        var length = mostWidth / 8;
        int primeRewardIndex;

        // rare, but can happen if others don't get enough traces
        if (numberOfRewardsDisplayed == 1)
        {
            primeRewardIndex = 0;
        }
        else if (numberOfRewardsDisplayed != 3)
        {
            Span<Point> rewardPoints4 = stackalloc Point[]
            {
                new(mostLeft + length, middleHeight),
                new(mostLeft + 3 * length, middleHeight),
                new(mostLeft + 5 * length, middleHeight),
                new(mostLeft + 7 * length, middleHeight)
            };

            primeRewardIndex = GetRewardIndex(in lastClick, rewardPoints4);

            // if we only have two rewards, we need to adjust the index
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
            Span<Point> rewardPoints3 = stackalloc Point[]
            {
                new(mostLeft + 2 * length, middleHeight),
                new(mostLeft + 4 * length, middleHeight),
                new(mostLeft + 6 * length, middleHeight)
            };

            primeRewardIndex = GetRewardIndex(in lastClick, rewardPoints3);
        }

        #region debuging image

        /*Debug.WriteLine($"Closest point: {lowestDistancePoint}, with distance: {lowestDistance}");

        timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff", ApplicationConstants.Culture);
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

    private static Rectangle SelectionRectangle(
        int numberOfRewardsDisplayed,
        int mostLeft,
        int top,
        int mostWidth,
        int bottom)
    {
        var selectionRectangle = new Rectangle(mostLeft, top, mostWidth, bottom / 2);

        if (numberOfRewardsDisplayed != 3)
            return selectionRectangle;

        // if the rewards are displayed in a different way, adjust the selection rectangle
        var offset = selectionRectangle.Width / 8;
        return selectionRectangle with
        {
            X = selectionRectangle.X + offset, Width = selectionRectangle.Width - offset * 2
        };
    }

    private static int GetRewardIndex(in Point lastClick, ReadOnlySpan<Point> rewardPoints)
    {
        var lowestDistance = int.MaxValue;
        var primeRewardIndex = 0;
        ref var rewardPointsRef = ref MemoryMarshal.GetReference(rewardPoints);

        for (var i = 0; i < rewardPoints.Length; i++)
        {
            ref var pnt = ref Unsafe.Add(ref rewardPointsRef, i);

            var distanceToLastClick = (lastClick.X - pnt.X) * (lastClick.X - pnt.X) +
                                      (lastClick.Y - pnt.Y) * (lastClick.Y - pnt.Y);

            Logger.Debug("current point: {Pnt}, with distance: {DistanceToLastClick}", pnt, distanceToLastClick);

            if (distanceToLastClick >= lowestDistance)
                continue;

            lowestDistance = distanceToLastClick;
            primeRewardIndex = i;
        }

        return primeRewardIndex;
    }
}
