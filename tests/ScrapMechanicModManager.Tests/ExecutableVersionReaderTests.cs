using ScrapMechanicModManager.Core.Validation;

namespace ScrapMechanicModManager.Tests;

public sealed class ExecutableVersionReaderTests
{
    [Fact]
    public void Reader_returns_product_version_from_a_versioned_binary()
    {
        string assemblyPath = typeof(GameInstallValidator).Assembly.Location;
        var reader = new ExecutableVersionReader();

        string version = reader.ReadProductVersion(assemblyPath);

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.True(Version.TryParse(version, out _), $"Not a System.Version: {version}");
    }

    [Fact]
    public void Reader_rejects_a_missing_executable()
    {
        var reader = new ExecutableVersionReader();

        Assert.Throws<FileNotFoundException>(() =>
            reader.ReadProductVersion(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe")));
    }
}
