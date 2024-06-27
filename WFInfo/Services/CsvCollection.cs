using System.IO;
using System.Runtime.CompilerServices;
using DotNext.Collections.Generic;
using RecordParser.Builders.Writer;
using RecordParser.Extensions;
using RecordParser.Parsers;
using WFInfo.Settings;

namespace WFInfo.Services;

public sealed class CsvCollection(ApplicationSettings settings) : ICsvCollection
{
    private static readonly IVariableLengthWriter<(string ItemName, string Plat, string Ducats, string Volume, bool Vaulted, string Owned, string PartsDetected)> OcrWriter;



    private static readonly ParallelismOptions ParallelOptions = new()
    {
        Enabled = true,
        EnsureOriginalOrdering = true,
        MaxDegreeOfParallelism = 4
    };

    static CsvCollection()
    {
        OcrWriter = new VariableLengthWriterBuilder<(string ItemName, string Plat, string Ducats, string Volume, bool Vaulted, string Owned, string PartsDetected)>()
                 .Map(x => x.ItemName, indexColumn: 0)
                 .Map(x => x.Plat, indexColumn: 1)
                 .Map(x => x.Ducats, indexColumn: 2)
                 .Map(x => x.Volume, indexColumn: 3)
                 .Map(x => x.Vaulted, indexColumn: 4, BoolToString)
                 .Map(x => x.Owned, indexColumn: 5)
                 .Map(x => x.PartsDetected, indexColumn: 6)
                 .Build(",");
    }

    public void OcrAddRow(
        string fileName,
        string itemName,
        string plat,
        string ducats,
        string volume,
        bool vaulted,
        string owned,
        string partsDetected)
    {
        if (!settings.SnapitExport)
            return;

        using var stream = File.Open(fileName, FileMode.Append);
        using TextWriter textWriter = new StreamWriter(stream);

        var items = List.Singleton((
            ItemName: itemName,
            Plat: plat,
            Ducats: ducats,
            Volume: volume,
            Vaulted: vaulted,
            Owned: owned, PartsDetected: partsDetected
        ));
        textWriter.WriteRecords(items, OcrWriter.TryFormat, ParallelOptions);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (bool success, int charsWritten) BoolToString(Span<char> span, bool inst)
    {
        var str = inst.ToString(ApplicationConstants.Culture);
        str.AsSpan().CopyTo(span);
        return (true, str.Length);
    }
}
