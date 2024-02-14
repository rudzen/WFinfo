using System.Windows.Threading;

namespace WFInfo.Extensions;

public static class ApplicationExtensions
{
    public static void InvokeIfRequired(this Dispatcher dispatcher, Action action)
    {
        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }

    public static void InvokeAsyncIfRequired(this Dispatcher dispatcher, Action action)
    {
        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.InvokeAsync(action);
    }
}
