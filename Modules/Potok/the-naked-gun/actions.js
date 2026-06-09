import { PotokSDK } from 'potok-sdk';
import { state, patchState } from './state.js';
import { fetchEvents, fetchSourceJson, fetchUrlJson } from './api.js';

export async function openMedia(media) {
    patchState({
        media,
        loading: true,
        error: '',

        sources: [],
        sourceValue: '',

        responseType: '',
        seasons: [],
        seasonValue: '',

        movieFiles: [],
        episodeFiles: [],
        voiceLinks: [],
        voiceValue: '',

        popupOpen: false
    });

    PotokSDK.ui.navigateTo('/extensions/the-naked-gun');

    try {
        const sources = await fetchEvents(media);

        patchState({
            sources,
            sourceValue: sources[0]?.url || ''
        });

        if (sources[0]) {
            await selectSource(sources[0].url);
        }
    } catch (error) {

        patchState({
            error: getErrorMessage(error),
            sources: [],
            sourceValue: ''
        });
    } finally {
        patchState({ loading: false });
    }
}

export async function selectSource(sourceUrl) {
    patchState({
        loading: true,
        error: '',
        sourceValue: sourceUrl,
        responseType: '',
        seasons: [],
        seasonValue: '',
        movieFiles: [],
        episodeFiles: [],
        voiceLinks: [],
        voiceValue: '',
        popupOpen: false
    });

    try {
        const json = await fetchSourceJson(sourceUrl, state.media);

        if (json.type === 'movie') {
            patchState({
                responseType: 'movie',
                movieFiles: json.data || []
            });
            return;
        }

        if (json.type === 'season') {
            patchState({
                responseType: 'season',
                seasons: json.data || [],
                seasonValue: json.data?.[0]?.url || ''
            });

            if (json.data?.[0]) {
                await selectSeason(json.data[0].url);
            }

            return;
        }

        if (json.type === 'episode') {
            const voiceLinks = json.voice || [];
            const activeVoice = voiceLinks.find((item) => item.active) || voiceLinks[0];

            patchState({
                loading: false,
                responseType: 'episode',
                episodeFiles: json.data || [],
                voiceLinks,
                voiceValue: activeVoice?.url || ''
            });

            return;
        }

        throw new Error(`Unsupported source response type: ${json.type}`);
    } catch (error) {

        patchState({
            error: getErrorMessage(error),
            responseType: '',
            seasons: [],
            seasonValue: '',
            movieFiles: [],
            episodeFiles: [],
            voiceLinks: [],
            voiceValue: ''
        });
    } finally {
        patchState({ loading: false });
    }
}

export async function selectSeason(seasonUrl) {
    patchState({
        loading: true,
        error: '',
        seasonValue: seasonUrl,
        episodeFiles: [],
        voiceLinks: [],
        voiceValue: '',
        popupOpen: false
    });

    try {
        const json = await fetchUrlJson(seasonUrl);
        await loadTmdbSeason();

        const voiceLinks = json.voice || [];
        const activeVoice = voiceLinks.find((item) => item.active) || voiceLinks[0];

        patchState({
            loading: false,
            responseType: 'episode',
            episodeFiles: json.data || [],
            voiceLinks,
            voiceValue: activeVoice?.url || ''
        });
    } catch (error) {

        patchState({
            error: getErrorMessage(error),
            episodeFiles: [],
            voiceLinks: []
        });
    } finally {
        patchState({ loading: false });
    }
}

export async function selectVoice(voiceUrl) {
    patchState({
        loading: true,
        error: '',
        voiceValue: voiceUrl,
        episodeFiles: [],
        popupOpen: false
    });

    try {
        const json = await fetchUrlJson(voiceUrl);

        if (json.type !== 'episode') {
            throw new Error(`Unsupported voice response type: ${json.type}`);
        }

        const voiceLinks = json.voice || state.voiceLinks || [];
        const activeVoice = voiceLinks.find((item) => item.active) ||
            voiceLinks.find((item) => item.url === voiceUrl) ||
            voiceLinks[0];

        patchState({
            responseType: 'episode',
            episodeFiles: json.data || [],
            voiceLinks,
            voiceValue: activeVoice?.url || voiceUrl
        });
    } catch (error) {

        patchState({
            error: error instanceof Error ? error.message : String(error || 'Unknown error'),
            episodeFiles: []
        });
    } finally {
        patchState({ loading: false });
    }
}

export async function playMovieFile(file) {
    var streamUrl = file.stream || file.url;
    if (!file.stream && file.method === 'call') {
        const response = await fetch(file.url);
        var call = await response.json();
        streamUrl = call.url;
    }

    PotokSDK.ui.playVideo({
        streamUrl: streamUrl,
        streamType: streamUrl.includes('.m3u8')
            ? 'm3u8'
            : streamUrl.includes('.mpd')
                ? 'dash'
                : 'mp4',
        title: file.title,
        mediaType: 'movie'
    });
}

export async function playEpisodeFile(file) {
    var streamUrl = file.stream || file.url;
    if (!file.stream && file.method === 'call') {
        const response = await fetch(file.url);
        var call = await response.json();
        streamUrl = call.url;
    }

    PotokSDK.ui.playVideo({
        streamUrl: streamUrl,
        streamType: streamUrl.includes('.m3u8')
            ? 'm3u8'
            : streamUrl.includes('.mpd')
                ? 'dash'
                : 'mp4',
        title: file.title,
        mediaType: 'tv',
        season: file.s,
        episode: file.e
    });
}


async function loadTmdbSeason() {
    const seasonNumber = getSelectedSeasonNumber();
    const tmdbId = getTmdbId();

    const gatewayURL = PotokSDK.config.gatewayURL;
    const response = await fetch(`${gatewayURL}/api/media/tmdb/tv/${tmdbId}/season/${seasonNumber}`);

    const data = await response.json();

    state.tmdbEpisodes = data.episodes.map((ep) => ({
        id: ep.id,
        episodeNumber: ep.episodeNumber,
        seasonNumber,
        name: ep.name,
        overview: ep.overview,
        stillPath: ep.stillPath || ep.still_path,
        airDate: ep.airDate
    }));
}

function getTmdbId() {
    return Number(
        state.media.tmdbId ||
        state.media.tmdb_id ||
        state.media.id
    );
}

function getSelectedSeasonNumber() {
    const selected = state.seasons.find((item) => item.url === state.seasonValue);
    return Number(selected?.id || selected?.s || 1);
}

function getErrorMessage(error) {
    return error instanceof Error
        ? error.message
        : String(error || 'Unknown error');
}