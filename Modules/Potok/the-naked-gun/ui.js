import { PotokSDK } from 'potok-sdk';
import { state } from './state.js';
import {
    selectSource,
    selectSeason,
    selectVoice,
    playMovieFile,
    playEpisodeFile
} from './actions.js';

const {
    VStack,
    HStack,
    Grid,
    Text,
    Select,
    Button,
    Divider,
    LoadingSpinner,
    EpisodeCard,
    Spacer
} = PotokSDK.ui.components;

function buildSourceSelect() {
    return Select('the-naked-gun-source')
        .options(
            state.sources.map((item) => ({
                value: item.url,
                label: item.name
            }))
        )
        .value(state.sourceValue)
        .onChange(selectSource);
}

function buildSeasonSelect() {
    return Select('the-naked-gun-season')
        .options(
            state.seasons.map((item) => ({
                value: item.url,
                label: item.name
            }))
        )
        .value(state.seasonValue)
        .onChange(selectSeason);
}

function buildMovieFiles() {
    return VStack()
        .spacing(20)
        .children(
            state.movieFiles.map((file, index) =>
                Button(file.translate || file.title || `auto`)
                    .height('45px')
                    .variant('secondary')
                    .onClick(() => playMovieFile(file))
            )
        );
}

function buildEpisodeFiles() {
    return Grid()
        .minWidth('375px')
        .gap('30px')
        .children(
            state.episodeFiles.map((file, index) =>
                buildEpisodeCard(file, index)
            )
        );
}

function buildVoiceSelect() {
    return Select('the-naked-gun-voice')
        .options(
            state.voiceLinks.map((item) => ({
                value: item.url,
                label: item.name
            }))
        )
        .value(state.voiceValue)
        .onChange(selectVoice);
}

function buildEpisodeCard(file, index) {
    const tmdbEpisode = state.tmdbEpisodes.find((item) =>
        Number(item.episodeNumber) === Number(file.e || index + 1)
    );

    const episode = {
        id: tmdbEpisode?.id || `${file.s || 1}:${file.e || index + 1}`,
        episodeNumber: tmdbEpisode?.episodeNumber || file.e || index + 1,
        seasonNumber: tmdbEpisode?.seasonNumber || file.s || getSelectedSeasonNumber(),
        name: tmdbEpisode?.name || file.name || file.title || `${file.e || index + 1} серия`,
        overview: tmdbEpisode?.overview || '',
        stillPath: tmdbEpisode?.stillPath || tmdbEpisode?.still_path || '',
        airDate: tmdbEpisode?.airDate || ''
    };

    return EpisodeCard()
        .episode(episode)
        .width('375px')
        .onClick(() => playEpisodeFile(file));
}

function getSelectedSeasonNumber() {
    const selected = state.seasons.find((item) => item.url === state.seasonValue);
    return selected?.season || selected?.s || 1;
}

export function buildPage() {
    if (!state.media) {
        return VStack()
            .spacing(16)
            .children([
                Text('Откройте карточку фильма или сериала и нажмите кнопку The Naked Gun.')
                    .variant('secondary')
            ]);
    }

    const children = [
        Text(state.media.title).bold(true).size('lg'),
        Text(state.media.subtitle).variant('secondary')
    ];

    if (state.error) {
        children.push(
            Text(`Ошибка: ${state.error}`)
                .variant('secondary')
        );

        children.push(Divider());
    }

    if (state.loading) {
        children.push(
            LoadingSpinner()
                .message('Загрузка...')
                .height('120px')
        );
    } else {
        const selects = [buildSourceSelect()];

        if (state.seasons.length > 0) {
            selects.push(buildSeasonSelect());
        }

        if (state.voiceLinks.length > 0) {
            selects.push(buildVoiceSelect());
        }

        children.push(
            HStack()
                .spacing(12)
                .children(selects)
        );

        children.push(Divider());

        if (state.responseType === 'movie') {
            children.push(buildMovieFiles());
        }

        if (state.responseType === 'episode') {
            children.push(buildEpisodeFiles());
        }
    }

    return VStack()
        .spacing(15)
        .children(children);
}