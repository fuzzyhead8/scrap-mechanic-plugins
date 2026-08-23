using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Tests;

public sealed class ReleaseChannelTests
{
    [Theory]
    [InlineData("0.2.0-preview.6", "v0.2.0-preview.6")]
    [InlineData("0.2.0-preview.6+abcdef", "v0.2.0-preview.6")]
    [InlineData("v0.2.0-preview.6", "v0.2.0-preview.6")]
    [InlineData("0.2.0", null)]
    [InlineData("0.2.0+abcdef", null)]
    [InlineData(null, null)]
    public void Prerelease_builds_target_their_tag_while_stable_builds_use_latest(
        string? informationalVersion,
        string? expectedTag)
    {
        Assert.Equal(expectedTag, ReleaseChannel.GetReleaseTag(informationalVersion));
    }
}
