using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Serilog;
using WFInfo.Settings;

namespace WFInfo.Services.OpticalCharacterRecognition;

public sealed class SnapZoneDivider(ApplicationSettings settings) : ISnapZoneDivider
{
    private sealed record Row(int Width, int Height);

    private sealed record Column(int Start, int Width);

    private static readonly ILogger Logger = Log.Logger.ForContext<SnapZoneDivider>();

    private static readonly Pen Brown = new(Brushes.Brown);
    private static readonly Pen White = new(Brushes.White);

    public List<SnapZone> DivideSnapZones(
        Bitmap filteredImage,
        Bitmap filteredImageClean,
        int[] rowHits,
        int[] colHits)
    {
        //find rows
        var rows = FindRows(
            filteredImageWidth: filteredImage.Width,
            filteredImageHeight: filteredImage.Height,
            rowHits: rowHits,
            rowHeight: out var rowHeight
        );

        //combine adjacent rows into one block of text
        CombineRows(
            filteredImage: filteredImage,
            filteredImageClean: filteredImageClean,
            rows: rows,
            rowHeight: rowHeight
        );

        //find columns
        var cols = FindColumns(
            filteredImageWidth: filteredImage.Width,
            filteredImageHeight: filteredImage.Height,
            colHits: colHits,
            rowHeight: rowHeight
        );

        // divide image into text blocks
        var zones = DivideToTextBlocks(
            filteredImageClean: filteredImageClean,
            rows: rows,
            cols: cols,
            rowHeight: rowHeight
        );

        DrawRectangles(filteredImage, zones, rowHeight);

        return zones;
    }

    private List<Row> FindRows(
        int filteredImageWidth,
        int filteredImageHeight,
        ReadOnlySpan<int> rowHits,
        out int rowHeight)
    {
        var i = 0;
        var height = 0;
        var rows = new List<Row>();

        while (i < filteredImageHeight)
        {
            if ((double)(rowHits[i]) / filteredImageWidth > settings.SnapRowTextDensity)
            {
                var j = 0;
                while (i + j < filteredImageHeight &&
                       (double)(rowHits[i + j]) / filteredImageWidth > settings.SnapRowEmptyDensity)
                {
                    j++;
                }

                //only add "rows" of reasonable height
                if (j > 3)
                {
                    rows.Add(new Row(i, j));
                    height += j;
                }

                i += j;
            }
            else
            {
                i++;
            }
        }

        if (rows.Count > 1)
            height /= rows.Count;

        rowHeight = height;
        return rows;
    }

    private static void CombineRows(
        Image filteredImage,
        Image filteredImageClean,
        List<Row> rows,
        int rowHeight)
    {
        var i = 0;

        var rowSpan = CollectionsMarshal.AsSpan(rows);
        ref var rowRef = ref MemoryMarshal.GetReference(rowSpan);

        using var g = Graphics.FromImage(filteredImage);
        using var gClean = Graphics.FromImage(filteredImageClean);
        while (i + 1 < rows.Count)
        {
            ref var row = ref Unsafe.Add(ref rowRef, i);
            ref var nextRow = ref Unsafe.Add(ref rowRef, i + 1);
            g.DrawLine(Brown, 0, row.Width + row.Height, 10000, row.Width + row.Height);
            gClean.DrawLine(White, 0, row.Width + row.Height, 10000, row.Width + row.Height);
            if (row.Width + row.Height + rowHeight > nextRow.Width)
            {
                rows[i + 1] = row with { Height = nextRow.Width - row.Width + nextRow.Height };
                rows.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }
    }

    private List<Column> FindColumns(
        int filteredImageWidth,
        int filteredImageHeight,
        ReadOnlySpan<int> colHits,
        int rowHeight)
    {
        List<Column> cols = [];
        var colStart = 0;
        var i = 0;

        while (i + 1 < filteredImageWidth)
        {
            if ((double)(colHits[i]) / filteredImageHeight < settings.SnapColEmptyDensity)
            {
                var j = 0;
                while (i + j + 1 < filteredImageWidth && (double)colHits[i + j] / filteredImageWidth < settings.SnapColEmptyDensity)
                    j++;

                if (j > rowHeight / 2)
                {
                    if (i != 0)
                        cols.Add(new Column(colStart, i - colStart));

                    colStart = i + j + 1;
                }

                i += j;
            }

            i += 1;
        }

        if (i != colStart)
            cols.Add(new Column(colStart, i - colStart));

        return cols;
    }

    private static List<SnapZone> DivideToTextBlocks(
        Bitmap filteredImageClean,
        List<Row> rows,
        List<Column> cols,
        int rowHeight)
    {
        var zones = new List<SnapZone>();

        var rowSpan = CollectionsMarshal.AsSpan(rows);
        var colSpan = CollectionsMarshal.AsSpan(cols);

        ref var rowRef = ref MemoryMarshal.GetReference(rowSpan);
        ref var colRef = ref MemoryMarshal.GetReference(colSpan);

        for (var i = 0; i < rowSpan.Length; i++)
        {
            ref var row = ref Unsafe.Add(ref rowRef, i);
            var top = Math.Max(row.Width - rowHeight / 2, 0);
            var height = Math.Min(row.Height + rowHeight, filteredImageClean.Height - top - 1);
            for (var j = 0; j < colSpan.Length; j++)
            {
                ref var col = ref Unsafe.Add(ref colRef, j);
                var left = Math.Max(col.Start - rowHeight / 4, 0);
                var width = Math.Min(col.Width + rowHeight / 2, filteredImageClean.Width - left - 1);
                var cloneRect = new Rectangle(left, top, width, height);
                var temp = new SnapZone(filteredImageClean.Clone(cloneRect, filteredImageClean.PixelFormat), cloneRect);
                zones.Add(temp);
            }
        }

        return zones;
    }

    private void DrawRectangles(
        Bitmap filteredImage,
        List<SnapZone> zones,
        int rowHeight)
    {
        var zoneSpan = CollectionsMarshal.AsSpan(zones);
        ref var zoneRef = ref MemoryMarshal.GetReference(zoneSpan);

        using var g = Graphics.FromImage(filteredImage);

        for (var i = 0; i < zoneSpan.Length; i++)
        {
            ref var zone = ref Unsafe.Add(ref zoneRef, i);
            g.DrawRectangle(Brown, zone.Rectangle);
        }

        g.DrawRectangle(Brown, 0, 0, rowHeight / 2, rowHeight);
    }
}
