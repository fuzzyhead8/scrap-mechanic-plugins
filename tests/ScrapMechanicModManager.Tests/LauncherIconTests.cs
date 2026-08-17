namespace ScrapMechanicModManager.Tests;

public sealed class LauncherIconTests
{
    [Fact]
    public void Launcher_embeds_a_multiresolution_application_icon()
    {
        string repoRoot = FindRepoRoot();
        string projectPath = Path.Combine(
            repoRoot,
            "src",
            "ScrapMechanicModManager",
            "ScrapMechanicModManager.csproj");
        string project = File.ReadAllText(projectPath);
        Assert.Contains(
            "<ApplicationIcon>Assets\\ScrapMechanicModManager.ico</ApplicationIcon>",
            project);

        string iconPath = Path.Combine(
            repoRoot,
            "src",
            "ScrapMechanicModManager",
            "Assets",
            "ScrapMechanicModManager.ico");
        Assert.True(File.Exists(iconPath), $"Missing {iconPath}");

        using var reader = new BinaryReader(File.OpenRead(iconPath));
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        ushort imageCount = reader.ReadUInt16();
        Assert.True(imageCount >= 7);

        var sizes = new HashSet<int>();
        for (int index = 0; index < imageCount; index++)
        {
            byte width = reader.ReadByte();
            byte height = reader.ReadByte();
            reader.ReadByte();
            reader.ReadByte();
            Assert.Equal(1, reader.ReadUInt16());
            Assert.Equal(32, reader.ReadUInt16());
            uint byteCount = reader.ReadUInt32();
            uint offset = reader.ReadUInt32();
            int resolvedWidth = width == 0 ? 256 : width;
            int resolvedHeight = height == 0 ? 256 : height;
            Assert.Equal(resolvedWidth, resolvedHeight);
            Assert.InRange(offset + byteCount, 1u, (uint)reader.BaseStream.Length);
            sizes.Add(resolvedWidth);
        }

        Assert.Subset(
            sizes,
            new HashSet<int> { 16, 24, 32, 48, 64, 128, 256 });
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "robots_01.zip")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
