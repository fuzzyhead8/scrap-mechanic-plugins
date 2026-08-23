using System.Runtime.InteropServices;

namespace ScrapMechanicModManager;

internal static class WindowsShellIconRefresher
{
    public static void RefreshCurrentExecutable()
    {
        SHChangeNotify(
            ShellChangeEvent.UpdateItem,
            ShellChangeFlags.PathW | ShellChangeFlags.FlushNoWait,
            Application.ExecutablePath,
            IntPtr.Zero);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(
        ShellChangeEvent eventId,
        ShellChangeFlags flags,
        string item1,
        IntPtr item2);

    private enum ShellChangeEvent : uint
    {
        UpdateItem = 0x00002000,
    }

    [Flags]
    private enum ShellChangeFlags : uint
    {
        PathW = 0x0005,
        FlushNoWait = 0x3000,
    }
}
