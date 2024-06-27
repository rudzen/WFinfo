using Windows.Graphics.Capture;

namespace WFInfo.Services.Screenshot.Composition.WindowsRuntimeHelpers;

public interface ICaptureItemService
{
    GraphicsCaptureItem CreateItemForWindow(nint hWnd);
    GraphicsCaptureItem CreateItemForMonitor(nint hMon);
}
