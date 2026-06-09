import { PotokSDK } from 'potok-sdk';

const {
    VStack,
    HStack,
    Button,
    Grid,
    EpisodeCard,
    MediaPlayer,
    LoadingSpinner,
    Select,
    Spacer
} = PotokSDK.ui.components;


const PLUGIN_ID = 'the-blue-oyster';
const SOURCES_URL = '{localhost}/sisi';

let API_URL = '';
let channels = [];
let menuItems = [];
let selectedMenuIndexes = {};
let mediaPlayerPlayback = null;
let mediaPlayerPlaybackPending = false;

await PotokSDK.storage.local.setItem('disableHttpProxy', true);

const state = PotokSDK.createState({
    loading: true,
    error: null,
    items: [],
    page: 1,
    endReached: false,
    mediaPlayerKey: 0
});

PotokSDK.registerSlotContribution({
    id: PLUGIN_ID,
    slotName: 'extension-page',
    render() {
        return {
            label: 'The Blue Oyster',
            layout: buildPageLayout()
        };
    }
});

PotokSDK.registerSlotContribution({
    id: `${PLUGIN_ID}-sidebar`,
    slotName: 'sidebar-menu-home',
    render() {
        return {
            label: 'The Blue Oyster',
            layout: Button('The Blue Oyster')
                .variant('sidebar-item')
                .icon('beer')
                .onClick(() => {
                    PotokSDK.ui.navigateTo(`/extensions/${PLUGIN_ID}`);
                })
        };
    }
});

state.$subscribe(() => {
    PotokSDK.ui.render(buildPageLayout(), PLUGIN_ID);
});


async function initPlugin() {
    try {
        state.loading = true;

        const data = await fetchJson(SOURCES_URL);

        channels = data.channels
            .filter((channel) => Number(channel.displayindex) > 2)
            .map((channel) => {
                return {
                    value: channel.playlist_url,
                    label: channel.title
                };
            });

        API_URL = channels[0].value;
        state.selectedChannelValue = channels[0].value;

    } catch (error) {
        state.error = String(error?.message || error);
        state.loading = false;
        state.endReached = true;
        PotokSDK.ui.showHUD('error', state.error);
    }
}


function buildPageLayout() {
    const gridChildren = [];
    const pageChildren = [];

    pageChildren.push(
        VStack()
            .id('source-select-wrapper')
            .children([
                VStack()
                    .id('source-select-bottom-spacer')
                    .height('10px'),

                HStack()
                    .id('source-select-row')
                    .spacing(10)
                    .children([
                        Select('source-select')
                            .options(channels)
                            .value(state.selectedChannelValue)
                            .onChange((value) => {
                                API_URL = value;
                                state.selectedChannelValue = value;

                                state.items = [];
                                state.page = 1;
                                state.endReached = false;

                                menuItems = [];
                                selectedMenuIndexes = {};

                                mediaPlayerPlayback = null;
                                mediaPlayerPlaybackPending = false;

                                loadPage(1);
                            }),

                        ...buildMenuSelects()
                    ]),

                VStack()
                    .id('source-select-bottom-spacer')
                    .height('15px')
            ])
    );

    if (mediaPlayerPlayback) {
        pageChildren.push(buildMediaPlayerBlock());
    }

    state.items.forEach((item, index) => {
        const episode = {
            name: item.name || `Видео ${index + 1}`,
            stillPath: item.picture || ''
        };

        gridChildren.push(
            EpisodeCard()
                .id(`item-card-${index}`)
                .episode(episode)
                .width('375px')
                .onClick(() => {
                    playItem(item, episode);
                })
        );
    });

    pageChildren.push(
        Grid()
            .id('catalog-grid')
            .minWidth('375px')
            .gap('20px')
            .children(gridChildren)
    );

    if (state.loading) {
        pageChildren.push(
            LoadingSpinner()
                .id('catalog-spinner')
                .message('Загрузка...')
        );
    }

    if (!state.endReached && !state.loading) {
        pageChildren.push(
            VStack()
                .id('load-more-wrapper')
                .children([
                    VStack()
                        .id('load-more-top-spacer')
                        .height('15px'),

                    Button('Загрузить ещё')
                        .id('load-more-button')
                        .variant('secondary')
                        .width('100%')
                        .height('50px')
                        .onClick(() => {
                            loadPage(state.page + 1);
                        })
                ])
        );
    }

    pageChildren.push(
        VStack()
            .id('bottom-spacer')
            .height('15px')
    );

    return VStack()
        .id('catalog-page')
        .spacing(12)
        .children(pageChildren);
}


function buildMenuSelects() {
    return menuItems
        .filter((menuItem) => menuItem.playlist_url === 'submenu')
        .map((menuItem, menuIndex) => {
            const submenuOptions = menuItem.submenu.map((item, submenuIndex) => {
                return {
                    value: String(submenuIndex),
                    label: item.title
                };
            });

            const selectedIndex = selectedMenuIndexes[menuIndex] || 0;

            return Select(`menu-select-${menuIndex}`)
                .options(submenuOptions)
                .value(String(selectedIndex))
                .onChange((value) => {
                    const submenuIndex = Number(value);
                    const selectedItem = menuItem.submenu[submenuIndex];

                    selectedMenuIndexes[menuIndex] = submenuIndex;
                    API_URL = selectedItem.playlist_url;

                    state.items = [];
                    state.page = 1;
                    state.endReached = false;

                    mediaPlayerPlayback = null;
                    mediaPlayerPlaybackPending = false;

                    loadPage(1);
                });
        });
}


function buildMediaPlayerBlock() {
    const player = MediaPlayer()
        .id(`media-player-${state.mediaPlayerKey}`)
        .isNetworkOffline(false);

    if (mediaPlayerPlaybackPending && mediaPlayerPlayback) {
        player.playback(mediaPlayerPlayback);
        mediaPlayerPlaybackPending = false;
    }

    return player;
}


async function loadPage(page) {
    try {
        state.loading = true;

        const separator = API_URL.includes('?') ? '&' : '?';
        const url = `${API_URL}${separator}pg=${page}&initial=potok`;
        const data = await fetchJson(url);

        if (!data.list.length) {
            state.endReached = true;
        }

        state.items.push(...data.list);
        menuItems = data.menu || [];
        state.page = page;

        const totalPages = Number(data.total_pages || 10);

        if (totalPages > 0 && page >= totalPages) {
            state.endReached = true;
        }

        state.loading = false;
    } catch (error) {
        state.error = String(error?.message || error);
        state.loading = false;
        state.endReached = true;
        PotokSDK.ui.showHUD('error', state.error);
    }
}


async function fetchJson(url) {
    const response = await fetch(url, {
        method: 'GET',
        headers: {
            Accept: 'application/json'
        }
    });

    if (!response.ok) {
        throw new Error(`API error: ${response.status}`);
    }

    return await response.json();
}


async function playItem(item, episode) {
    try {
        var streamUrl = null;

        if (item.json === true) {
            const data = await fetchJson(item.video + '&initial=potok');
            const qualityMap = data.qualitys_proxy || data.qualitys;
            streamUrl = Object.values(qualityMap)[0];
        }

        mediaPlayerPlayback = {
            streamUrl: streamUrl || item.video,
            title: episode.name,
            mediaType: 'movie'
        };

        mediaPlayerPlaybackPending = true;
        state.mediaPlayerKey += 1;
    } catch (error) {
        state.error = String(error?.message || error);
        PotokSDK.ui.showHUD('error', state.error);
    }
}


await initPlugin();
loadPage(1);