using System;
using System.Runtime.InteropServices;
using System.Threading;

// Windows can briefly refuse OpenClipboard while the shell renders or locks a
// clipboard payload. Retry only that transient failure; every other error is a
// real test failure.
internal static class TestSupport
{
    internal const int ClipboardCannotOpen = unchecked((int)0x800401D0);

    internal static void ClipboardRetry(Action action)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { action(); return; }
            catch (ExternalException ex)
            {
                if (attempt >= 99 || ex.HResult != ClipboardCannotOpen) throw;
                Thread.Sleep(100);
            }
        }
    }

    internal static T ClipboardRetry<T>(Func<T> action)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return action(); }
            catch (ExternalException ex)
            {
                if (attempt >= 99 || ex.HResult != ClipboardCannotOpen) throw;
                Thread.Sleep(100);
            }
        }
    }

    internal static bool TryClipboardRetry(Action action)
    {
        try { ClipboardRetry(action); return true; }
        catch (ExternalException ex)
        {
            if (ex.HResult == ClipboardCannotOpen) return false;
            throw;
        }
    }
}
