using Velopack.Sources;

namespace CraftingCalculator;

internal sealed class GithubUpdateSource(string repositoryUrl) : GithubSource(repositoryUrl, null, false)
{
    private const string WindowsFeed = "releases.win.json";

    protected override async Task<GithubRelease[]> GetReleases(bool includePrereleases)
    {
        var releases = await base.GetReleases(includePrereleases).ConfigureAwait(false);
        var latest = releases.FirstOrDefault(release => FindAsset(release, WindowsFeed) is not null);
        // This workflow publishes full packages. A historical release cannot block the current one.
        return latest is null ? [] : [latest];
    }

    protected override string GetAssetUrlFromName(GithubRelease release, string assetName) =>
        FindAsset(release, assetName)?.Url ?? base.GetAssetUrlFromName(release, assetName);

    private static GithubReleaseAsset? FindAsset(GithubRelease release, string name) =>
        release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, name, StringComparison.OrdinalIgnoreCase));
}
