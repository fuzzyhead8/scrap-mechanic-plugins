using System.Diagnostics;
using ScrapMechanicModManager.Core.Validation;

namespace ScrapMechanicModManager.Core.Platform;

public sealed class LinuxGamePlatformService(bool useFlatpakSteam) : IGamePlatformService
{
    private readonly bool _useFlatpakSteam = useFlatpakSteam;

    public static bool IsFlatpakSteamRoot(string steamRoot)
    {
        string normalized = steamRoot.Replace('\\', '/');
        return normalized.Contains(
            "/.var/app/com.valvesoftware.Steam/",
            StringComparison.Ordinal);
    }

    public static bool MatchesGameProcess(string processName, string? commandLine)
    {
        if (processName.StartsWith("ScrapMechanic", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(commandLine)
            && commandLine.Contains("ScrapMechanic.exe", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsGameRunning()
    {
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (MatchesGameProcess(
                            process.ProcessName,
                            ReadProcCommandLine(process.Id)))
                    {
                        return true;
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited while it was being inspected.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Another user's process may not be readable.
                }
            }
        }
        return false;
    }

    public GameLaunchCommand CreateLaunchCommand(bool devMode)
    {
        var arguments = new List<string>();
        string fileName;
        if (_useFlatpakSteam)
        {
            fileName = "flatpak";
            arguments.Add("run");
            arguments.Add("com.valvesoftware.Steam");
        }
        else
        {
            fileName = "steam";
        }

        arguments.Add("-applaunch");
        arguments.Add(GameInstallValidator.ScrapMechanicAppId);
        if (devMode)
        {
            arguments.Add("-dev");
        }
        return new GameLaunchCommand(fileName, arguments);
    }

    public void LaunchGame(bool devMode)
    {
        GameLaunchCommand command = CreateLaunchCommand(devMode);
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            UseShellExecute = false,
        };
        foreach (string argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Steam could not be started.");
    }

    private static string? ReadProcCommandLine(int processId)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        string path = Path.Combine("/proc", processId.ToString(), "cmdline");
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
