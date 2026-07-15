namespace Music;

public static class MusicProviderRegistry
{
    static readonly List<IMusicMetadataProvider> metadataProviders = new()
    {
        new MusicBrainzMetadataProvider()
    };

    static readonly List<IMusicDiscoveryProvider> discoveryProviders = new()
    {
        new AppleMusicDiscoveryProvider(),
        new VkTopChartDiscoveryProvider(),
        new SoundCloudDiscoveryProvider()
    };

    static readonly List<IMusicAudioProvider> audioProviders = new()
    {
        new YouTubeAudioProvider(),
        new SefonAudioProvider(),
        new SoundCloudAudioProvider()
    };

    static readonly IMusicAudioProvider z3FmAudioProvider = new Z3FmAudioProvider();

    static readonly List<IMusicAuthProvider> authProviders = new()
    {
        new SoundCloudAuthProvider()
    };

    public static IReadOnlyList<IMusicMetadataProvider> MetadataProviders => metadataProviders;
    public static IReadOnlyList<IMusicDiscoveryProvider> DiscoveryProviders => discoveryProviders;
    public static IReadOnlyList<IMusicAudioProvider> AudioProviders => GetAudioProviders();
    public static IReadOnlyList<IMusicAuthProvider> AuthProviders => authProviders;

    public static IMusicMetadataProvider GetMetadataProvider(string id = null)
    {
        string target = string.IsNullOrWhiteSpace(id) ? ModInit.conf.default_metadata_provider : id;
        return metadataProviders.FirstOrDefault(i => i.Id == target && i.Enabled) ?? metadataProviders.FirstOrDefault(i => i.Enabled);
    }

    public static IMusicAudioProvider GetAudioProvider(string id = null)
    {
        string target = string.IsNullOrWhiteSpace(id) ? ModInit.conf.default_audio_provider : id;
        return AudioProviders.FirstOrDefault(i => i.Id == target && i.Enabled) ?? AudioProviders.FirstOrDefault(i => i.Enabled);
    }

    public static IMusicDiscoveryProvider GetDiscoveryProvider(string id = null)
    {
        string target = string.IsNullOrWhiteSpace(id) ? null : id;
        return discoveryProviders.FirstOrDefault(i => i.Id == target && i.Enabled) ?? discoveryProviders.FirstOrDefault(i => i.Enabled);
    }

    public static IMusicAuthProvider GetAuthProvider(string id = null)
    {
        string target = string.IsNullOrWhiteSpace(id) ? ModInit.conf.default_auth_provider : id;
        return authProviders.FirstOrDefault(i => i.Id == target && i.Enabled) ?? authProviders.FirstOrDefault(i => i.Enabled);
    }

    public static List<MusicProviderDescriptor> DescribeMetadata() => metadataProviders.Select(i => new MusicProviderDescriptor
    {
        id = i.Id,
        name = i.Name,
        type = "metadata",
        enabled = i.Enabled,
        requires_auth = false,
        capabilities = new List<string> { "search", "artist", "album", "track", "browse" }
    }).ToList();

    public static List<MusicProviderDescriptor> DescribeAudio() => AudioProviders.Select(i => new MusicProviderDescriptor
    {
        id = i.Id,
        name = i.Name,
        type = "audio",
        enabled = i.Enabled,
        requires_auth = i.RequiresAuth,
        capabilities = BuildAudioCapabilities(i)
    }).ToList();

    public static List<MusicProviderDescriptor> DescribeAuth() => authProviders.Select(i => new MusicProviderDescriptor
    {
        id = i.Id,
        name = i.Name,
        type = "auth",
        enabled = i.Enabled,
        requires_auth = true,
        capabilities = new List<string> { "state", "save", "logout" }
    }).ToList();

    static List<string> BuildAudioCapabilities(IMusicAudioProvider provider)
    {
        var capabilities = new List<string> { "match", "streams", "playback", "audio" };

        if (string.Equals(provider?.Id, "youtubeaudio", StringComparison.OrdinalIgnoreCase))
            capabilities.Add("video");

        if (provider?.RequiresAuth == true)
            capabilities.Add("auth");

        return capabilities;
    }

    static IReadOnlyList<IMusicAudioProvider> GetAudioProviders()
    {
        if (z3FmAudioProvider.Enabled)
            return audioProviders.Concat(new[] { z3FmAudioProvider }).ToList();

        return audioProviders;
    }
}
