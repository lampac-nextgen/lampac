import { PotokSDK } from 'potok-sdk';

export const state = PotokSDK.createState({
    media: null,
    loading: false,
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
    tmdbEpisodes: [],

    popupOpen: false
});

export function patchState(patch) {
    Object.assign(state, patch);
}
