namespace Mertviy;

internal sealed record UpdateCheckResult(
    string CurrentVersion,
    string? LatestVersion,
    string? ReleasePageUrl,
    Uri? AssetUrl,
    Uri? ChecksumUrl,
    long AssetSize,
    bool IsUpdateAvailable,
    string StatusMessage)
{
    public static UpdateCheckResult Unavailable(string currentVersion, string message) =>
        new(currentVersion, null, null, null, null, 0, false, message);
}
