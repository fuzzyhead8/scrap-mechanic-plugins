using System.Diagnostics;

namespace ScrapMechanicModManager.Core.Validation;

public sealed class ExecutableVersionReader
{
    public string ReadProductVersion(string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "The executable required for version detection was not found.",
                executablePath);
        }

        FileVersionInfo info = FileVersionInfo.GetVersionInfo(executablePath);
        string? version = !string.IsNullOrWhiteSpace(info.ProductVersion)
            ? info.ProductVersion
            : info.FileVersion;
        if (string.IsNullOrWhiteSpace(version))
        {
            version = PortableExecutableVersionReader.TryReadProductVersion(executablePath);
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidDataException(
                $"The executable contains no version information: {executablePath}");
        }

        string normalized = version.Split(['+', ' '], StringSplitOptions.RemoveEmptyEntries)[0];
        return normalized;
    }
}
