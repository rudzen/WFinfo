using WFInfo.Settings;

namespace WFInfo.Services;

public sealed class OverlayFactory(ApplicationSettings settings) : IOverlayFactory
{
    public Overlay Create()
    {
        return new Overlay(settings);
    }
}
