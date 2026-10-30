using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SSnip.App.Services;

/// <summary>Sleep or shut down the PC for the "when queue finishes" option.</summary>
public interface IPowerService
{
    void Sleep();

    void ShutDown();
}

public sealed class PowerService : IPowerService
{
    public void Sleep()
    {
        // Sleep, not hibernate; wake events stay enabled.
        if (!SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void ShutDown()
    {
        // Windows' own shutdown gives open apps the usual chance to save; "shutdown /a" still aborts it.
        using var process = Process.Start(new ProcessStartInfo("shutdown.exe")
        {
            ArgumentList = { "/s", "/t", "10", "/c", "SSnip finished the download queue." },
            CreateNoWindow = true,
            UseShellExecute = false,
        });
    }

    // DllImport rather than LibraryImport, which would need unsafe code enabled for the whole app.
    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.Bool)] bool hibernate,
        [MarshalAs(UnmanagedType.Bool)] bool forceCritical,
        [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);
}
