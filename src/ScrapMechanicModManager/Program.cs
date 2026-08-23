namespace ScrapMechanicModManager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        WindowsShellIconRefresher.RefreshCurrentExecutable();
        Application.Run(new MainForm());
    }
}
