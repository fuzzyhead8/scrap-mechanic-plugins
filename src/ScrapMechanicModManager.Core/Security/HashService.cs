using System.Security.Cryptography;

namespace ScrapMechanicModManager.Core.Security;

public sealed class HashService
{
    public async Task<string> ComputeSha256Async(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    public async Task<bool> VerifyFileAsync(
        string filePath,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        await using FileStream stream = File.OpenRead(filePath);
        string actual = await ComputeSha256Async(stream, cancellationToken);
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }
}
