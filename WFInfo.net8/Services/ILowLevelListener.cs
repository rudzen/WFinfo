namespace WFInfo.Services;

public interface ILowLevelListener : IDisposable
{
    void Hook();
    void UnHook();
}
