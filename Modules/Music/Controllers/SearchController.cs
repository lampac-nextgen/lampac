using Microsoft.AspNetCore.Mvc;

namespace Music;

public class SearchController : BaseController
{
    [HttpGet]
    [Route("music/search")]
    async public Task<ActionResult> Search(string q, string provider, bool full = false)
    {
        var result = await MusicCatalogService.SearchAsync(q, provider, full);
        MusicImageProxyService.Apply(this, result);
        return ContentTo(MusicJson.Serialize(result));
    }

    [HttpGet]
    [Route("music/artist")]
    async public Task<ActionResult> Artist(string id, string provider)
    {
        var result = await MusicCatalogService.GetArtistAsync(id, provider);
        if (result == null)
            return ContentTo(MusicJson.Serialize(new { available = false, message = "Artist not found." }));

        MusicImageProxyService.Apply(this, result);
        return ContentTo(MusicJson.Serialize(result));
    }

    [HttpGet]
    [Route("music/artistsection")]
    async public Task<ActionResult> ArtistSection(string id, string provider, string page, int limit = 20)
    {
        var result = await MusicCatalogService.GetArtistSectionAsync(id, provider, page, limit);
        if (result == null)
            return ContentTo(MusicJson.Serialize(new { available = false, message = "Artist section not found." }));

        MusicImageProxyService.Apply(this, result);
        return ContentTo(MusicJson.Serialize(result));
    }

    [HttpGet]
    [Route("music/artistimg")]
    async public Task<ActionResult> ArtistImage(string id, string name, string country, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ContentTo(MusicJson.Serialize(new { available = false, id, images = new List<MusicImage>() }));

        var artist = new MusicArtist
        {
            id = id,
            name = name,
            country = country
        };

        artist.images = await DiscogsArtistImageService.ResolveImagesAsync(artist, cancellationToken) ?? new List<MusicImage>();
        MusicImageProxyService.Apply(this, artist);

        return ContentTo(MusicJson.Serialize(new
        {
            available = artist.images.Count > 0,
            id = id,
            images = artist.images
        }));
    }

    [HttpGet]
    [Route("music/album")]
    async public Task<ActionResult> Album(string id, string provider)
    {
        var result = await MusicCatalogService.GetAlbumAsync(id, provider);
        if (result == null)
            return ContentTo(MusicJson.Serialize(new { available = false, message = "Album not found." }));

        MusicImageProxyService.Apply(this, result);
        return ContentTo(MusicJson.Serialize(result));
    }

    [HttpGet]
    [Route("music/track")]
    async public Task<ActionResult> Track(string id, string provider)
    {
        var result = await MusicCatalogService.GetTrackAsync(id, provider);
        if (result == null)
            return ContentTo(MusicJson.Serialize(new { available = false, message = "Track not found." }));

        return ContentTo(MusicJson.Serialize(result));
    }
}
