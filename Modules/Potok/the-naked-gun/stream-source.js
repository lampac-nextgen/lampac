import { PotokSDK } from 'potok-sdk';
import { fetchEvents, fetchSourceJson, fetchUrlJson } from './api.js';
import { mediaFromWatchQuery } from './media.js';

function mapSourceToSearchResult(item, index) {
    var stream = item.stream || item.url;
    return {
        id: `source-${index}`,
        title: item.name,
        quality: '',
        url: stream,
        streamUrl: stream,
        provider: item.name,
        providerId: stream,
        voice: '',
        kind: stream.includes('.m3u8')
            ? 'm3u8'
            : stream.includes('.mpd')
                ? 'dash'
                : 'mp4'
    };
}

export function registerTheNakedGunStreamSource() {
    PotokSDK.streams.registerStreamSource({
        id: 'the-naked-gun',
        name: 'The Naked Gun',
        supportedTypes: ['movie', 'tv'],

        async search(query) {
            const media = await mediaFromWatchQuery(query);
            const sources = await fetchEvents(media);
            return sources.map(mapSourceToSearchResult);
        },

        async getEpisodes(stream, context) {
            const media = await mediaFromWatchQuery(context);
            const root = await fetchSourceJson(stream.providerId, media);
            if (root.type !== 'season') {
                return {
                    episodes: [],
                    tmdbSeasonsCount: 1
                };
            }

            const seasonResponses = await Promise.all(
                root.data.map((season) => fetchUrlJson(season.url))
            );

            const episodes = seasonResponses.flatMap((json) =>
                json.data.map((item) => ({
                    id: `${item.s}:${item.e}`,
                    season: item.s,
                    episode: item.e,
                    title: item.name,
                    url: item.stream || item.url
                }))
            );

            return {
                episodes,
                tmdbSeasonsCount: root.data.length
            };
        },

        async getPlaybackInfo(stream, episode, context) {
            const media = await mediaFromWatchQuery(context);

            if (episode) {
                var stream = episode.stream || episode.url;
                return {
                    streamUrl: stream,
                    streamType: stream.includes('.m3u8')
                        ? 'm3u8'
                        : stream.includes('.mpd')
                            ? 'dash'
                            : 'mp4',
                    title: episode.title || stream.title,
                    season: episode.season,
                    episode: episode.episode
                };
            }

            const root = await fetchSourceJson(stream.providerId, media);

            if (root.type === 'movie') {
                const file = root.data[0];
                var stream = file.stream || file.url;
                return {
                    streamUrl: stream,
                    streamType: stream.includes('.m3u8')
                        ? 'm3u8'
                        : stream.includes('.mpd')
                            ? 'dash'
                            : 'mp4',
                    title: file.title || stream.title
                };
            }

            const firstSeason = await fetchUrlJson(root.data[0].url);
            const firstEpisode = firstSeason.data[0];
            var streamEpisode = firstEpisode.stream || firstEpisode.url;

            return {
                streamUrl: streamEpisode,
                streamType: streamEpisode.includes('.m3u8')
                    ? 'm3u8'
                    : streamEpisode.includes('.mpd')
                        ? 'dash'
                        : 'mp4',
                title: firstEpisode.title || stream.title,
                season: firstEpisode.s,
                episode: firstEpisode.e
            };
        }
    });
}
