namespace Music;

public class SoundCloudDiscoveryProvider : IMusicDiscoveryProvider
{
    public string Id => SoundCloudSupport.DiscoveryProviderId;
    public string Name => "SoundCloud Charts";
    public bool Enabled => SoundCloudSupport.IsDiscoveryEnabled;

    public Task<List<MusicBrowseSection>> GetHomeSectionsAsync(int limit, CancellationToken cancellationToken = default)
    {
        return GetHomeSectionsInternalAsync(limit, cancellationToken);
    }

    public Task<MusicBrowseSection> GetSectionAsync(string sectionId, int limit, CancellationToken cancellationToken = default)
    {
        return GetSectionInternalAsync(sectionId, limit, cancellationToken);
    }

    public Task<MusicAlbum> GetAlbumAsync(string id, CancellationToken cancellationToken = default)
    {
        return SoundCloudSupport.GetChartAlbumAsync(id, cancellationToken);
    }

    async Task<List<MusicBrowseSection>> GetHomeSectionsInternalAsync(int limit, CancellationToken cancellationToken)
    {
        var albums = await SoundCloudSupport.GetChartAlbumsAsync(cancellationToken);
        if (albums.Count == 0)
            return new List<MusicBrowseSection>();

        return new List<MusicBrowseSection>
        {
            new()
            {
                id = SoundCloudSupport.ChartsSectionId,
                title = "SoundCloud",
                type = "album",
                source_provider = Id,
                has_more = albums.Count > limit,
                albums = albums.Take(Math.Max(limit, 1)).ToList()
            }
        };
    }

    async Task<MusicBrowseSection> GetSectionInternalAsync(string sectionId, int limit, CancellationToken cancellationToken)
    {
        if (!string.Equals(sectionId, SoundCloudSupport.ChartsSectionId, StringComparison.OrdinalIgnoreCase))
            return null;

        var albums = await SoundCloudSupport.GetChartAlbumsAsync(cancellationToken);
        if (albums.Count == 0)
            return null;

        return new MusicBrowseSection
        {
            id = SoundCloudSupport.ChartsSectionId,
            title = "SoundCloud",
            type = "album",
            source_provider = Id,
            has_more = false,
            albums = albums.ToList()
        };
    }
}
