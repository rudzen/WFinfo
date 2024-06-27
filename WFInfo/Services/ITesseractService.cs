using Tesseract;

namespace WFInfo.Services;

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
    Task ReloadEngines();
}
