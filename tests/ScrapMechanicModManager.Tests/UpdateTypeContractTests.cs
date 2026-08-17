using System.Reflection;
using ScrapMechanicModManager.Core.Validation;

namespace ScrapMechanicModManager.Tests;

public sealed class UpdateTypeContractTests
{
    [Theory]
    [InlineData("ScrapMechanicModManager.Core.Updates.ModManifest")]
    [InlineData("ScrapMechanicModManager.Core.Updates.GitHubReleaseClient")]
    [InlineData("ScrapMechanicModManager.Core.Security.HashService")]
    [InlineData("ScrapMechanicModManager.Core.Installation.ModInstaller")]
    [InlineData("ScrapMechanicModManager.Core.Validation.ExecutableVersionReader")]
    public void Core_exposes_required_update_components(string typeName)
    {
        Assembly assembly = typeof(GameInstallValidator).Assembly;

        Assert.NotNull(assembly.GetType(typeName));
    }
}
