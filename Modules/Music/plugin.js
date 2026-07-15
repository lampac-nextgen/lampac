(function () {
    'use strict';

    /*
     * ======================================================================
     * Music-модуль Lampac — весь клиент для Lampa одним файлом.
     * Сервер отдаёт его как /music.js (шаблоны {localhost}/{client_debug_enabled}
     * подставляются при отдаче). Серверная часть: Modules/Music (C#).
     * Обзор архитектуры: README.md, docs/client-architecture.md.
     *
     * КАРТА СЕКЦИЙ — навигация по маркерам "// ===== ИМЯ =====":
     *   CORE / CONFIG                 endpoints, storage-ключи, глобальное состояние, SVG-иконки
     *   TRANSPORT / COMMON HELPERS    request/requestPost, uid, трейсы и heat-метрики, кэш-хелперы
     *   PLAYBACK SETTINGS / PROVIDERS режим качества/воспроизведения, выбор audio-провайдера
     *   ARTWORK / LOCAL STATE         генеративные SVG-обложки, recent-полки, закладки
     *   AUTH                          вход во внешние сервисы (SoundCloud OAuth)
     *   ENTITY NAVIGATION             переходы на экраны артиста/альбома/секции
     *   QUEUE / IOS PLAYBACK          очередь, снапшот/restore, standalone <audio>,
     *                                 keep-alive, Media Session, watchdog
     *   IOS FULL PLAYER               фулл-плеер: UI, свайпы, шиты, лирика, sleep-таймер
     *   SEARCH DATA                   поиск, история запросов, догрузка «Ещё»
     *   LAMPA PLAYER BRIDGE           embedded-режим (player=inner) поверх Lampa.PlayerVideo
     *   USER PLAYLISTS                плейлисты, импорт по ссылке, контекст-меню
     *   HOME / CARD UI                главный экран, карточки, MusicComponent
     *   FULL SCREEN COMPONENTS        экраны entries/section/artist/album/bookmarks
     *   REGISTRATION / STYLES         add()/createMusic(): пункт меню, компоненты, CSS
     *
     * ТОЧКИ ВХОДА (откуда Lampa вызывает этот код):
     *   1. createMusic() в самом низу файла — регистрирует шаблон стилей и
     *      Lampa-компоненты, после app ready вызывает add().
     *   2. add() — добавляет пункт меню «Музыка», подписывается на
     *      Lampa.Player start/destroy (мост embedded-режима).
     *   3. Зарегистрированные компоненты (lampac_music_home/search/section/
     *      artist/album/entries/bookmarks) — Lampa создаёт их через Activity.push.
     *   4. Обработчики Media Session (play/pause/seekto/prev/next) — iOS
     *      дергает их с lock screen / из пункта управления.
     *
     * ДВА РЕЖИМА ВОСПРОИЗВЕДЕНИЯ — ДВЕ РАЗНЫЕ РЕАЛЬНОСТИ, НЕ СМЕШИВАТЬ:
     *   player=inner — музыка играет в штатном Lampa.PlayerVideo
     *                  (секция LAMPA PLAYER BRIDGE). Lock screen iOS:
     *                  play/pause/next/prev, перемотки НЕТ намеренно —
     *                  см. docs/ios-embedded-lockscreen-seek.md.
     *   player=ios   — собственный <audio>-элемент и свой UI (секции
     *                  QUEUE / IOS PLAYBACK + IOS FULL PLAYER). Полный
     *                  lock screen, включая скраббер перемотки. Весь слой
     *                  iOS-хаков (warmup/keep-alive/lock-kick) — только здесь.
     *
     * ЛЕГЕНДА ИМЕНОВАНИЙ:
     *   MUSIC_*             модульное глобальное состояние (паспорта — у объявлений)
     *   standaloneIos*      standalone iOS-плеер (player=ios)
     *   MUSIC_EMBEDDED_IOS / embedded-функции — embedded-режим (player=inner)
     *   mapXxxCard          DTO сервера -> карточка полки (entry)
     *   buildXxx            конструкторы DOM/URL/DTO
     *   openXxx             навигация (Activity.push, шиты, меню)
     *   traceStandaloneIosAudio / bumpMusicHeatMetric — отладка; шлют на
     *                       /music/clientlog ТОЛЬКО при client_debug_enabled
     *
     * ОСНОВНЫЕ DTO (источник правды — Models/Contracts.cs на сервере):
     *
     * @typedef {Object} MusicTrack трек каталога
     *   id           {string}   "провайдер-скоуп" ид: mb:..., sc:..., spotify:track:...
     *   title        {string}
     *   artist_id / artist_name {string}
     *   artists      {string[]}
     *   album_id / album_title  {string}
     *   duration_ms  {?number}  может отсутствовать (напр. VK-чарт) —
     *                           тогда бэкфиллится из selected_match при резолве
     *   images       {Array}    [{url, ...}]; url уже проксирован сервером
     *
     * @typedef {Object} MusicAudioMatch кандидат audio-источника
     *   provider_id  {string}   youtubeaudio | sefonaudio | soundcloudaudio | z3fmaudio
     *   id           {string}
     *   duration_ms  {?number}
     *   pinned       {boolean}  выбран пользователем вручную: обходит эвристики
     *                           релевантности и не перезаписывается авто-подбором
     *
     * Ответ /music/play: { available, track, selected_match: MusicAudioMatch,
     *   sources: [{ url, external_url, headers, ... }] } — url ведёт на
     *   серверный relay /music/stream, напрямую на CDN клиент не ходит.
     *
     * entry (карточка полки, см. mapTrackCard и соседей):
     *   { type: 'track'|'album'|'artist'|'playlist'|'query',
     *     id, title, subtitle, badge, image, background, raw }
     * ======================================================================
     */

    // ===== CORE / CONFIG =====

    if (window.plugin_lampac_music_ready) return;
    window.plugin_lampac_music_ready = true;

    var MUSIC = {
        version: '0.3.0',
        title: 'Музыка',
        endpoints: {
            home: '{localhost}/music/home',
            section: '{localhost}/music/section',
            search: '{localhost}/music/search',
            providers: '{localhost}/music/providers',
            artist: '{localhost}/music/artist',
            artistSection: '{localhost}/music/artistsection',
            artistImage: '{localhost}/music/artistimg',
            album: '{localhost}/music/album',
            play: '{localhost}/music/play',
            playlist: '{localhost}/music/playlist.m3u',
            matches: '{localhost}/music/matches',
            selectMatch: '{localhost}/music/match/select',
            resetMatch: '{localhost}/music/match/reset',
            lyrics: '{localhost}/music/lyrics',
            playlists: '{localhost}/music/playlists',
            playlistTracks: '{localhost}/music/playlists/tracks',
            playlistCreate: '{localhost}/music/playlists/create',
            playlistDelete: '{localhost}/music/playlists/delete',
            playlistImport: '{localhost}/music/playlists/import',
            playlistSync: '{localhost}/music/playlists/sync',
            playlistTrackAdd: '{localhost}/music/playlists/track/add',
            playlistTrackRemove: '{localhost}/music/playlists/track/remove',
            playlistTrackMove: '{localhost}/music/playlists/track/move',
            statsTop: '{localhost}/music/stats/top',
            radio: '{localhost}/music/radio',
            markHistory: '{localhost}/music/history/mark',
            removeHistory: '{localhost}/music/history/remove',
            authState: '{localhost}/music/auth/state',
            authSave: '{localhost}/music/auth/save',
            authLogout: '{localhost}/music/auth/logout',
            clientLog: '{localhost}/music/clientlog'
        },
        storage: {
            last_query: 'lampac_music_last_query',
            recent_queries: 'lampac_music_recent_queries',
            recent_albums: 'lampac_music_recent_albums',
            recent_artists: 'lampac_music_recent_artists',
            bookmarked_tracks: 'lampac_music_bookmarked_tracks',
            bookmarked_albums: 'lampac_music_bookmarked_albums',
            repeat_mode: 'lampac_music_repeat_mode',
            shuffle: 'lampac_music_shuffle',
            bookmarked_artists: 'lampac_music_bookmarked_artists',
            quality_mode: 'lampac_music_quality_mode',
            playback_mode: 'lampac_music_playback_mode',
            audio_provider: 'lampac_music_audio_provider',
            player: 'lampac_music_player',
            radio_autoplay_enabled: 'lampac_music_radio_autoplay_enabled',
            queue_restore_enabled: 'lampac_music_queue_restore_enabled',
            queue_snapshot: 'lampac_music_queue_snapshot', // legacy v1: только чтение при миграции
            queue_blob_v2: 'lampac_music_queue_blob_v2',
            queue_position_v2: 'lampac_music_queue_position_v2'
        },
        art: {
            cover: 'https://coverartarchive.org/release-group/'
        },
        features: {
            auth: false
        }
    };

    var MUSIC_QUEUE = {
        tracks: [],
        currentIndex: 0,
        currentTrackId: null
    };
    var MUSIC_RADIO_STATE = {
        pending: false,
        lastGeneration: '',
        lastRequestAt: 0
    };
    var MUSIC_HISTORY_MARK_DELAY = 10000;
    var MUSIC_CLIENT_TRACE_ENABLED = '{client_debug_enabled}' === 'true';
    var MUSIC_HEAT_PROBE = {
        timer: 0,
        interval: 10000,
        seq: 0,
        startedAt: 0,
        lastFlushAt: 0,
        counters: {}
    };
    var MUSIC_LAST_FOCUS_CONTEXT = null;
    var MUSIC_HOME_REFRESH_RESTORE_BLOCK_UNTIL = 0;
    var MUSIC_DEFERRED_HOME_REFRESH = false;
    var MUSIC_LAST_PLAYER_CONTROLLER_RESTORE_AT = 0;
    var MUSIC_PLAYBACK_LOADING_ACTIVE = false;
    var MUSIC_PLAYBACK_LOADING_TIMER = 0;
    /*
     * ПАСПОРТ: состояние standalone iOS-плеера (player=ios). Главный неявный
     * автомат файла — почти все iOS-хаки читают/пишут эти поля.
     *
     * Жизненный цикл: startStandaloneIosAudioPlayback (жест пользователя!) ->
     * active=true, играем очередь -> stopStandaloneIosAudio -> active=false.
     *
     *   audio            сам <audio>-элемент; живёт от старта до stop
     *   active           плеер владеет воспроизведением (бар/фулл-плеер видимы)
     *   playlist/tracks  очередь: playlist — исходный порядок, tracks — рабочий
     *   currentIndex     индекс текущего трека в tracks
     *   switching        true на время смены трека: глушит watchdog/synthetic-ended,
     *                    чтобы переходное состояние не приняли за зависание
     *   playing          наше представление "звук идёт" (не то же, что !audio.paused:
     *                    iOS умеет замораживать currentTime при играющем флаге)
     *   shuffleOrder     ленивая пермутация индексов при shuffle (null = выкл)
     *
     * Media Session / lock screen:
     *   mediaSessionArmed   обработчики play/pause/seekto/prev/next навешены.
     *                       Регистрируются ОДИН раз ДО первого play() — iOS
     *                       фиксирует возможности Now Playing при создании сессии
     *   mediaSessionTrackId для какого трека выставлены metadata/artwork
     *   lastPositionSync    троттлинг setPositionState
     *
     * Keep-alive (удержание аудиосессии на паузе, см. секцию keep-alive):
     *   keepAlive           <audio>-луп тишины (legacy-путь, флаг WEBAUDIO=false)
     *   keepAliveCtx        AudioContext (основной путь, WEBAUDIO=true)
     *   keepAliveActive     луп/контекст сейчас держит сессию
     *   keepAliveGestureArmed  gesture-unlock слушатели навешены
     *
     * Watchdog / восстановление:
     *   prepareToken / playWatchToken  токены-инварианты против гонок: асинхронный
     *                    колбэк сверяет токен и молча выходит, если началась
     *                    новая операция (смена трека, новый watch)
     *   resumePosition   позиция для восстановления после recovery/restore
     *   lastPlayingAt    метка последнего реального прогресса (для watchdog)
     *
     * UI-троттлинг (не перерисовывать бар/фулл-плеер без изменений):
     *   lastUiUpdate, barUiKey, barProgressKey, fullUiKey, fullPlaybackKey,
     *   fullProgressKey — ключи "что уже нарисовано"
     *
     * Отладка: traceSeq, lastTraceTimeupdate (только при client_debug_enabled).
     * Служебное: timeupdateHandler/timeupdateAttached (attach/detach ровно одного
     * слушателя), lifecycleBound (visibility/pagehide-трейсы навешены один раз).
     */
    var MUSIC_IOS_AUDIO = {
        audio: null,
        active: false,
        playlist: [],
        tracks: [],
        currentIndex: -1,
        switching: false,
        playing: false,
        mediaSessionTrackId: null,
        lastPositionSync: 0,
        prepareToken: 0,
        playWatchToken: 0,
        keepAlive: null,
        keepAliveCtx: null,
        keepAliveActive: false,
        shuffleOrder: null,
        keepAliveGestureArmed: false,
        mediaSessionArmed: false,
        resumePosition: 0,
        lastPlayingAt: 0,
        traceSeq: 0,
        lastTraceTimeupdate: 0,
        lastUiUpdate: 0,
        barUiKey: '',
        barProgressKey: '',
        fullUiKey: '',
        fullPlaybackKey: '',
        fullProgressKey: '',
        timeupdateHandler: null,
        timeupdateAttached: false,
        lifecycleBound: false
    };
    var MUSIC_IOS_BAR = null;
    var MUSIC_IOS_FULL_PLAYER = null;
    var MUSIC_IOS_FULL_PLAYER_OPEN = false;
    var MUSIC_IOS_FULL_PLAYER_RETURN_CONTROLLER = 'content';
    var MUSIC_IOS_SLEEP_TIMER = {
        timer: 0,
        endAt: 0
    };
    /*
     * ПАСПОРТ: embedded-режим (player=inner) — музыка в штатном Lampa.PlayerVideo,
     * мы только дополняем его Media Session-метаданными и next/prev.
     * НЕ управляет audio-элементом: media owner — сам Lampa-плеер.
     *   active            embedded-мост включён (играет музыкальный контент)
     *   mediaSessionArmed play/pause/prev/next навешены (seekto тут НЕ вешаем —
     *                     см. docs/ios-embedded-lockscreen-seek.md)
     *   lastData          последний data от Lampa.Player start (метаданные трека)
     *   playWatchToken / switchToken  анти-гонки, как в MUSIC_IOS_AUDIO
     */
    var MUSIC_EMBEDDED_IOS = {
        active: false,
        lifecycleBound: false,
        mediaSessionArmed: false,
        playWatchToken: 0,
        lastData: null,
        traceSeq: 0,
        switchToken: 0
    };
    var MUSIC_CLIENT_DEBUG_ERRORS_BOUND = false;
    var MUSIC_LAMPA_PLAYER_DEBUG_BOUND = false;
    var PLAY_PREFETCH_CACHE = {};
    var PLAY_PREFETCH_PENDING = {};
    var PLAY_PREFETCH_TIMER = 0;
    var PLAY_PREFETCH_TTL = 1000 * 60 * 10;
    var MUSIC_QUEUE_SNAPSHOT_VERSION = 1; // legacy-формат, принимается только при чтении
    var MUSIC_QUEUE_BLOB_VERSION = 2;
    var MUSIC_QUEUE_SNAPSHOT_LIMIT = 500;
    var MUSIC_QUEUE_SNAPSHOT_TTL = 1000 * 60 * 60 * 24 * 14;
    var MUSIC_QUEUE_SNAPSHOT_SAVE_DELAY = 4000;
    // за сколько позиций до края сохранённого окна переписываем blob целиком
    var MUSIC_QUEUE_WINDOW_EDGE_MARGIN = 50;
    /*
     * Снапшот v2 разнесён на два ключа, чтобы частый 4-секундный тик не
     * пересобирал и не сериализовывал всю очередь (до 500 треков):
     *   queue_blob_v2     — окно треков + режимы; пишется ТОЛЬКО по событиям
     *                       (смена трека/состава, toggles, край окна)
     *   queue_position_v2 — крошечная запись позиции; пишется каждые 4s
     * Связка через snapshotId: позиция от чужого поколения блоба игнорируется.
     * MUSIC_QUEUE_BLOB_STATE — что лежит в записанном блобе (для тика позиции).
     */
    var MUSIC_QUEUE_BLOB_STATE = {
        snapshotId: '',
        windowStart: 0,
        windowLength: 0,
        truncated: false,
        seq: 0,
        legacyCleared: false
    };
    /*
     * ПАСПОРТ: восстановление очереди после перезапуска клиента.
     * Формат v2 — два ключа Storage (см. комментарий у MUSIC_QUEUE_BLOB_STATE):
     * blob окна очереди пишется по событиям (force), позиция — раз в 4s.
     * Читается при старте (v2, fallback на legacy v1); TTL 14 дней по свежести
     * позиции. Плейлисты >500 треков усечены окном вокруг текущего.
     *   available  валидный снапшот прочитан, restore-путь доступен
     *   position   сохранённый currentTime трека
     *   timer      отложенная запись позиции (0 = нет ожидающей)
     */
    var MUSIC_QUEUE_RESTORE = {
        available: false,
        position: 0,
        updatedAt: 0,
        timer: 0
    };

    var MUSIC_HOME_CACHE = {};
    var MUSIC_HOME_SECTION_TITLES = {};
    var MUSIC_HOME_SECTION_META = {};
    var SEARCH_EXPANDED_CACHE = {};
    var SEARCH_EXPANDED_PENDING = {};
    var SEARCH_EXPANDED_WAITERS = {};
    var HOME_SECTION_LIMIT = 20;
    var RECENT_SECTION_STORAGE_LIMIT = 100;
    var RECENT_QUERY_QUICK_LIMIT = 8;
    var MUSIC_RECENT_EVENT = 'lampac_music_recent_changed';
    var ARTIST_IMAGE_CACHE = {};
    var ARTIST_IMAGE_PENDING = {};
    var ARTIST_IMAGE_QUEUE = [];
    var ARTIST_IMAGE_ACTIVE = 0;
    var ARTIST_IMAGE_CONCURRENCY = 1;
    var MUSIC_AUDIO_PROVIDERS = null;
    var MUSIC_AUDIO_PROVIDERS_PENDING = false;
    var MUSIC_AUDIO_PROVIDER_WAITERS = [];
    // сводка «Твоего топа»: checkedAt — троттлинг проверки unlock (60s),
    // summary — последний ответ /music/stats/top (для hero-меты экрана)
    var MUSIC_STATS_TOP = { checkedAt: 0, summary: null };

    var IMG_BG = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAADUlEQVR42gECAP3/AAAAAgABUyucMAAAAABJRU5ErkJggg==';
    var MENU_ICON = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M12 4V14.5C11.4134 14.1819 10.7413 14 10 14C7.79086 14 6 15.7909 6 18C6 20.2091 7.79086 22 10 22C12.2091 22 14 20.2091 14 18V8H18V4H12Z" fill="currentColor"/></svg>';
    var SEARCH_ICON = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M10.5 4a6.5 6.5 0 1 0 4.06 11.58l3.43 3.43 1.41-1.41-3.43-3.43A6.5 6.5 0 0 0 10.5 4Zm0 2a4.5 4.5 0 1 1 0 9 4.5 4.5 0 0 1 0-9Z" fill="currentColor"/></svg>';
    var FILTER_ICON = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M4 7h16v2H4V7Zm0 5h16v2H4v-2Zm0 5h16v2H4v-2Z" fill="currentColor"/></svg>';
    var BOOKMARK_ICON = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M7 4h10a1 1 0 0 1 1 1v15l-6-3.4L6 20V5a1 1 0 0 1 1-1Z" fill="currentColor"/></svg>';
    var HOME_ICON = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M12 4 4 10.2V20h5v-5h6v5h5v-9.8L12 4Z" fill="currentColor"/></svg>';
    var IOS_PLAYER_PREV_ICON = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M7 6h2v12H7V6Zm10.5 1.2v9.6c0 .8-.9 1.28-1.56.82L10 13.4a1 1 0 0 1 0-1.64l5.94-4.18c.66-.46 1.56.02 1.56.82Z" fill="currentColor"/></svg>';
    var IOS_PLAYER_PLAY_ICON = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M8.8 6.9v10.2c0 .8.87 1.3 1.56.87l7.23-5.1a1.05 1.05 0 0 0 0-1.78l-7.23-5.1c-.69-.43-1.56.06-1.56.91Z" fill="currentColor"/></svg>';
    var IOS_PLAYER_PAUSE_ICON = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M8 6.5h3V17.5H8V6.5Zm5 0h3V17.5H13V6.5Z" fill="currentColor"/></svg>';
    var IOS_PLAYER_NEXT_ICON = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M15 6h2v12h-2V6ZM6.5 7.2v9.6c0 .8.9 1.28 1.56.82L14 13.4a1 1 0 0 0 0-1.64L8.06 7.58c-.66-.46-1.56.02-1.56.82Z" fill="currentColor"/></svg>';
    var IOS_PLAYER_QUEUE_ICON = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M5 7.5h10M5 12h10M5 16.5h7M17.5 15v-5m0 0-2 2m2-2 2 2" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"/></svg>';
    var IOS_PLAYER_CLOSE_ICON = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="m8.46 8.47 7.07 7.06m0-7.06-7.07 7.06" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"/></svg>';
    var IOS_PLAYER_DOWN_ICON = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="m7 10 5 5 5-5" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>';
    var IOS_PLAYER_SOURCE_ICON = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M7 7h10l-2.5 3M17 17H7l2.5-3M5.5 9.5A6.5 6.5 0 0 1 17 7m1.5 7.5A6.5 6.5 0 0 1 7 17" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"/></svg>';
    var IOS_PLAYER_TIMER_ICON = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M12 7v5l3 2M9 3h6M12 21a8 8 0 1 0 0-16 8 8 0 0 0 0 16Z" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"/></svg>';
    var IOS_PLAYER_LYRICS_ICON = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M12 2a3 3 0 0 0-3 3v6a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z" stroke="currentColor" stroke-width="1.9" stroke-linejoin="round"/><path d="M19 10v1a7 7 0 0 1-14 0v-1M12 18v3" stroke="currentColor" stroke-width="1.9" stroke-linecap="round"/></svg>';
    var IOS_PLAYER_SHUFFLE_ICON ='<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M16 3h5v5M4 20 21 3M21 16v5h-5M15 15l6 6M4 4l5 5" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"/></svg>';
    var IOS_PLAYER_RADIO_ICON = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M6.4 8.7C4.5 8.7 3 10.2 3 12s1.5 3.3 3.4 3.3c3.3 0 7.9-6.6 11.2-6.6 1.9 0 3.4 1.5 3.4 3.3s-1.5 3.3-3.4 3.3c-3.3 0-7.9-6.6-11.2-6.6Z" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"/></svg>';
    var IOS_PLAYER_REPEAT_ICON = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="m17 1 4 4-4 4M3 11V9a4 4 0 0 1 4-4h14M7 23l-4-4 4-4M21 13v2a4 4 0 0 1-4 4H3" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"/></svg>';
    var IOS_PLAYER_REPEAT_ONE_ICON = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="m17 1 4 4-4 4M3 11V9a4 4 0 0 1 4-4h14M7 23l-4-4 4-4M21 13v2a4 4 0 0 1-4 4H3" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"/><text x="12" y="15" text-anchor="middle" font-size="8.5" font-weight="800" fill="currentColor" stroke="none" font-family="Arial">1</text></svg>';

    window.lampac_music = MUSIC;

    var musicUid = Lampa.Storage.get('lampac_unic_id', '');
    if (!musicUid) {
        musicUid = Lampa.Utils.uid(8).toLowerCase();
        Lampa.Storage.set('lampac_unic_id', musicUid);
    }

    // ===== TRANSPORT / COMMON HELPERS =====

    function withIdentity(url) {
        url = String(url || '');

        if (url.indexOf('account_email=') === -1) {
            var email = Lampa.Storage.get('account_email', '');
            if (email) url = Lampa.Utils.addUrlComponent(url, 'account_email=' + encodeURIComponent(email));
        }

        if (url.indexOf('uid=') === -1) {
            var uid = Lampa.Storage.get('lampac_unic_id', '');
            if (uid) url = Lampa.Utils.addUrlComponent(url, 'uid=' + encodeURIComponent(uid));
        }

        return url;
    }

    function withIdentityPayload(payload) {
        payload = String(payload || '');

        if (payload.indexOf('account_email=') === -1) {
            var email = Lampa.Storage.get('account_email', '');
            if (email) payload += (payload ? '&' : '') + 'account_email=' + encodeURIComponent(email);
        }

        if (payload.indexOf('uid=') === -1) {
            var uid = Lampa.Storage.get('lampac_unic_id', '');
            if (uid) payload += (payload ? '&' : '') + 'uid=' + encodeURIComponent(uid);
        }

        return payload;
    }

    function setTextIfChanged(element, value) {
        if (!element || !element.length) return;

        var text = value == null ? '' : String(value);
        if (element.text() !== text) element.text(text);
    }

    function setCappedCacheEntry(cache, key, value, limit) {
        if (!cache || !key) return;

        if (Object.prototype.hasOwnProperty.call(cache, key))
            delete cache[key];

        cache[key] = value;

        var keys = Object.keys(cache);
        while (keys.length > limit) {
            delete cache[keys.shift()];
        }
    }

    function request(url, success, error) {
        var network = new Lampa.Reguest();
        network.timeout(20000);
        network.silent(withIdentity(url), success, error || function () {});
    }

    function requestPost(url, data, success, error) {
        var network = new Lampa.Reguest();
        network.timeout(20000);
        network.native(withIdentity(url), function (response) {
            success(parseJson(response));
        }, error || function () {}, withIdentityPayload(data), {
            dataType: 'text',
            type: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8'
            }
        });
    }

    function startMusicPlaybackLoading(message) {
        MUSIC_PLAYBACK_LOADING_ACTIVE = true;
        if (MUSIC_PLAYBACK_LOADING_TIMER) clearTimeout(MUSIC_PLAYBACK_LOADING_TIMER);
        MUSIC_PLAYBACK_LOADING_TIMER = setTimeout(function () {
            stopMusicPlaybackLoading();
        }, 30000);

        try {
            Lampa.Loading.start(function () {}, message || 'Подготавливаю плейлист');
        } catch (e) {}
    }

    function stopMusicPlaybackLoading() {
        if (!MUSIC_PLAYBACK_LOADING_ACTIVE) return;

        MUSIC_PLAYBACK_LOADING_ACTIVE = false;
        if (MUSIC_PLAYBACK_LOADING_TIMER) {
            clearTimeout(MUSIC_PLAYBACK_LOADING_TIMER);
            MUSIC_PLAYBACK_LOADING_TIMER = 0;
        }
        try {
            Lampa.Loading.stop();
        } catch (e) {}
    }

    function traceStandaloneIosAudio(eventName, message, force) {
        if (!MUSIC_CLIENT_TRACE_ENABLED) return;
        if (!isStandaloneIosAudioActive() && eventName !== 'audio-create') return;

        var now = Date.now();
        if (!force && eventName === 'timeupdate') {
            if (now - (MUSIC_IOS_AUDIO.lastTraceTimeupdate || 0) < 10000) return;
            MUSIC_IOS_AUDIO.lastTraceTimeupdate = now;
        }

        var audio = MUSIC_IOS_AUDIO.audio;
        var track = MUSIC_IOS_AUDIO.tracks[MUSIC_IOS_AUDIO.currentIndex];
        var session = mediaSessionObject();
        var state = {
            seq: ++MUSIC_IOS_AUDIO.traceSeq,
            at: now,
            hidden: !!document.hidden,
            visibility: document.visibilityState || '',
            focus: typeof document.hasFocus === 'function' ? document.hasFocus() : null,
            active: !!MUSIC_IOS_AUDIO.active,
            switching: !!MUSIC_IOS_AUDIO.switching,
            realPlaying: !!MUSIC_IOS_AUDIO.playing,
            paused: audio ? !!audio.paused : null,
            ended: audio ? !!audio.ended : null,
            currentTime: audio && isFinite(audio.currentTime) ? Math.round(audio.currentTime * 100) / 100 : null,
            duration: audio && isFinite(audio.duration) ? Math.round(audio.duration * 100) / 100 : null,
            playbackRate: audio && isFinite(audio.playbackRate) ? audio.playbackRate : null,
            muted: audio ? !!audio.muted : null,
            readyState: audio ? audio.readyState : null,
            networkState: audio ? audio.networkState : null,
            src: audio ? (audio.currentSrc || audio.src || '').slice(0, 140) : '',
            mediaSessionState: session && 'playbackState' in session ? session.playbackState : '',
            queueLength: queueTracks().length,
            queueIndex: queueCurrentIndex(),
            playlistLength: Array.isArray(MUSIC_IOS_AUDIO.playlist) ? MUSIC_IOS_AUDIO.playlist.length : 0,
            trackIndex: MUSIC_IOS_AUDIO.currentIndex,
            track: track ? {
                id: track.id || '',
                title: track.title || '',
                artist: track.artist_name || ''
            } : null
        };
        var payload = 'event_name=' + encodeURIComponent(eventName || '')
            + '&track_id=' + encodeURIComponent(track && track.id ? track.id : '')
            + '&state=' + encodeURIComponent(JSON.stringify(state))
            + '&message=' + encodeURIComponent(message || '');

        try {
            var url = withIdentity(MUSIC.endpoints.clientLog);
            if (navigator.sendBeacon) {
                navigator.sendBeacon(url, new Blob([withIdentityPayload(payload)], {
                    type: 'application/x-www-form-urlencoded; charset=UTF-8'
                }));
                return;
            }
        } catch (e) {}

        try {
            requestPost(MUSIC.endpoints.clientLog, payload, function () {}, function () {});
        } catch (e) {}
    }

    function describeMusicValue(value) {
        if (typeof value === 'function') return '[function]';
        if (value == null) return '';

        value = String(value || '');
        if (!value) return '';

        var output = value;

        try {
            var parsed = new URL(value, window.location.href);
            output = parsed.protocol + '//' + parsed.host + parsed.pathname;
        } catch (e) {}

        return output.slice(0, 160);
    }

    function describeMediaRanges(ranges) {
        if (!ranges || typeof ranges.length !== 'number' || !ranges.length) return '';

        try {
            var last = ranges.length - 1;
            return ranges.length + ':' + Math.round(ranges.start(0) * 100) / 100 + '-' + Math.round(ranges.end(last) * 100) / 100;
        } catch (e) {
            return String(ranges.length || '');
        }
    }

    function describeError(error) {
        if (!error) return '';

        try {
            if (error.stack) return String(error.stack).slice(0, 800);
            if (error.message) return ((error.name || 'Error') + ': ' + error.message).slice(0, 800);
        } catch (e) {}

        return String(error || '').slice(0, 800);
    }

    function describeMediaError(media) {
        if (!media || !media.error) return '';

        try {
            return [
                'code=' + (media.error.code || ''),
                media.error.message || ''
            ].join(' ').trim().slice(0, 300);
        } catch (e) {
            return 'media-error';
        }
    }

    function sendMusicClientDebug(eventName, state, message, trackId) {
        if (!MUSIC_CLIENT_TRACE_ENABLED) return;

        var payload = 'event_name=' + encodeURIComponent(eventName || '')
            + '&track_id=' + encodeURIComponent(trackId || '')
            + '&state=' + encodeURIComponent(JSON.stringify(state || {}))
            + '&message=' + encodeURIComponent(message || '');

        try {
            var url = withIdentity(MUSIC.endpoints.clientLog);
            if (navigator.sendBeacon) {
                navigator.sendBeacon(url, new Blob([withIdentityPayload(payload)], {
                    type: 'application/x-www-form-urlencoded; charset=UTF-8'
                }));
                return;
            }
        } catch (e) {}

        try {
            requestPost(MUSIC.endpoints.clientLog, payload, function () {}, function () {});
        } catch (e) {}
    }

    function bindMusicClientDebugErrors() {
        if (!MUSIC_CLIENT_TRACE_ENABLED || MUSIC_CLIENT_DEBUG_ERRORS_BOUND) return;
        MUSIC_CLIENT_DEBUG_ERRORS_BOUND = true;

        window.addEventListener('error', function (event) {
            var data = activePlayerData() || MUSIC_EMBEDDED_IOS.lastData || {};

            sendMusicClientDebug('music-js-error', {
                at: Date.now(),
                hidden: !!document.hidden,
                message: event && event.message ? event.message : '',
                source: event && event.filename ? event.filename : '',
                line: event && event.lineno ? event.lineno : 0,
                column: event && event.colno ? event.colno : 0,
                error: describeError(event && event.error),
                data: {
                    id: data.music_track_id || '',
                    title: data.title || '',
                    artist: data.artist || '',
                    url: describeMusicValue(data.url)
                }
            }, event && event.message ? event.message : '', data.music_track_id || '');
        });

        window.addEventListener('unhandledrejection', function (event) {
            var data = activePlayerData() || MUSIC_EMBEDDED_IOS.lastData || {};

            sendMusicClientDebug('music-js-unhandledrejection', {
                at: Date.now(),
                hidden: !!document.hidden,
                reason: describeError(event && event.reason),
                data: {
                    id: data.music_track_id || '',
                    title: data.title || '',
                    artist: data.artist || '',
                    url: describeMusicValue(data.url)
                }
            }, describeError(event && event.reason), data.music_track_id || '');
        });
    }

    function describeLampaPlayerDebugPayload(event) {
        var parts = [];
        event = event || {};

        if (typeof event.position === 'number') parts.push('position=' + event.position);
        if (event.percent != null) parts.push('percent=' + Math.round(Number(event.percent || 0) * 10000) / 100 + '%');
        if (event.method) parts.push('method=' + event.method);
        if (event.size) parts.push('size=' + event.size);
        if (event.url) parts.push('url=' + describeMusicValue(event.url));
        if (event.error) parts.push('error=' + describeMusicValue(event.error));
        if (event.current != null || event.duration != null)
            parts.push('time=' + Math.round(Number(event.current || 0) * 100) / 100 + '/' + Math.round(Number(event.duration || 0) * 100) / 100);

        if (event.item) {
            parts.push('item=' + [
                event.item.music_track_id || '',
                event.item.title || '',
                describeMusicValue(event.item.url || '')
            ].join('|'));
        }

        if (Array.isArray(event.playlist)) {
            var sample = event.playlist.slice(0, 5).map(function (item, index) {
                return index + ':' + (item && (item.music_track_id || item.title || describeMusicValue(item.url || '')) || '');
            }).join(',');
            parts.push('playlist=' + event.playlist.length + '[' + sample + ']');
        }

        return parts.join(' ');
    }

    function traceLampaPlayerDebug(source, name, event) {
        if (!MUSIC_CLIENT_TRACE_ENABLED) return;
        traceEmbeddedIos('lampa-' + source + '-' + name, describeLampaPlayerDebugPayload(event), true);
    }

    function bindLampaPlayerDebug() {
        if (!MUSIC_CLIENT_TRACE_ENABLED || MUSIC_LAMPA_PLAYER_DEBUG_BOUND) return;
        MUSIC_LAMPA_PLAYER_DEBUG_BOUND = true;

        function follow(listener, source, events) {
            if (!listener || typeof listener.follow !== 'function') return;

            events.forEach(function (name) {
                try {
                    listener.follow(name, function (event) {
                        traceLampaPlayerDebug(source, name, event || {});
                    });
                } catch (e) {}
            });
        }

        function wrap(owner, method, source) {
            if (!owner || typeof owner[method] !== 'function') return;

            var flag = '__musicDebugWrapped_' + method;
            if (owner[flag]) return;
            owner[flag] = true;

            var original = owner[method];
            owner[method] = function () {
                var event = {};

                if (arguments.length === 1) {
                    if (typeof arguments[0] === 'object') event = arguments[0] || {};
                    else event = { value: arguments[0], url: method === 'url' ? arguments[0] : '' };
                } else if (arguments.length) {
                    event = {
                        args: Array.prototype.slice.call(arguments).map(function (arg) {
                            return describeMusicValue(arg);
                        }).join('|')
                    };
                }

                if (event.value != null && !event.url) event.url = method === 'url' ? event.value : '';
                traceLampaPlayerDebug(source, method + '-call', event);
                return original.apply(this, arguments);
            };
        }

        follow(Lampa.PlayerVideo && Lampa.PlayerVideo.listener, 'video', [
            'play', 'pause', 'rewind', 'ended', 'error', 'reset_continue', 'canplay'
        ]);

        follow(Lampa.PlayerPanel && Lampa.PlayerPanel.listener, 'panel', [
            'playpause', 'playlist', 'prev', 'next', 'rprev', 'rnext', 'to_start', 'to_end', 'quality', 'mouse_rewind'
        ]);

        follow(Lampa.PlayerPlaylist && Lampa.PlayerPlaylist.listener, 'playlist', [
            'select', 'set'
        ]);

        wrap(Lampa.Player, 'play', 'player');
        wrap(Lampa.Player, 'playlist', 'player');
        wrap(Lampa.Player, 'close', 'player');
        wrap(Lampa.PlayerVideo, 'url', 'video');
        wrap(Lampa.PlayerVideo, 'destroy', 'video');
        wrap(Lampa.PlayerVideo, 'to', 'video');
        wrap(Lampa.PlayerVideo, 'play', 'video');
        wrap(Lampa.PlayerVideo, 'pause', 'video');
        wrap(Lampa.PlayerPlaylist, 'url', 'playlist');
        wrap(Lampa.PlayerPlaylist, 'set', 'playlist');
        wrap(Lampa.PlayerPlaylist, 'next', 'playlist');
        wrap(Lampa.PlayerPlaylist, 'prev', 'playlist');
    }

    function traceEmbeddedIos(eventName, message, force) {
        if (!MUSIC_CLIENT_TRACE_ENABLED) return;
        if (!force && !MUSIC_EMBEDDED_IOS.active) return;

        var now = Date.now();
        var media = activeMusicMediaElement();
        var data = activePlayerData() || MUSIC_EMBEDDED_IOS.lastData || {};
        var track = queueCurrentTrack();
        var session = mediaSessionObject();
        var playlist = activePlaylist();
        var queueIndex = queueCurrentIndex();
        var playlistItem = playlist && queueIndex >= 0 ? playlist[queueIndex] : null;
        var mediaCount = 0;

        try {
            mediaCount = document.querySelectorAll('.player-video video, .player-video audio').length;
        } catch (e) {}

        var state = {
            seq: ++MUSIC_EMBEDDED_IOS.traceSeq,
            at: now,
            hidden: !!document.hidden,
            visibility: document.visibilityState || '',
            focus: typeof document.hasFocus === 'function' ? document.hasFocus() : null,
            controller: Lampa.Controller && Lampa.Controller.enabled ? ((Lampa.Controller.enabled() || {}).name || '') : '',
            player: currentExternalPlayer(),
            playbackMode: getPlaybackMode(),
            provider: getAudioProviderId(),
            active: !!MUSIC_EMBEDDED_IOS.active,
            keepAlive: !!MUSIC_IOS_AUDIO.keepAliveActive,
            paused: media ? !!media.paused : null,
            ended: media ? !!media.ended : null,
            currentTime: media && isFinite(media.currentTime) ? Math.round(media.currentTime * 100) / 100 : null,
            duration: embeddedIosEffectiveDuration(media, data) || null,
            readyState: media ? media.readyState : null,
            networkState: media ? media.networkState : null,
            mediaError: describeMediaError(media),
            mediaTag: media && media.tagName ? media.tagName.toLowerCase() : '',
            seekable: media ? describeMediaRanges(media.seekable) : '',
            buffered: media ? describeMediaRanges(media.buffered) : '',
            playsInline: media ? !!(media.playsInline || media.getAttribute('playsinline') != null || media.getAttribute('webkit-playsinline') != null) : null,
            mediaSrc: media ? describeMusicValue(media.currentSrc || media.src || '') : '',
            mediaCount: mediaCount,
            mediaSessionState: session && 'playbackState' in session ? session.playbackState : '',
            visual: {
                exists: !!$('.lm-player-visual').length,
                mode: musicPlayerVisualMode || '',
                key: musicPlayerVisualKey || '',
                lyricsRequest: musicPlayerVisualLyricsRequest || ''
            },
            queueLength: queueTracks().length,
            queueIndex: queueIndex,
            playlistLength: Array.isArray(playlist) ? playlist.length : 0,
            data: {
                id: data.music_track_id || '',
                title: data.title || '',
                artist: data.artist || '',
                url: describeMusicValue(data.url)
            },
            track: track ? {
                id: track.id || '',
                title: track.title || '',
                artist: track.artist_name || ''
            } : null,
            playlistItem: playlistItem ? {
                id: playlistItem.music_track_id || '',
                title: playlistItem.title || '',
                artist: playlistItem.artist || '',
                url: describeMusicValue(playlistItem.url)
            } : null
        };
        sendMusicClientDebug('embedded-' + (eventName || ''), state, message || '', (data && data.music_track_id) || (track && track.id) || '');
    }

    function musicHeatHasCounters(counters) {
        counters = counters || {};

        for (var key in counters) {
            if (Object.prototype.hasOwnProperty.call(counters, key)) return true;
        }

        return false;
    }

    function isMusicHeatProbeActive() {
        return isStandaloneIosAudioActive()
            || !!MUSIC_EMBEDDED_IOS.active
            || !!MUSIC_IOS_AUDIO.keepAliveActive
            || !!MUSIC_IOS_FULL_PLAYER_OPEN;
    }

    function scheduleMusicHeatProbe() {
        if (!MUSIC_CLIENT_TRACE_ENABLED || MUSIC_HEAT_PROBE.timer) return;
        if (!isMusicHeatProbeActive()) return;

        if (!MUSIC_HEAT_PROBE.startedAt) MUSIC_HEAT_PROBE.startedAt = Date.now();
        MUSIC_HEAT_PROBE.timer = setTimeout(flushMusicHeatProbe, MUSIC_HEAT_PROBE.interval);
    }

    function bumpMusicHeatMetric(name, amount) {
        if (!MUSIC_CLIENT_TRACE_ENABLED || !name) return;

        MUSIC_HEAT_PROBE.counters[name] = (MUSIC_HEAT_PROBE.counters[name] || 0) + (amount || 1);
        scheduleMusicHeatProbe();
    }

    function musicHeatNow() {
        try {
            if (window.performance && typeof window.performance.now === 'function')
                return window.performance.now();
        } catch (e) {}

        return Date.now();
    }

    function bumpMusicHeatDuration(name, startedAt) {
        if (!MUSIC_CLIENT_TRACE_ENABLED || !name || !startedAt) return;

        var elapsed = Math.max(0, musicHeatNow() - startedAt);
        bumpMusicHeatMetric(name + 'Count');
        bumpMusicHeatMetric(name + 'Ms', Math.round(elapsed * 100) / 100);
    }

    function numberForHeat(value) {
        return isFinite(value) ? Math.round(value * 100) / 100 : null;
    }

    function standaloneIosFullHeatState(now) {
        if (!MUSIC_CLIENT_TRACE_ENABLED) return null;

        var player = MUSIC_IOS_FULL_PLAYER;
        if (!player || !player.length) {
            return {
                open: !!MUSIC_IOS_FULL_PLAYER_OPEN,
                dom: false
            };
        }

        var shell = player.find('.lm-ios-full-player__shell').get(0);
        var queueList = player.find('.lm-ios-full-player__queue-list').get(0);
        var sheetBody = player.find('.lm-ios-full-player__sheet-body').get(0);
        var lyricsMeta = player.data('lyricsLineMeta');
        var manualAt = Number(player.attr('data-lyrics-manual') || 0);

        return {
            open: !!MUSIC_IOS_FULL_PLAYER_OPEN,
            visible: player.hasClass('lm-ios-full-player--visible'),
            sheet: player.hasClass('lm-ios-full-player--sheet-open'),
            sheetKind: player.attr('data-sheet-kind') || '',
            seeking: player.attr('data-seeking') === 'true',
            pausedClass: player.hasClass('lm-ios-full-player--paused'),
            queueKeyLen: String(player.attr('data-queue-key') || '').length,
            progressKey: MUSIC_IOS_AUDIO.fullProgressKey || '',
            playbackKeyLen: String(MUSIC_IOS_AUDIO.fullPlaybackKey || '').length,
            uiKeyLen: String(MUSIC_IOS_AUDIO.fullUiKey || '').length,
            dom: {
                quicks: player.find('.lm-ios-full-player__quick').length,
                queueItems: player.find('.lm-ios-full-player__queue-item').length,
                queueDividers: player.find('.lm-ios-full-player__queue-divider').length,
                sheetRows: player.find('.lm-ios-full-player__sheet-row').length,
                lyricLines: player.find('.lm-ios-full-player__lyrics-line').length
            },
            shell: shell ? {
                h: shell.clientHeight || 0,
                scrollH: shell.scrollHeight || 0
            } : null,
            sheetBody: sheetBody ? {
                h: sheetBody.clientHeight || 0,
                scrollH: sheetBody.scrollHeight || 0,
                top: sheetBody.scrollTop || 0
            } : null,
            queue: queueList ? {
                h: queueList.clientHeight || 0,
                scrollH: queueList.scrollHeight || 0,
                top: queueList.scrollTop || 0
            } : null,
            lyrics: lyricsMeta ? {
                lines: lyricsMeta.elements ? lyricsMeta.elements.length : 0,
                active: lyricsMeta.activeIndex,
                manualAge: manualAt ? now - manualAt : 0
            } : null
        };
    }

    function logStandaloneIosFullEvent(eventName, message) {
        if (!MUSIC_CLIENT_TRACE_ENABLED || !eventName) return;

        var track = MUSIC_IOS_AUDIO.tracks[MUSIC_IOS_AUDIO.currentIndex] || queueCurrentTrack();
        sendMusicClientDebug(
            'full-player-' + eventName,
            standaloneIosFullHeatState(Date.now()),
            message || '',
            track && track.id ? track.id : ''
        );
    }

    function postMusicHeatProbe(counters, now) {
        var audio = MUSIC_IOS_AUDIO.audio;
        var media = activeMusicMediaElement();
        var data = activePlayerData() || MUSIC_EMBEDDED_IOS.lastData || {};
        var track = MUSIC_IOS_AUDIO.tracks[MUSIC_IOS_AUDIO.currentIndex] || queueCurrentTrack();
        var session = mediaSessionObject();
        var ctx = MUSIC_IOS_AUDIO.keepAliveCtx;
        var state = {
            seq: ++MUSIC_HEAT_PROBE.seq,
            at: now,
            span: MUSIC_HEAT_PROBE.lastFlushAt ? now - MUSIC_HEAT_PROBE.lastFlushAt : 0,
            uptime: MUSIC_HEAT_PROBE.startedAt ? now - MUSIC_HEAT_PROBE.startedAt : 0,
            hidden: !!document.hidden,
            vis: document.visibilityState || '',
            focus: typeof document.hasFocus === 'function' ? document.hasFocus() : null,
            ctrl: Lampa.Controller && Lampa.Controller.enabled ? ((Lampa.Controller.enabled() || {}).name || '') : '',
            player: currentExternalPlayer(),
            mode: getPlaybackMode(),
            provider: getAudioProviderId(),
            ms: session && 'playbackState' in session ? session.playbackState : '',
            counters: counters || {},
            sa: {
                active: !!MUSIC_IOS_AUDIO.active,
                playing: !!MUSIC_IOS_AUDIO.playing,
                switching: !!MUSIC_IOS_AUDIO.switching,
                paused: audio ? !!audio.paused : null,
                ct: audio ? numberForHeat(audio.currentTime) : null,
                dur: audio ? numberForHeat(audio.duration) : null,
                rs: audio ? audio.readyState : null,
                ns: audio ? audio.networkState : null,
                posAge: MUSIC_IOS_AUDIO.lastPositionSync ? now - MUSIC_IOS_AUDIO.lastPositionSync : null,
                uiAge: MUSIC_IOS_AUDIO.lastUiUpdate ? now - MUSIC_IOS_AUDIO.lastUiUpdate : null
            },
            em: {
                active: !!MUSIC_EMBEDDED_IOS.active,
                paused: media ? !!media.paused : null,
                ct: media ? numberForHeat(media.currentTime) : null,
                dur: media ? numberForHeat(media.duration) : null,
                rs: media ? media.readyState : null,
                ns: media ? media.networkState : null,
                tag: media && media.tagName ? media.tagName.toLowerCase() : ''
            },
            ka: {
                active: !!MUSIC_IOS_AUDIO.keepAliveActive,
                webaudio: !!MUSIC_IOS_KEEPALIVE_WEBAUDIO,
                ctx: ctx ? (ctx.state || '') : '',
                loop: !!MUSIC_IOS_AUDIO.keepAlive
            },
            full: standaloneIosFullHeatState(now),
            track: track ? {
                id: track.id || data.music_track_id || '',
                title: track.title || data.title || '',
                artist: track.artist_name || data.artist || ''
            } : null
        };
        var payload = 'event_name=' + encodeURIComponent('heat-probe')
            + '&track_id=' + encodeURIComponent((track && track.id) || data.music_track_id || '')
            + '&state=' + encodeURIComponent(JSON.stringify(state))
            + '&message=';

        MUSIC_HEAT_PROBE.lastFlushAt = now;

        try {
            var url = withIdentity(MUSIC.endpoints.clientLog);
            if (navigator.sendBeacon) {
                navigator.sendBeacon(url, new Blob([withIdentityPayload(payload)], {
                    type: 'application/x-www-form-urlencoded; charset=UTF-8'
                }));
                return;
            }
        } catch (e) {}

        try {
            requestPost(MUSIC.endpoints.clientLog, payload, function () {}, function () {});
        } catch (e) {}
    }

    function flushMusicHeatProbe() {
        var now = Date.now();
        var counters = MUSIC_HEAT_PROBE.counters || {};
        var active = isMusicHeatProbeActive();

        MUSIC_HEAT_PROBE.timer = 0;
        MUSIC_HEAT_PROBE.counters = {};

        if (!MUSIC_CLIENT_TRACE_ENABLED) return;
        if (!active && !musicHeatHasCounters(counters)) {
            MUSIC_HEAT_PROBE.startedAt = 0;
            return;
        }

        postMusicHeatProbe(counters, now);

        if (active) scheduleMusicHeatProbe();
        else MUSIC_HEAT_PROBE.startedAt = 0;
    }

    function parseJson(value) {
        if (!value) return null;
        if (typeof value === 'object') return value;

        try {
            return JSON.parse(value);
        } catch (e) {
            return null;
        }
    }

    function encodeSvg(svg) {
        return 'data:image/svg+xml;charset=UTF-8,' + encodeURIComponent(svg);
    }

    function escapeXml(value) {
        return String(value || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&apos;');
    }

    function initials(value) {
        var words = String(value || '')
            .replace(/[^A-Za-z0-9А-Яа-яЁёІіЇїЄєҐґ\s]/g, ' ')
            .trim()
            .split(/\s+/)
            .filter(Boolean);

        if (!words.length) return 'MU';
        if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
        return (words[0][0] + words[1][0]).toUpperCase();
    }

    function formatYear(value) {
        var match = String(value || '').match(/\d{4}/);
        return match ? match[0] : '';
    }

    function formatDuration(ms) {
        if (!ms) return '-:--';

        var total = Math.floor(ms / 1000);
        var minutes = Math.floor(total / 60);
        var seconds = total % 60;

        return minutes + ':' + (seconds < 10 ? '0' : '') + seconds;
    }

    function translate(key, fallback) {
        if (window.Lampa && Lampa.Lang && typeof Lampa.Lang.translate === 'function')
            return Lampa.Lang.translate(key);

        return fallback || key;
    }

    function showContextMenu(title, items, onSelect, focusContext) {
        focusContext = focusContext || captureMusicFocusContext('content');
        var list = Array.isArray(items) ? items.filter(Boolean) : [];

        if (!list.length) return;

        Lampa.Select.show({
            title: title || translate('title_action', 'Действия'),
            items: list,
            onBack: function () {
                restoreMusicFocusContext(focusContext, 0);
            },
            onBeforeClose: function () {
                return true;
            },
            onSelect: function (selected) {
                if (selected && (selected.separator || selected.disabled || selected.noop)) {
                    restoreMusicFocusContext(focusContext);
                    return;
                }

                if (!selected || !onSelect) {
                    restoreMusicFocusContext(focusContext);
                    return;
                }
                onSelect(selected, focusContext);
            }
        });
    }

    function safeControllerName(fallback) {
        try {
            var enabled = Lampa.Controller && Lampa.Controller.enabled ? Lampa.Controller.enabled() : null;
            return enabled && enabled.name ? enabled.name : (fallback || 'content');
        } catch (e) {
            return fallback || 'content';
        }
    }

    function restoreController(name) {
        name = name || 'content';

        setTimeout(function () {
            try {
                if (Lampa.Controller && Lampa.Controller.toggle)
                    Lampa.Controller.toggle(name);
            } catch (e) {
                try {
                    if (name !== 'content' && Lampa.Controller && Lampa.Controller.toggle)
                        Lampa.Controller.toggle('content');
                } catch (_) {}
            }
        }, 0);
    }

    function buildMusicFocusKey(item) {
        if (!item) return '';

        return [
            item.section_key || '',
            item.type || '',
            item.id || item.title || ''
        ].join('|');
    }

    function captureMusicFocusContext(fallbackController) {
        var focused = $('.lm-card.selector.focus,.lm-track.selector.focus,.selector.focus').get(0);
        var container = focused
            ? ($(focused).closest('.lm-home-line,.lm-search-line,.lm-search-actions,.lm-search-sources,.lm-search-history,.lm-search-bar,.lm-search-screen,.lm-full,.items-line')[0] || null)
            : null;
        var selectorIndex = -1;

        if (focused && container)
            selectorIndex = $(container).find('.selector').index(focused);

        var context = {
            controller: safeControllerName(fallbackController || 'content'),
            element: focused || null,
            focusKey: focused ? (focused.getAttribute('data-music-focus-key') || '') : '',
            container: container,
            selectorIndex: selectorIndex
        };

        if (focused && container)
            MUSIC_LAST_FOCUS_CONTEXT = context;
        else if (MUSIC_LAST_FOCUS_CONTEXT)
            return MUSIC_LAST_FOCUS_CONTEXT;

        return context;
    }

    function findMusicFocusElement(context) {
        if (!context) return null;

        if (context.element && document.documentElement.contains(context.element))
            return context.element;

        if (context.focusKey) {
            var matched = $('.selector[data-music-focus-key]').filter(function () {
                return this.getAttribute('data-music-focus-key') === context.focusKey;
            }).get(0);

            if (matched) return matched;
        }

        if (context.container && document.documentElement.contains(context.container)) {
            var selectors = $(context.container).find('.selector');
            if (selectors.length) {
                var index = Math.max(0, Math.min(context.selectorIndex || 0, selectors.length - 1));
                return selectors.eq(index).get(0);
            }
        }

        return null;
    }

    function restoreMusicFocusContext(context, delay) {
        if (!context) return;

        setTimeout(function () {
            if (Date.now() < MUSIC_HOME_REFRESH_RESTORE_BLOCK_UNTIL && context.container && $(context.container).hasClass('lm-home-line'))
                return;

            restoreController(context.controller || 'content');

            setTimeout(function () {
                var target = findMusicFocusElement(context);
                if (!target) {
                    var fallbackContainer = context.container && document.documentElement.contains(context.container)
                        ? context.container
                        : null;

                    if (fallbackContainer && Lampa.Controller && Lampa.Controller.collectionSet)
                        Lampa.Controller.collectionSet(fallbackContainer);

                    return;
                }

                var container = $(target).closest('.lm-home-line,.lm-search-line,.lm-full,.items-line')[0]
                    || context.container
                    || target.parentNode
                    || document.body;

                try {
                    if (Lampa.Controller && Lampa.Controller.collectionSet)
                        Lampa.Controller.collectionSet(container);
                    if (Lampa.Controller && Lampa.Controller.collectionFocus)
                        Lampa.Controller.collectionFocus(target, container);
                } catch (e) {}

                try {
                    target.scrollIntoView({ block: 'nearest', inline: 'nearest' });
                } catch (_) {}
            }, 60);
        }, typeof delay === 'number' ? delay : 60);
    }

    function restoreMusicFocusContextAfterRefresh(context) {
        restoreMusicFocusContext(context, 120);
        restoreMusicFocusContext(context, 500);
    }

    function captureMusicNeighborFocusContext(context, fallbackController) {
        context = context || captureMusicFocusContext(fallbackController);

        var target = context && context.element;
        if (!target) return context;

        var container = context.container && document.documentElement.contains(context.container)
            ? context.container
            : ($(target).closest('.lm-home-line,.lm-search-line,.lm-search-actions,.lm-search-sources,.lm-search-history,.lm-search-bar,.lm-search-screen,.lm-full,.items-line')[0] || null);

        if (!container) return context;

        var selectors = $(container).find('.selector').filter(function () {
            return this !== target && !$(this).closest(target).length;
        });

        if (!selectors.length) return context;

        var allSelectors = $(container).find('.selector');
        var targetIndex = allSelectors.index(target);
        var neighbor = null;

        if (targetIndex >= 0) {
            for (var i = targetIndex + 1; i < allSelectors.length; i++) {
                if (allSelectors[i] !== target) {
                    neighbor = allSelectors[i];
                    break;
                }
            }

            if (!neighbor) {
                for (var j = targetIndex - 1; j >= 0; j--) {
                    if (allSelectors[j] !== target) {
                        neighbor = allSelectors[j];
                        break;
                    }
                }
            }
        }

        neighbor = neighbor || selectors.get(0);

        return {
            controller: safeControllerName((context && context.controller) || fallbackController || 'content'),
            element: neighbor,
            focusKey: neighbor.getAttribute('data-music-focus-key') || '',
            container: container,
            selectorIndex: allSelectors.index(neighbor)
        };
    }

    function buildEntryRestoreState(entry, index) {
        var sectionKey = entry && entry.section_key ? String(entry.section_key) : '';
        if (!sectionKey) return null;

        return {
            sectionKey: sectionKey,
            entryIndex: Math.max(0, Number(index) || 0)
        };
    }

    function refreshListWithFocus(refresh, restoreState, fallbackContext) {
        if (!refresh) {
            restoreMusicFocusContext(fallbackContext, 120);
            return;
        }

        if (refresh.length > 0) {
            refresh(restoreState || null);
            return;
        }

        refresh();
        restoreMusicFocusContextAfterRefresh(fallbackContext);
    }

    function formatSeconds(seconds) {
        seconds = Math.max(0, Math.floor(Number(seconds) || 0));

        var hours = Math.floor(seconds / 3600);
        var minutes = Math.floor((seconds % 3600) / 60);
        var secs = seconds % 60;

        if (hours > 0)
            return hours + ':' + (minutes < 10 ? '0' : '') + minutes + ':' + (secs < 10 ? '0' : '') + secs;

        return minutes + ':' + (secs < 10 ? '0' : '') + secs;
    }

    function formatDate(value) {
        if (!value) return 'Неизвестно';

        var string = String(value);
        if (/^\d{4}$/.test(string)) return string;
        if (/^\d{4}-\d{2}-\d{2}$/.test(string)) {
            var parts = string.split('-');
            return parts[2] + '.' + parts[1] + '.' + parts[0];
        }

        return string;
    }

    var STREAM_MODE_ITEMS = [
        { mode: 'auto', title: 'Авто' },
        { mode: 'best', title: 'Лучшее качество' },
        { mode: 'low', title: 'Экономия' },
        { mode: 'm4a', title: 'Предпочитать m4a' },
        { mode: 'webm', title: 'Предпочитать webm' }
    ];

    var PLAYBACK_MODE_ITEMS = [
        { mode: 'audio', title: 'Без видео' },
        { mode: 'video', title: 'С видео' }
    ];
    var AUDIO_PROVIDER_FALLBACKS = [
        { id: 'youtubeaudio', name: 'YouTube Audio', capabilities: ['match', 'streams', 'playback', 'audio', 'video'] },
        { id: 'sefonaudio', name: 'Sefon', capabilities: ['match', 'streams', 'playback', 'audio'] },
        { id: 'soundcloudaudio', name: 'SoundCloud', capabilities: ['match', 'streams', 'playback', 'audio'] }
    ];

    // ===== PLAYBACK SETTINGS / PROVIDERS =====

    function getStreamMode() {
        var mode = Lampa.Storage.get(MUSIC.storage.quality_mode, 'auto');
        mode = String(mode || 'auto').toLowerCase();

        return STREAM_MODE_ITEMS.some(function (item) {
            return item.mode === mode;
        }) ? mode : 'auto';
    }

    function setStreamMode(mode) {
        Lampa.Storage.set(MUSIC.storage.quality_mode, mode || 'auto');
    }

    function getPlaybackMode() {
        var mode = Lampa.Storage.get(MUSIC.storage.playback_mode, 'audio');
        mode = String(mode || 'audio').toLowerCase();

        return PLAYBACK_MODE_ITEMS.some(function (item) {
            return item.mode === mode;
        }) ? mode : 'audio';
    }

    function setPlaybackMode(mode) {
        Lampa.Storage.set(MUSIC.storage.playback_mode, mode || 'audio');
    }

    function buildMusicPlayerValues() {
        var values = {
            inner: 'Встроенный'
        };

        if (Lampa.Platform.is('apple') && !Lampa.Platform.macOS()) {
            values.ios = 'iOS';
            values.vlc = 'VLC';
            values.outplayer = 'Outplayer';
            values.nplayer = 'nPlayer';
            values.infuse = 'Infuse';
        }

        if (Lampa.Platform.macOS()) {
            values.mpv = 'MPV';
            values.iina = 'IINA';
            values.nplayer = 'nPlayer';
            values.infuse = 'Infuse';
        }

        if (Lampa.Platform.desktop())
            values.other = 'Другой внешний';

        return values;
    }

    // пока пользователь не выбирал плеер для музыки, наследуем глобальный
    // плеер Lampa (если он есть в списке платформы), иначе встроенный
    function defaultMusicPlayerId(values) {
        var globalPlayer = Lampa.Storage.field('player');
        return Object.prototype.hasOwnProperty.call(values, globalPlayer) ? globalPlayer : 'inner';
    }

    function getMusicPlayerId() {
        var values = buildMusicPlayerValues();
        var player = Lampa.Storage.get(MUSIC.storage.player, '') || '';

        return Object.prototype.hasOwnProperty.call(values, player) ? player : defaultMusicPlayerId(values);
    }

    function getLaunchMusicPlayerId() {
        var player = getMusicPlayerId();

        // Наш iOS-плеер — audio-only <audio>. Когда пользователь включает
        // YouTube «С видео», запуск должен идти через встроенный видеоплеер
        // Lampa, иначе ядро не создаёт нормальную video-очередь.
        if (player === 'ios' && getPlaybackMode() === 'video')
            return 'inner';

        return player;
    }

    // Lampa.Player.play выбирает ВНЕШНЕЕ приложение по глобальной настройке
    // player (externalPlayer в ядре читает Storage.field), data.launch_player
    // влияет только на развилку inner/внешний. Поэтому выбранный для музыки
    // внешний плеер запускаем сами — схемы те же, что в ядре Lampa
    function musicExternalSchemeUrl(player, url) {
        if (!url) return '';

        var encoded = encodeURIComponent(url);

        switch (player) {
            case 'vlc': return 'vlc://' + url;
            case 'nplayer': return 'nplayer-' + url;
            case 'infuse': return 'infuse://x-callback-url/play?url=' + encoded;
            case 'outplayer': return 'outplayer://x-callback-url/play?url=' + encoded;
            case 'mpv': return 'mpv://' + encodeURI(url);
            case 'iina': return 'iina://weblink?url=' + encoded;
            default: return '';
        }
    }

    // ядро Lampa решает playsinline по ГЛОБАЛЬНОЙ настройке player В МОМЕНТ
    // создания video-элемента (data.launch_player на это не влияет): при
    // глобальном ios видео уходит в НАТИВНЫЙ фулскрин, где панель Lampa с
    // плейлистом недоступна. Единственная ручка — временно подменить глобальное
    // поле на время запуска; восстанавливаем на start/destroy и по таймауту
    function playWithTemporaryGlobalPlayer(data, temporaryPlayer) {
        var original = Lampa.Storage.get('player', '');
        var restored = false;

        function restore() {
            if (restored) return;
            restored = true;

            try {
                Lampa.Storage.set('player', original);
            } catch (e) {}

            try {
                Lampa.Player.listener.remove('start', restore);
                Lampa.Player.listener.remove('destroy', restore);
            } catch (e) {}
        }

        try {
            Lampa.Storage.set('player', temporaryPlayer);
        } catch (e) {
            Lampa.Player.play(data);
            return;
        }

        try {
            Lampa.Player.listener.follow('start', restore);
            Lampa.Player.listener.follow('destroy', restore);
        } catch (e) {}

        setTimeout(restore, 3000);
        Lampa.Player.play(data);
    }

    function launchMusicPlayback(data) {
        var player = getLaunchMusicPlayerId();

        // музыкальный inner должен открываться встроенным даже при внешнем
        // глобальном плеере — это ядро умеет через data.launch_player
        if (player === 'inner') {
            data.launch_player = 'inner';

            // музыкальное видео во встроенном при ГЛОБАЛЬНОМ ios-плеере:
            // без подмены ядро создаст video-элемент без playsinline →
            // iOS попробует нативный фулскрин, а webkitEnterFullscreen без
            // живого жеста запрещён (подготовка плейлиста жест «съедает») →
            // «Видео не найдено или повреждено». Гейт НЕ зависит от
            // музыкальной настройки плеера — только от глобальной
            if (Lampa.Platform.is('apple')
                && getPlaybackMode() === 'video'
                && Lampa.Storage.field('player') === 'ios') {
                playWithTemporaryGlobalPlayer(data, 'inner');
                return;
            }

            Lampa.Player.play(data);
            return;
        }

        var scheme = musicExternalSchemeUrl(player, data && data.url);

        if (scheme) {
            try {
                window.location.assign(scheme);
                return;
            } catch (e) {}
        }

        Lampa.Player.play(data);
    }

    function moveMusicPlayerSettingInPlayer(body) {
        if (!body || !body.length) return;

        var musicPlayer = body.find('[data-name="' + MUSIC.storage.player + '"]');
        var mainPlayer = body.find('[data-name="player"]');

        if (musicPlayer.length && mainPlayer.length)
            mainPlayer.after(musicPlayer);
    }

    function registerMusicPlayerSetting() {
        if (!Lampa.SettingsApi || typeof Lampa.SettingsApi.addParam !== 'function') return;

        Lampa.SettingsApi.addParam({
            component: 'player',
            param: {
                name: MUSIC.storage.player,
                // валидные типы SettingsApi: select/trigger/input/title/static/button;
                // 'toggle' не существует — поле молча не рендерится
                type: 'select',
                values: buildMusicPlayerValues(),
                default: defaultMusicPlayerId(buildMusicPlayerValues())
            },
            field: {
                name: 'Тип плеера для музыки',
                description: 'Каким плеером воспроизводить музыку'
            }
        });

        if (Lampa.Settings && Lampa.Settings.listener && typeof Lampa.Settings.listener.follow === 'function') {
            Lampa.Settings.listener.follow('open', function (event) {
                if (event && event.name === 'player')
                    moveMusicPlayerSettingInPlayer(event.body);
            });
        }
    }

    function normalizeAudioProviderId(id) {
        return String(id || '').trim().toLowerCase();
    }

    function normalizeAudioProviders(list) {
        return (Array.isArray(list) ? list : []).map(function (item) {
            if (!item || !item.id) return null;

            return {
                id: normalizeAudioProviderId(item.id),
                name: item.name || item.id,
                capabilities: Array.isArray(item.capabilities) ? item.capabilities.slice() : []
            };
        }).filter(Boolean);
    }

    function getCachedAudioProviders() {
        return MUSIC_AUDIO_PROVIDERS && MUSIC_AUDIO_PROVIDERS.length
            ? MUSIC_AUDIO_PROVIDERS
            : AUDIO_PROVIDER_FALLBACKS;
    }

    function providerHasCapability(provider, capability) {
        if (!provider || !Array.isArray(provider.capabilities) || !capability) return false;
        return provider.capabilities.some(function (item) {
            return String(item || '').toLowerCase() === String(capability).toLowerCase();
        });
    }

    function providerSupportsPlaybackMode(provider, playbackMode) {
        if (!provider) return false;

        if (playbackMode === 'video')
            return providerHasCapability(provider, 'video');

        return providerHasCapability(provider, 'audio')
            || !provider.capabilities
            || !provider.capabilities.length;
    }

    function findAudioProviderById(id, providers) {
        id = normalizeAudioProviderId(id);
        providers = normalizeAudioProviders(providers || getCachedAudioProviders());

        return providers.filter(function (item) {
            return item.id === id;
        })[0] || null;
    }

    function getDefaultAudioProviderId(playbackMode, providers) {
        playbackMode = playbackMode || getPlaybackMode();
        providers = normalizeAudioProviders(providers || getCachedAudioProviders());

        var compatible = providers.filter(function (item) {
            return providerSupportsPlaybackMode(item, playbackMode);
        })[0];

        if (compatible && compatible.id) return compatible.id;
        if (playbackMode === 'video') return 'youtubeaudio';
        return 'youtubeaudio';
    }

    function ensureAudioProviderCompatibility(playbackMode, providers) {
        playbackMode = playbackMode || getPlaybackMode();

        // авторитетен только список с сервера (или явно переданный) — до его
        // загрузки работает неполный AUDIO_PROVIDER_FALLBACKS
        var authoritative = !!providers || !!(MUSIC_AUDIO_PROVIDERS && MUSIC_AUDIO_PROVIDERS.length);
        providers = normalizeAudioProviders(providers || getCachedAudioProviders());

        var currentId = normalizeAudioProviderId(Lampa.Storage.get(MUSIC.storage.audio_provider, ''));
        var current = findAudioProviderById(currentId, providers);

        if (current && providerSupportsPlaybackMode(current, playbackMode))
            return current.id;

        // пока список не авторитетен, сохранённый выбор НЕ затираем: раньше
        // холодный старт сбрасывал soundcloudaudio на дефолт ещё до ответа
        // /music/providers, и выбор пользователя «не сохранялся»
        if (!authoritative && currentId)
            return currentId;

        var nextId = getDefaultAudioProviderId(playbackMode, providers);
        setAudioProviderId(nextId);
        return nextId;
    }

    function getAudioProviderId(providers) {
        return ensureAudioProviderCompatibility(getPlaybackMode(), providers);
    }

    function setAudioProviderId(id) {
        Lampa.Storage.set(MUSIC.storage.audio_provider, normalizeAudioProviderId(id || 'youtubeaudio'));
    }

    function isQueueRestoreEnabled() {
        return Lampa.Storage.get(MUSIC.storage.queue_restore_enabled, false) === true;
    }

    function hasActiveMusicPlayback() {
        var data = activePlayerData();
        return isStandaloneIosAudioActive()
            || !!(data && data.from_music_cluster);
    }

    function setQueueRestoreEnabled(enabled) {
        enabled = enabled !== false;
        Lampa.Storage.set(MUSIC.storage.queue_restore_enabled, enabled);

        if (enabled) {
            scheduleQueueSnapshotSave(true);
            return;
        }

        clearQueueSnapshot();

        if (!hasActiveMusicPlayback()) {
            MUSIC_QUEUE.tracks = [];
            MUSIC_QUEUE.currentIndex = 0;
            MUSIC_QUEUE.currentTrackId = null;
            MUSIC_IOS_AUDIO.tracks = [];
            MUSIC_IOS_AUDIO.playlist = [];
            MUSIC_IOS_AUDIO.currentIndex = -1;
            MUSIC_IOS_AUDIO.active = false;
            MUSIC_IOS_AUDIO.switching = false;
            MUSIC_IOS_AUDIO.playing = false;
            updateStandaloneIosPlayerBar();
        }
    }

    function getAudioProviderTitle(id, providers) {
        var provider = findAudioProviderById(id || getAudioProviderId(providers), providers);
        if (provider && provider.name) return provider.name;

        id = normalizeAudioProviderId(id);
        if (id === 'sefonaudio') return 'Sefon';
        if (id === 'youtubeaudio') return 'YouTube Audio';
        if (id === 'yandexmusic') return 'Yandex Music';
        return id || 'YouTube Audio';
    }

    function getAudioProviderAvailabilitySubtitle(provider, playbackMode) {
        if (!provider) return '';
        if (providerSupportsPlaybackMode(provider, playbackMode)) return '';

        if (playbackMode === 'video')
            return 'Доступно только без видео';

        return 'Недоступно в этом режиме';
    }

    function supportsPlayPrefetch(providerId) {
        providerId = normalizeAudioProviderId(providerId || getAudioProviderId());
        return providerId === 'youtubeaudio';
    }

    function requiresPreparedInternalPlaylist(providerId) {
        providerId = normalizeAudioProviderId(providerId || getAudioProviderId());
        if (shouldPreferTrackNavigationControls()) return true;
        return providerId === 'sefonaudio';
    }

    function getPreparedPlaylistConcurrency(providerId) {
        providerId = normalizeAudioProviderId(providerId || getAudioProviderId());
        if (shouldPreferTrackNavigationControls()) return 2;
        if (providerId === 'soundcloudaudio') return 6;
        return providerId === 'sefonaudio' ? 4 : 2;
    }

    function shouldUseStagedStandaloneIosPreparation(providerId) {
        providerId = normalizeAudioProviderId(providerId || getAudioProviderId());
        return providerId === 'soundcloudaudio'
            || providerId === 'sefonaudio'
            || providerId === 'youtubeaudio';
    }

    function getStandaloneIosInitialResolveCount(providerId) {
        providerId = normalizeAudioProviderId(providerId || getAudioProviderId());
        if (providerId === 'soundcloudaudio') return 2;
        if (providerId === 'sefonaudio') return 2;
        if (providerId === 'youtubeaudio') return 3;
        return 0;
    }

    function fetchAudioProviders(done) {
        if (MUSIC_AUDIO_PROVIDERS && MUSIC_AUDIO_PROVIDERS.length) {
            done(MUSIC_AUDIO_PROVIDERS);
            return;
        }

        if (MUSIC_AUDIO_PROVIDERS_PENDING) {
            MUSIC_AUDIO_PROVIDER_WAITERS.push(done);
            return;
        }

        MUSIC_AUDIO_PROVIDERS_PENDING = true;
        MUSIC_AUDIO_PROVIDER_WAITERS.push(done);

        request(MUSIC.endpoints.providers, function (json) {
            var parsed = parseJson(json) || {};
            MUSIC_AUDIO_PROVIDERS = normalizeAudioProviders(parsed.audio);
            if (!MUSIC_AUDIO_PROVIDERS.length)
                MUSIC_AUDIO_PROVIDERS = normalizeAudioProviders(AUDIO_PROVIDER_FALLBACKS);

            var waiters = MUSIC_AUDIO_PROVIDER_WAITERS.slice();
            MUSIC_AUDIO_PROVIDER_WAITERS = [];
            MUSIC_AUDIO_PROVIDERS_PENDING = false;

            waiters.forEach(function (callback) {
                if (callback) callback(MUSIC_AUDIO_PROVIDERS);
            });
        }, function () {
            MUSIC_AUDIO_PROVIDERS = normalizeAudioProviders(AUDIO_PROVIDER_FALLBACKS);

            var waiters = MUSIC_AUDIO_PROVIDER_WAITERS.slice();
            MUSIC_AUDIO_PROVIDER_WAITERS = [];
            MUSIC_AUDIO_PROVIDERS_PENDING = false;

            waiters.forEach(function (callback) {
                if (callback) callback(MUSIC_AUDIO_PROVIDERS);
            });
        });
    }

    function getPlaybackModeTitle(mode) {
        mode = mode || getPlaybackMode();
        var found = PLAYBACK_MODE_ITEMS.filter(function (item) {
            return item.mode === mode;
        })[0];

        return found ? found.title : 'Без видео';
    }

    function getStreamModeTitle(mode) {
        mode = mode || getStreamMode();
        var found = STREAM_MODE_ITEMS.filter(function (item) {
            return item.mode === mode;
        })[0];

        return found ? found.title : 'Авто';
    }

    function getQualityModeTitle(mode, playbackMode) {
        mode = mode || getStreamMode();
        playbackMode = playbackMode || getPlaybackMode();

        var found = getQualityMenuItems(playbackMode).filter(function (item) {
            return item.mode === mode;
        })[0];

        return found ? found.title : getStreamModeTitle(mode);
    }

    function updateFilterButton(button) {
        if (!button || !button.length) return;
        button.attr('data-mode', getStreamMode());
        button.attr('data-playback-mode', getPlaybackMode());
        button.attr('data-audio-provider', getAudioProviderId());
        button.attr('title', 'Фильтр · ' + getPlaybackModeTitle() + ' · ' + getAudioProviderTitle(getAudioProviderId()) + ' · ' + getQualityModeTitle());
    }

    function buildPlayUrl(track) {
        return withIdentity(MUSIC.endpoints.play
            + '?' + buildTrackRequestParams(track)
            + '&audio_provider=' + encodeURIComponent(getAudioProviderId())
            + '&stream_mode=' + encodeURIComponent(getStreamMode())
            + '&playback_mode=' + encodeURIComponent(getPlaybackMode()));
    }

    function buildStreamUrl(track) {
        return withIdentity(MUSIC.endpoints.play.replace('/play', '/stream')
            + '?' + buildTrackRequestParams(track)
            + '&audio_provider=' + encodeURIComponent(getAudioProviderId())
            + '&stream_mode=' + encodeURIComponent(getStreamMode())
            + '&playback_mode=' + encodeURIComponent(getPlaybackMode()));
    }

    function getEntityProviderId(entity) {
        if (!entity) return '';
        if (entity.provider) return String(entity.provider).trim();

        if (Array.isArray(entity.provider_refs)) {
            for (var i = 0; i < entity.provider_refs.length; i++) {
                var providerRef = entity.provider_refs[i];
                if (providerRef && providerRef.provider) return String(providerRef.provider).trim();
            }
        }

        return '';
    }

    function getTrackProviderId(track) {
        return getEntityProviderId(track);
    }

    function extractYouTubeTrackId(track) {
        if (!track) return '';

        var id = normalizeYouTubeVideoId(track.id);
        if (id) return id;

        if (Array.isArray(track.provider_refs)) {
            for (var i = 0; i < track.provider_refs.length; i++) {
                var providerRef = track.provider_refs[i];
                if (!providerRef || String(providerRef.provider || '').toLowerCase() !== 'youtubeaudio') continue;

                var externalId = normalizeYouTubeVideoId(providerRef.external_id);
                if (externalId) return externalId;
            }
        }

        return '';
    }

    function normalizeYouTubeVideoId(id) {
        id = String(id || '').trim();
        if (!id) return '';

        if (/^youtube:/i.test(id))
            id = id.replace(/^youtube:/i, '').trim();

        return /^[A-Za-z0-9_-]{6,32}$/.test(id) ? id : '';
    }

    function extractYouTubeMatchId(json) {
        var match = json && json.selected_match;
        if (!match || String(match.provider_id || '').toLowerCase() !== 'youtubeaudio') return '';

        var id = normalizeYouTubeVideoId(match.id);
        if (id) return id;

        if (match.payload) {
            try {
                var payload = typeof match.payload === 'string' ? JSON.parse(match.payload) : match.payload;
                return normalizeYouTubeVideoId(payload && payload.video_id);
            } catch (e) {}
        }

        return '';
    }

    function extractResolvedYouTubeTrackId(track, json) {
        return extractYouTubeMatchId(json) || extractYouTubeTrackId(track);
    }

    function isDirectArtistEntity(artist) {
        if (!artist) return false;

        var id = String(artist.id || '');
        var provider = getEntityProviderId(artist);

        return provider === 'youtubeaudio'
            || provider === 'soundcloudcharts'
            || /^youtube:channel:/i.test(id)
            || /^soundcloud:user:/i.test(id);
    }

    function buildTrackRequestParams(track) {
        var parts = [];

        if (track && track.id) parts.push('id=' + encodeURIComponent(track.id));
        if (track && getTrackProviderId(track)) parts.push('provider=' + encodeURIComponent(getTrackProviderId(track)));
        if (track && track.title) parts.push('title=' + encodeURIComponent(track.title));
        if (track && track.artist_name) parts.push('artist_name=' + encodeURIComponent(track.artist_name));
        if (track && track.album_title) parts.push('album_title=' + encodeURIComponent(track.album_title));
        if (track && (track.duration_ms || track.duration_ms === 0)) parts.push('duration_ms=' + encodeURIComponent(track.duration_ms));
        if (track && track.date) parts.push('date=' + encodeURIComponent(track.date));

        return parts.join('&');
    }

    function buildPlayCacheKey(track) {
        return [
            buildTrackRequestParams(track),
            'audio_provider=' + getAudioProviderId(),
            'stream_mode=' + getStreamMode(),
            'playback_mode=' + getPlaybackMode()
        ].join('&');
    }

    function getCachedPlayResponse(track) {
        var key = buildPlayCacheKey(track);
        var cached = PLAY_PREFETCH_CACHE[key];

        if (!cached) return null;
        if (cached.expires <= Date.now()) {
            delete PLAY_PREFETCH_CACHE[key];
            return null;
        }

        return cached.response || null;
    }

    function saveCachedPlayResponse(track, response, cacheKey) {
        if (!track || !response || !response.available || !response.sources || !response.sources.length) return;

        PLAY_PREFETCH_CACHE[cacheKey || buildPlayCacheKey(track)] = {
            response: response,
            expires: Date.now() + PLAY_PREFETCH_TTL
        };
    }

    function invalidateTrackPlayCache(track) {
        if (!track) return;

        var matchId = track.id ? String(track.id) : '';
        Object.keys(PLAY_PREFETCH_CACHE).forEach(function (key) {
            if (matchId && key.indexOf('id=' + encodeURIComponent(matchId)) >= 0)
                delete PLAY_PREFETCH_CACHE[key];
        });
        Object.keys(PLAY_PREFETCH_PENDING).forEach(function (key) {
            if (matchId && key.indexOf('id=' + encodeURIComponent(matchId)) >= 0)
                delete PLAY_PREFETCH_PENDING[key];
        });
    }

    function schedulePlayPrefetch(track) {
        if (!track || !track.title) return;
        if (!supportsPlayPrefetch()) return;

        if (PLAY_PREFETCH_TIMER) {
            clearTimeout(PLAY_PREFETCH_TIMER);
            PLAY_PREFETCH_TIMER = 0;
        }

        PLAY_PREFETCH_TIMER = setTimeout(function () {
            PLAY_PREFETCH_TIMER = 0;
            requestPlay(track, function () {}, function () {});
        }, 180);
    }

    function getQualityMenuItems(playbackMode) {
        playbackMode = playbackMode || getPlaybackMode();

        return STREAM_MODE_ITEMS.map(function (item) {
            if (playbackMode === 'video' && item.mode === 'm4a')
                return {
                    mode: item.mode,
                    title: 'Предпочитать mp4'
                };

            return item;
        });
    }

    function openPlaybackModeMenu(button, focusContext) {
        var currentMode = getPlaybackMode();
        Lampa.Select.show({
            title: 'Режим воспроизведения',
            items: PLAYBACK_MODE_ITEMS.map(function (item) {
                return {
                    title: item.title,
                    mode: item.mode,
                    selected: item.mode === currentMode
                };
            }),
            onBack: function () {
                openFilterMenu(button, focusContext);
            },
            onSelect: function (selected) {
                if (!selected || !selected.mode) return;
                setPlaybackMode(selected.mode);
                ensureAudioProviderCompatibility(selected.mode);
                updateFilterButton(button);
                Lampa.Noty.show('Режим: ' + getPlaybackModeTitle(selected.mode));
                restoreMusicFocusContext(focusContext, 120);
            }
        });
    }

    function openAudioProviderMenu(button, focusContext) {
        fetchAudioProviders(function (providers) {
            var playbackMode = getPlaybackMode();
            var items = normalizeAudioProviders(providers);
            var currentId = getAudioProviderId(items);

            if (!items.length) {
                Lampa.Noty.show('Подходящих провайдеров нет.');
                restoreMusicFocusContext(focusContext, 120);
                return;
            }

            Lampa.Select.show({
                title: 'Источник звука',
                items: items.map(function (item) {
                    return {
                        title: item.name,
                        subtitle: getAudioProviderAvailabilitySubtitle(item, playbackMode),
                        provider_id: item.id,
                        selected: item.id === currentId,
                        disabled: !providerSupportsPlaybackMode(item, playbackMode)
                    };
                }),
                onBack: function () {
                    openFilterMenu(button, focusContext);
                },
                onSelect: function (selected) {
                    if (!selected || !selected.provider_id) return;
                    if (selected.disabled) {
                        Lampa.Noty.show(selected.subtitle || 'Провайдер недоступен в этом режиме.');
                        restoreMusicFocusContext(focusContext, 120);
                        return;
                    }
                    setAudioProviderId(selected.provider_id);
                    updateFilterButton(button);
                    Lampa.Noty.show('Источник: ' + getAudioProviderTitle(selected.provider_id, items));
                    restoreMusicFocusContext(focusContext, 120);
                }
            });
        });
    }

    function openStreamModeMenu(button, focusContext) {
        var currentMode = getStreamMode();
        var playbackMode = getPlaybackMode();
        Lampa.Select.show({
            title: 'Качество',
            items: getQualityMenuItems(playbackMode).map(function (item) {
                return {
                    title: item.title,
                    mode: item.mode,
                    selected: item.mode === currentMode
                };
            }),
            onBack: function () {
                openFilterMenu(button, focusContext);
            },
            onSelect: function (selected) {
                if (!selected || !selected.mode) return;
                setStreamMode(selected.mode);
                updateFilterButton(button);
                Lampa.Noty.show('Качество: ' + getQualityModeTitle(selected.mode));
                restoreMusicFocusContext(focusContext, 120);
            }
        });
    }

    function openFilterMenu(button, focusContext) {
        focusContext = focusContext || captureMusicFocusContext('content');

        var currentMode = getStreamMode();
        var playbackMode = getPlaybackMode();

        Lampa.Select.show({
            title: 'Фильтр',
            items: [
                {
                    title: 'Режим воспроизведения',
                    subtitle: getPlaybackModeTitle(playbackMode),
                    action: 'playback_mode'
                },
                {
                    title: 'Источник звука',
                    subtitle: getAudioProviderTitle(getAudioProviderId()),
                    action: 'audio_provider'
                },
                {
                    title: 'Качество',
                    subtitle: getQualityModeTitle(currentMode, playbackMode),
                    action: 'stream_mode'
                },
                {
                    title: 'Восстановление очереди',
                    subtitle: isQueueRestoreEnabled() ? 'Включено' : 'Выключено',
                    action: 'queue_restore'
                },
                {
                    title: 'Автоподборка треков',
                    subtitle: isRadioAutoplayEnabled() ? 'Включено' : 'Выключено',
                    action: 'radio_autoplay'
                }
            ],
            onBack: function () {
                restoreMusicFocusContext(focusContext, 0);
            },
            onSelect: function (selected) {
                if (!selected || !selected.action) {
                    restoreMusicFocusContext(focusContext);
                    return;
                }

                if (selected.action === 'playback_mode') {
                    openPlaybackModeMenu(button, focusContext);
                    return;
                }

                if (selected.action === 'audio_provider') {
                    openAudioProviderMenu(button, focusContext);
                    return;
                }

                if (selected.action === 'stream_mode') {
                    openStreamModeMenu(button, focusContext);
                    return;
                }

                if (selected.action === 'queue_restore') {
                    var enabled = !isQueueRestoreEnabled();
                    setQueueRestoreEnabled(enabled);
                    Lampa.Noty.show(enabled ? 'Восстановление очереди включено.' : 'Восстановление очереди выключено.');
                    restoreMusicFocusContext(focusContext, 120);
                    return;
                }

                if (selected.action === 'radio_autoplay') {
                    var radioEnabled = !isRadioAutoplayEnabled();
                    setRadioAutoplayEnabled(radioEnabled);
                    Lampa.Noty.show(radioEnabled ? 'Автоподборка треков включена.' : 'Автоподборка треков выключена.');
                    restoreMusicFocusContext(focusContext, 120);
                }
            }
        });
    }

    // ===== ARTWORK / LOCAL STATE =====

    function artworkHash(value) {
        var hash = 2166136261;
        value = String(value || '');

        for (var i = 0; i < value.length; i++) {
            hash ^= value.charCodeAt(i);
            hash += (hash << 1) + (hash << 4) + (hash << 7) + (hash << 8) + (hash << 24);
        }

        return hash >>> 0;
    }

    function artworkPalette(kind, title, subtitle, colors) {
        var palettes = [
            { base: '#11161d', deep: '#2f4651', accent: '#83c7bd', accent2: '#e4b86e', line: '#f0f4ef' },
            { base: '#161822', deep: '#4b3448', accent: '#d9a56b', accent2: '#88c7d0', line: '#f8efe5' },
            { base: '#101820', deep: '#3a574b', accent: '#bdd27b', accent2: '#e4a56f', line: '#eef3df' },
            { base: '#17151c', deep: '#35435e', accent: '#9eb7e5', accent2: '#e2c278', line: '#edf1fb' },
            { base: '#151a18', deep: '#533d37', accent: '#e0a77c', accent2: '#83c5c0', line: '#f5eee8' },
            { base: '#121922', deep: '#3c4f3f', accent: '#d4d889', accent2: '#8fc3de', line: '#f2f3e7' }
        ];
        var selected = palettes[artworkHash(kind + '|' + title + '|' + subtitle) % palettes.length];

        return {
            base: colors && colors[0] ? colors[0] : selected.base,
            deep: colors && colors[1] ? colors[1] : selected.deep,
            accent: selected.accent,
            accent2: selected.accent2,
            line: selected.line
        };
    }

    function artworkSymbol(kind, palette) {
        if (kind === 'artist') {
            return [
                '<g transform="translate(250 250)">',
                '<circle cx="0" cy="0" r="122" fill="' + palette.line + '" opacity="0.10"/>',
                '<circle cx="0" cy="-36" r="54" fill="' + palette.line + '" opacity="0.76"/>',
                '<path d="M-96 110c15-54 52-82 96-82s81 28 96 82" fill="' + palette.line + '" opacity="0.54"/>',
                '</g>'
            ].join('');
        }

        if (kind === 'track') {
            return [
                '<g transform="translate(250 250)">',
                '<circle cx="0" cy="0" r="126" fill="' + palette.line + '" opacity="0.08"/>',
                '<path d="M36-100v142c-16-10-38-12-58-5-34 11-52 40-42 65s45 35 79 24c28-9 45-31 45-57V-38l92-18v-58L36-100Z" fill="' + palette.line + '" opacity="0.82"/>',
                '</g>'
            ].join('');
        }

        if (kind === 'playlist' || kind === 'playlist_add') {
            return [
                '<g transform="translate(250 250)">',
                '<rect x="-108" y="-112" width="216" height="224" rx="30" fill="' + palette.line + '" opacity="0.10" stroke="' + palette.line + '" stroke-opacity="0.16" stroke-width="2"/>',
                kind === 'playlist_add'
                    ? '<path d="M0-58v116m-58-58h116" stroke="' + palette.line + '" stroke-width="20" stroke-linecap="round" opacity="0.82"/>'
                    : '<path d="M-46-58h112M-46 0h112M-46 58h78" stroke="' + palette.line + '" stroke-width="18" stroke-linecap="round" opacity="0.76"/><circle cx="-74" cy="-58" r="9" fill="' + palette.accent2 + '" opacity="0.82"/><circle cx="-74" cy="0" r="9" fill="' + palette.accent2 + '" opacity="0.64"/><circle cx="-74" cy="58" r="9" fill="' + palette.accent2 + '" opacity="0.48"/>',
                '</g>'
            ].join('');
        }

        return [
            '<g transform="translate(250 252)">',
            '<circle cx="58" cy="-2" r="116" fill="#05070a" opacity="0.34"/>',
            '<circle cx="58" cy="-2" r="40" fill="' + palette.accent2 + '" opacity="0.54"/>',
            '<rect x="-136" y="-132" width="212" height="212" rx="28" fill="' + palette.line + '" opacity="0.16" stroke="' + palette.line + '" stroke-opacity="0.16" stroke-width="2"/>',
            '<path d="M-30-70v100c-14-8-32-10-49-4-27 9-42 33-34 53 9 20 38 28 65 19 23-8 37-26 37-46V-14l70-14v-48L-30-70Z" fill="' + palette.line + '" opacity="0.74"/>',
            '</g>'
        ].join('');
    }

    function artwork(kind, title, subtitle, colors) {
        kind = kind || 'album';
        var palette = artworkPalette(kind, title, subtitle, colors);
        var svg = [
            '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 500 500">',
            '<defs>',
            '<linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">',
            '<stop offset="0%" stop-color="' + palette.base + '"/>',
            '<stop offset="100%" stop-color="' + palette.deep + '"/>',
            '</linearGradient>',
            '</defs>',
            '<rect width="500" height="500" rx="28" fill="url(#bg)"/>',
            '<circle cx="410" cy="86" r="150" fill="' + palette.accent + '" opacity="0.16"/>',
            '<circle cx="76" cy="424" r="166" fill="' + palette.accent2 + '" opacity="0.10"/>',
            '<rect x="18" y="18" width="464" height="464" rx="24" fill="none" stroke="' + palette.line + '" stroke-opacity="0.12" stroke-width="2"/>',
            artworkSymbol(kind, palette),
            '</svg>'
        ].join('');

        return encodeSvg(svg);
    }

    function selectSizedImageEntry(images, desiredSize) {
        if (!images || !images.length) return null;

        if (!desiredSize)
            return images[0] && images[0].url ? images[0] : null;

        var filtered = images.filter(function (item) {
            return item && item.url;
        });

        if (!filtered.length) return null;

        filtered.sort(function (a, b) {
            var aw = a.width || 0;
            var bw = b.width || 0;
            return aw - bw;
        });

        for (var i = 0; i < filtered.length; i++) {
            var width = filtered[i].width || 0;
            if (width >= desiredSize) return filtered[i];
        }

        return filtered[filtered.length - 1];
    }

    function selectSizedImage(images, desiredSize) {
        var entry = selectSizedImageEntry(images, desiredSize);
        return entry && entry.url ? entry.url : '';
    }

    function artistImage(artist, desiredSize) {
        var remote = '';

        if (artist && artist.image) {
            remote = artist.image;
        }
        else if (artist && artist.images) {
            var selected = selectSizedImageEntry(artist.images, desiredSize);

            if (selected && (selected.width || 0) === 250) {
                var fallback = artist.images
                    .filter(function (item) {
                        return item && item.url && (item.width || 0) > 250;
                    })
                    .sort(function (a, b) {
                        return (a.width || 0) - (b.width || 0);
                    })[0];

                if (fallback) selected = fallback;
            }

            remote = selected && selected.url ? selected.url : '';
        }

        return remote || artwork('artist', artist.name, artist.country || artist.sort_name || 'Artist', ['#172432', '#356181']);
    }

    function artistHasRemoteImage(artist) {
        if (!artist || !artist.images || !artist.images.length) return false;

        var url = selectSizedImage(artist.images, 150);
        return !!(url && !/^data:/i.test(url));
    }

    function artistImageKey(artist) {
        return [
            artist && artist.id ? artist.id : '',
            artist && artist.name ? artist.name : '',
            artist && artist.country ? artist.country : ''
        ].join('|').trim().toLowerCase();
    }

    function buildArtistImageUrl(artist) {
        var url = MUSIC.endpoints.artistImage;

        if (artist && artist.id)
            url = Lampa.Utils.addUrlComponent(url, 'id=' + encodeURIComponent(artist.id));

        if (artist && artist.name)
            url = Lampa.Utils.addUrlComponent(url, 'name=' + encodeURIComponent(artist.name));

        if (artist && artist.country)
            url = Lampa.Utils.addUrlComponent(url, 'country=' + encodeURIComponent(artist.country));

        return url;
    }

    function flushArtistImageQueue(key, images) {
        ARTIST_IMAGE_CACHE[key] = images || [];

        var state = ARTIST_IMAGE_PENDING[key];
        delete ARTIST_IMAGE_PENDING[key];

        if (!state || !state.callbacks) return;

        state.callbacks.forEach(function (callback) {
            if (callback) callback(images || []);
        });
    }

    function pumpArtistImageQueue() {
        while (ARTIST_IMAGE_ACTIVE < ARTIST_IMAGE_CONCURRENCY && ARTIST_IMAGE_QUEUE.length) {
            var key = ARTIST_IMAGE_QUEUE.shift();
            var state = ARTIST_IMAGE_PENDING[key];

            if (!state || !state.artist) continue;

            ARTIST_IMAGE_ACTIVE++;

            request(buildArtistImageUrl(state.artist), function (json) {
                var parsed = parseJson(json) || {};
                var images = Array.isArray(parsed.images) ? parsed.images : [];

                flushArtistImageQueue(key, images);
                ARTIST_IMAGE_ACTIVE = Math.max(0, ARTIST_IMAGE_ACTIVE - 1);
                pumpArtistImageQueue();
            }, function () {
                flushArtistImageQueue(key, []);
                ARTIST_IMAGE_ACTIVE = Math.max(0, ARTIST_IMAGE_ACTIVE - 1);
                pumpArtistImageQueue();
            });
        }
    }

    function requestArtistImage(artist, callback) {
        if (!artist || !artist.name) {
            if (callback) callback([]);
            return;
        }

        if (artistHasRemoteImage(artist)) {
            if (callback) callback(artist.images || []);
            return;
        }

        var key = artistImageKey(artist);
        if (!key) {
            if (callback) callback([]);
            return;
        }

        if (Object.prototype.hasOwnProperty.call(ARTIST_IMAGE_CACHE, key)) {
            if (callback) callback(ARTIST_IMAGE_CACHE[key] || []);
            return;
        }

        if (ARTIST_IMAGE_PENDING[key]) {
            if (callback) ARTIST_IMAGE_PENDING[key].callbacks.push(callback);
            return;
        }

        ARTIST_IMAGE_PENDING[key] = {
            artist: artist,
            callbacks: callback ? [callback] : []
        };

        ARTIST_IMAGE_QUEUE.push(key);
        pumpArtistImageQueue();
    }

    function albumImage(album, size) {
        var remote = album && album.image
            ? album.image
            : album && album.images ? selectSizedImage(album.images, size || 250) : '';

        return remote || artwork('album', album.title, album.artist_name || 'Album', ['#20263a', '#475a7b']);
    }

    function trackImage(track) {
        var remote = track && track.image
            ? track.image
            : track && track.images ? selectSizedImage(track.images, 250) : '';

        return remote || artwork('track', track.title, track.artist_name || track.album_title || 'Track', ['#1d2128', '#46505e']);
    }

    function setBackground(url) {
        Lampa.Background.change(url || IMG_BG);
    }

    function emitRecentChanged(sectionKey, payload) {
        if (isLampaPlayerOverlayOpen()) {
            MUSIC_DEFERRED_HOME_REFRESH = true;
            traceEmbeddedIos('recent-event-deferred', sectionKey || '', true);
            ensureLampaPlayerController('recent-event-deferred');
            return;
        }

        window.dispatchEvent(new CustomEvent(MUSIC_RECENT_EVENT, {
            detail: {
                section_key: sectionKey,
                payload: payload || null
            }
        }));
    }

    function isLampaPlayerOverlayOpen() {
        try {
            return !!(Lampa.Player && typeof Lampa.Player.opened === 'function' && Lampa.Player.opened());
        } catch (e) {
            return false;
        }
    }

    function getLampaControllerName() {
        try {
            var enabled = Lampa.Controller && Lampa.Controller.enabled ? Lampa.Controller.enabled() : null;
            return enabled && enabled.name ? String(enabled.name) : '';
        } catch (e) {
            return '';
        }
    }

    function isLampaPlayerControllerName(name) {
        name = String(name || '');
        return name.indexOf('player') === 0;
    }

    function shouldSkipPlayerControllerRestore(name) {
        name = String(name || '');
        return name === 'select' || name === 'modal' || name === 'keyboard' || name.indexOf('settings') === 0;
    }

    function ensureLampaPlayerController(origin) {
        if (!isLampaPlayerOverlayOpen()) return false;

        var data = activePlayerData();
        if (!data || !data.from_music_cluster) return false;

        var name = getLampaControllerName();
        if (isLampaPlayerControllerName(name) || shouldSkipPlayerControllerRestore(name))
            return false;

        var now = Date.now();
        if (now - MUSIC_LAST_PLAYER_CONTROLLER_RESTORE_AT < 500)
            return false;

        MUSIC_LAST_PLAYER_CONTROLLER_RESTORE_AT = now;

        try {
            if (Lampa.Controller && typeof Lampa.Controller.toggle === 'function') {
                Lampa.Controller.toggle('player');
                traceEmbeddedIos('controller-restored', (origin || '') + ' from=' + name, true);
                return true;
            }
        } catch (e) {
            traceEmbeddedIos('controller-restore-error', describeError(e), true);
        }

        return false;
    }

    function bindRecentListener(handler) {
        if (!handler) return function () {};
        window.addEventListener(MUSIC_RECENT_EVENT, handler);
        return function () {
            window.removeEventListener(MUSIC_RECENT_EVENT, handler);
        };
    }

    function notifyPlaylistChanged(playlistId, options) {
        options = options || {};
        playlistId = String(playlistId || '').trim();

        if (!options.preserveHomeCache) {
            MUSIC_HOME_CACHE.user_playlists = [];
            MUSIC_HOME_SECTION_META.user_playlists = { has_more: false };
        }

        if (!options.skipHomeSection) {
            emitRecentChanged('user_playlists', {
                playlist_id: playlistId,
                restoreState: options.restoreState || null,
                skipActiveSectionRefresh: options.skipActiveSectionRefresh === true
            });
        }

        if (playlistId && !options.skipPlaylistSection)
            emitRecentChanged('playlist:' + playlistId, { playlist_id: playlistId });
    }

    function cleanRecentAlbumArtist(value) {
        value = String(value || '').trim();
        if (!value) return '';

        return value
            .replace(/\s*[·•]\s*\d{4}.*$/i, '')
            .replace(/\s*[·•]\s*album.*$/i, '')
            .trim();
    }

    function recentAlbumIdentity(album) {
        if (!album) return '';

        var title = normalizeText(album.title || album.album_title || '');
        var artist = normalizeText(album.artist_name || album.artist || album.album_artist || '');

        if (!artist && album.subtitle)
            artist = normalizeText(cleanRecentAlbumArtist(album.subtitle));

        if (title && artist)
            return 'album:' + artist + '|' + title;

        return album.id ? 'id:' + String(album.id) : '';
    }

    function recentArtistIdentity(artist) {
        if (!artist) return '';

        var name = normalizeText(artist.name || artist.title || artist.artist_name || artist.sort_name || '');

        if (name)
            return 'artist:' + name;

        return artist.id ? 'id:' + String(artist.id) : '';
    }

    function homeCacheEntryIdentity(sectionKey, entry) {
        if (!entry) return '';

        if (sectionKey === 'recent_albums')
            return recentAlbumIdentity(entry.raw || entry);

        if (sectionKey === 'recent_artists')
            return recentArtistIdentity(entry.raw || entry);

        return entry.id ? 'id:' + String(entry.id) : '';
    }

    function recentEntityIdentity(key, entity) {
        if (!entity) return '';

        if (key === MUSIC.storage.recent_albums)
            return recentAlbumIdentity(entity);

        if (key === MUSIC.storage.recent_artists)
            return recentArtistIdentity(entity);

        return entity.id ? 'id:' + String(entity.id) : '';
    }

    function dedupeRecentEntities(key, list, limit) {
        var seen = {};
        var result = [];

        (Array.isArray(list) ? list : []).forEach(function (item) {
            var identity = recentEntityIdentity(key, item);
            if (!identity || seen[identity]) return;

            seen[identity] = true;
            result.push(item);
        });

        return result.slice(0, limit || RECENT_SECTION_STORAGE_LIMIT);
    }

    function touchHomeCacheEntry(sectionKey, entry, limit) {
        if (!sectionKey || !entry || !entry.id) return;

        var identity = homeCacheEntryIdentity(sectionKey, entry);
        if (!identity) return;

        var list = Array.isArray(MUSIC_HOME_CACHE[sectionKey]) ? MUSIC_HOME_CACHE[sectionKey].slice() : [];
        list = list.filter(function (item) {
            return item && homeCacheEntryIdentity(sectionKey, item) !== identity;
        });
        list.unshift(entry);
        MUSIC_HOME_CACHE[sectionKey] = list.slice(0, limit || RECENT_SECTION_STORAGE_LIMIT);
    }

    function updateHomeSectionMetaFromCache(sectionKey) {
        if (!sectionKey) return;

        var entries = Array.isArray(MUSIC_HOME_CACHE[sectionKey]) ? MUSIC_HOME_CACHE[sectionKey] : [];
        MUSIC_HOME_SECTION_META[sectionKey] = {
            has_more: entries.length > HOME_SECTION_LIMIT
        };
    }

    function hasHomeCacheEntry(sectionKey, entryId) {
        if (!sectionKey || !entryId || !Array.isArray(MUSIC_HOME_CACHE[sectionKey])) return false;

        return MUSIC_HOME_CACHE[sectionKey].some(function (item) {
            return item && item.id === entryId;
        });
    }

    function removeHomeCacheEntry(sectionKey, entityOrId) {
        if (!sectionKey || !entityOrId || !Array.isArray(MUSIC_HOME_CACHE[sectionKey])) return;

        var targetId = typeof entityOrId === 'object' ? entityOrId.id : entityOrId;
        var targetIdentity = typeof entityOrId === 'object' ? homeCacheEntryIdentity(sectionKey, entityOrId) : '';

        MUSIC_HOME_CACHE[sectionKey] = MUSIC_HOME_CACHE[sectionKey].filter(function (item) {
            if (!item) return false;
            if (targetIdentity && homeCacheEntryIdentity(sectionKey, item) === targetIdentity) return false;
            return item.id !== targetId;
        });
    }

    function normalizeRecentQueryItem(item) {
        if (!item) return null;

        if (typeof item === 'string') {
            var value = String(item || '').trim();
            return value ? { query: value, artist: null } : null;
        }

        if (typeof item === 'object') {
            var query = String(item.query || item.title || '').trim();
            if (!query) return null;

            return {
                query: query,
                artist: item.artist || null
            };
        }

        return null;
    }

    function snapshotRecentQueryArtist(artist) {
        if (!artist || !artist.id) return null;

        return {
            id: artist.id,
            name: artist.name || '',
            sort_name: artist.sort_name || '',
            country: artist.country || '',
            images: Array.isArray(artist.images) ? artist.images.slice() : []
        };
    }

    function updateRecentQueriesCache(emitPayload) {
        var queries = getRecentQueries();
        Lampa.Storage.set(MUSIC.storage.recent_queries, queries.slice(0, RECENT_SECTION_STORAGE_LIMIT));
        MUSIC_HOME_CACHE.recent_queries = applySectionKey(queries.map(mapQueryCard), 'recent_queries');
        MUSIC_HOME_SECTION_META.recent_queries = { has_more: queries.length > HOME_SECTION_LIMIT };

        if (typeof emitPayload !== 'undefined')
            emitRecentChanged('recent_queries', emitPayload);
    }

    function saveLastQuery(query) {
        query = String(query || '').trim();
        if (!query) return;

        Lampa.Storage.set(MUSIC.storage.last_query, query);

        var existing = null;
        var list = getRecentQueries().filter(function (item) {
            if (!item) return false;

            if (item.query.toLowerCase() === String(query).toLowerCase()) {
                existing = item;
                return false;
            }

            return true;
        });

        list.unshift({
            query: query,
            artist: existing && existing.artist ? existing.artist : null
        });

        Lampa.Storage.set(MUSIC.storage.recent_queries, list.slice(0, RECENT_SECTION_STORAGE_LIMIT));
        updateRecentQueriesCache(query);
    }

    function clearRecentQueries() {
        Lampa.Storage.set(MUSIC.storage.recent_queries, []);
        MUSIC_HOME_CACHE.recent_queries = [];
        MUSIC_HOME_SECTION_META.recent_queries = { has_more: false };
        emitRecentChanged('recent_queries', null);
    }

    function removeRecentQuery(query, options) {
        options = options || {};
        query = String(query || '').trim();
        if (!query) return false;

        var list = getRecentQueries();
        var next = list.filter(function (item) {
            return item && item.query.toLowerCase() !== query.toLowerCase();
        });

        if (next.length === list.length) return false;

        Lampa.Storage.set(MUSIC.storage.recent_queries, next);
        MUSIC_HOME_CACHE.recent_queries = applySectionKey(next.map(mapQueryCard), 'recent_queries');
        MUSIC_HOME_SECTION_META.recent_queries = { has_more: next.length > HOME_SECTION_LIMIT };
        if (!options.skipEvent)
            emitRecentChanged('recent_queries', query);
        return true;
    }

    function removeRecentEntity(key, entityOrId, options) {
        options = options || {};
        var entityId = typeof entityOrId === 'object' ? entityOrId.id : entityOrId;
        if (!key || !entityId) return false;

        var list = Lampa.Storage.get(key, []);
        list = Array.isArray(list) ? list : [];
        var targetIdentity = typeof entityOrId === 'object' ? recentEntityIdentity(key, entityOrId) : '';

        var next = list.filter(function (item) {
            if (!item) return false;
            if (targetIdentity && recentEntityIdentity(key, item) === targetIdentity) return false;
            return item.id !== entityId;
        });

        Lampa.Storage.set(key, next);
        var sectionKey = key === MUSIC.storage.recent_albums
            ? 'recent_albums'
            : key === MUSIC.storage.recent_artists
                ? 'recent_artists'
                : '';

        if (sectionKey) {
            removeHomeCacheEntry(sectionKey, entityOrId);
            MUSIC_HOME_SECTION_META[sectionKey] = {
                has_more: next.length > HOME_SECTION_LIMIT
            };
            if (!options.skipEvent)
                emitRecentChanged(sectionKey, entityId);
        }

        return next.length !== list.length;
    }

    function getLastQuery() {
        return Lampa.Storage.get(MUSIC.storage.last_query, 'Nirvana');
    }

    function getRecentQueries() {
        var list = Lampa.Storage.get(MUSIC.storage.recent_queries, []);
        return Array.isArray(list) ? list.map(normalizeRecentQueryItem).filter(Boolean).slice(0, RECENT_SECTION_STORAGE_LIMIT) : [];
    }

    function updateRecentQueryArtist(query, artist, emitChange) {
        query = String(query || '').trim();
        if (!query || !artist || !artist.id) return false;

        var changed = false;
        var next = getRecentQueries().map(function (item) {
            if (!item || item.query.toLowerCase() !== query.toLowerCase()) return item;

            var snapshot = snapshotRecentQueryArtist(artist);
            if (!snapshot) return item;

            var prevKey = JSON.stringify(item.artist || null);
            var nextKey = JSON.stringify(snapshot);
            if (prevKey === nextKey) return item;

            changed = true;

            return {
                query: item.query,
                artist: snapshot
            };
        });

        if (!changed) return false;

        Lampa.Storage.set(MUSIC.storage.recent_queries, next.slice(0, RECENT_SECTION_STORAGE_LIMIT));
        updateRecentQueriesCache(emitChange === false ? undefined : query);
        return true;
    }

    function openSearchQuery(query) {
        query = String(query || '').trim();
        if (!query) return;

        saveLastQuery(query);
        openSearchScreen(query);
    }

    function openSearchScreen(query) {
        query = String(query || '').trim();
        var payload = {
            title: 'Поиск',
            component: 'lampac_music_search',
            search_mode: true,
            query: query,
            page: 1,
            noinfo: true
        };
        var active = Lampa.Activity.active();

        if (active && active.component === 'lampac_music_search') {
            Lampa.Activity.replace(payload);
            return;
        }

        Lampa.Activity.push(payload);
    }

    function saveRecentEntity(key, entity) {
        if (!entity || !entity.id) return;

        var list = Lampa.Storage.get(key, []);
        list = Array.isArray(list) ? list : [];
        var identity = recentEntityIdentity(key, entity);
        list = list.filter(function (item) {
            return item && recentEntityIdentity(key, item) !== identity;
        });
        list.unshift(entity);
        list = dedupeRecentEntities(key, list, RECENT_SECTION_STORAGE_LIMIT);
        Lampa.Storage.set(key, list);

        if (key === MUSIC.storage.recent_albums) {
            touchHomeCacheEntry('recent_albums', mapAlbumCard(entity), RECENT_SECTION_STORAGE_LIMIT);
            MUSIC_HOME_SECTION_META.recent_albums = { has_more: list.length > HOME_SECTION_LIMIT };
            emitRecentChanged('recent_albums', entity);
        }

        if (key === MUSIC.storage.recent_artists) {
            touchHomeCacheEntry('recent_artists', mapArtistCard(entity), RECENT_SECTION_STORAGE_LIMIT);
            MUSIC_HOME_SECTION_META.recent_artists = { has_more: list.length > HOME_SECTION_LIMIT };
            emitRecentChanged('recent_artists', entity);
        }
    }

    function getRecentEntities(key) {
        var list = Lampa.Storage.get(key, []);
        list = Array.isArray(list) ? list.filter(function (item) {
            return item && item.id;
        }) : [];

        if (key === MUSIC.storage.recent_albums || key === MUSIC.storage.recent_artists) {
            var deduped = dedupeRecentEntities(key, list, RECENT_SECTION_STORAGE_LIMIT);
            if (deduped.length !== list.length)
                Lampa.Storage.set(key, deduped);
            return deduped;
        }

        return list;
    }

    // мемоизация закладок: isBookmarkedEntity дёргается тиком фулл-плеера
    // 2–4 раза в секунду (heat-probe: fullPlayerActionsUpdate), и каждый вызов
    // парсил массив из Storage заново. Все записи идут через save/remove ниже —
    // там инвалидация; читатели не мутируют возвращённый массив (проверено)
    var MUSIC_BOOKMARK_CACHE = {};

    function getBookmarkedEntities(key) {
        if (MUSIC_BOOKMARK_CACHE[key]) return MUSIC_BOOKMARK_CACHE[key];

        var list = Lampa.Storage.get(key, []);
        list = Array.isArray(list) ? list.filter(function (item) {
            return item && item.id;
        }) : [];

        MUSIC_BOOKMARK_CACHE[key] = list;
        return list;
    }

    function isBookmarkedEntity(key, entityId) {
        if (!key || !entityId) return false;

        return getBookmarkedEntities(key).some(function (item) {
            return item && item.id === entityId;
        });
    }

    function saveBookmarkedEntity(key, entity) {
        if (!key || !entity || !entity.id) return false;

        var list = getBookmarkedEntities(key).filter(function (item) {
            return item && item.id !== entity.id;
        });

        list.unshift(entity);
        Lampa.Storage.set(key, list.slice(0, 100));
        delete MUSIC_BOOKMARK_CACHE[key];
        return true;
    }

    function removeBookmarkedEntity(key, entityId) {
        if (!key || !entityId) return false;

        var list = getBookmarkedEntities(key);
        var next = list.filter(function (item) {
            return item && item.id !== entityId;
        });

        Lampa.Storage.set(key, next);
        delete MUSIC_BOOKMARK_CACHE[key];
        return next.length !== list.length;
    }

    function toggleBookmarkedEntity(key, entity) {
        if (!key || !entity || !entity.id) return false;

        if (isBookmarkedEntity(key, entity.id)) {
            removeBookmarkedEntity(key, entity.id);
            return false;
        }

        saveBookmarkedEntity(key, entity);
        return true;
    }

    function removeHistoryTrack(trackId, done, options) {
        options = options || {};
        requestPost(MUSIC.endpoints.removeHistory, 'id=' + encodeURIComponent(trackId || ''), function (json) {
            var removed = !!(json && json.removed);
            if (removed) {
                removeHomeCacheEntry('recently_played', trackId);
                updateHomeSectionMetaFromCache('recently_played');
                if (!options.skipEvent)
                    emitRecentChanged('recently_played', trackId);
            }
            done(removed);
        }, function () {
            done(false);
        });
    }

    function normalizeText(value) {
        return String(value || '')
            .toLowerCase()
            .replace(/[^a-z0-9а-яёіїєґ]+/gi, ' ')
            .replace(/\s+/g, ' ')
            .trim();
    }

    function scoreAlbumCandidate(source, candidate) {
        if (!candidate) return -1;

        var score = 0;
        var sourceTitle = normalizeText(source.title);
        var sourceArtist = normalizeText(source.artist_name);
        var candidateTitle = normalizeText(candidate.title);
        var candidateArtist = normalizeText(candidate.artist_name);

        if (sourceTitle && candidateTitle === sourceTitle) score += 60;
        else if (sourceTitle && candidateTitle.indexOf(sourceTitle) !== -1) score += 30;

        if (sourceArtist && candidateArtist === sourceArtist) score += 50;
        else if (sourceArtist && candidateArtist.indexOf(sourceArtist) !== -1) score += 20;

        if (source.year && candidate.year && String(source.year) === String(candidate.year)) score += 15;
        else if (source.date && candidate.date && String(candidate.date).slice(0, 4) === String(source.date).slice(0, 4)) score += 10;

        if (candidateTitle && /deluxe|edition|remaster|anniversary|karaoke|tribute|cover/i.test(candidateTitle))
            score -= 25;

        return score;
    }

    // Apple-чарты полны изданий вида «ANTI (Deluxe)» / «Dangerous: The Double
    // Album» — по полному названию MusicBrainz-поиск не находит ничего,
    // по базовому — находит (проверено живьём, июль 2026)
    function stripAlbumEditionSuffix(query) {
        return String(query || '')
            .replace(/\s*\([^)]*(?:deluxe|edition|version|anniversary|expanded|remaster)[^)]*\)\s*$/i, '')
            .replace(/\s*[-–—:]\s*(?:the\s+)?(?:deluxe|anthology|double\s+album|expanded|complete)(?:\s+(?:edition|version|album))?\s*$/i, '')
            .replace(/\s*[-–—]\s*EP\s*$/i, '')
            .trim();
    }

    function resolveAlbum(album, done, fail) {
        if (!album || !album.lookup_query) {
            done(album);
            return;
        }

        function fallbackToDirectAlbum() {
            if (album && album.id) {
                done(album);
                return;
            }

            if (fail) fail();
        }

        // у /music/search бюджет ответа 2s: холодный запрос отдаёт пустоту,
        // а фоновый прогрев дозаполняет кэш — поэтому пустой ответ повторяем
        // через паузу, и только потом пробуем запрос без edition-суффикса.
        // Если у исходного трека уже был album_id, он остаётся прямым fallback:
        // у MusicBrainz track.album_id может быть release id, а lookup_query
        // иногда не успевает/не находит release-group по артисту и названию.
        var fullQuery = String(album.lookup_query).trim();
        var strippedQuery = stripAlbumEditionSuffix(fullQuery);
        var attempts = [
            { query: fullQuery, delay: 0 },
            { query: fullQuery, delay: 2500 }
        ];

        if (strippedQuery && strippedQuery !== fullQuery) {
            attempts.push({ query: strippedQuery, delay: 0 });
            attempts.push({ query: strippedQuery, delay: 2500 });
        }

        function runAttempt(step) {
            if (step >= attempts.length) {
                fallbackToDirectAlbum();
                return;
            }

            var attempt = attempts[step];

            function fire() {
                request(MUSIC.endpoints.search + '?q=' + encodeURIComponent(attempt.query), function (json) {
                    var parsed = parseJson(json) || {};
                    var albums = Array.isArray(parsed.albums) ? parsed.albums : [];
                    var best = null;
                    var bestScore = -1;

                    albums.forEach(function (candidate) {
                        var score = scoreAlbumCandidate(album, candidate);
                        if (score > bestScore) {
                            bestScore = score;
                            best = candidate;
                        }
                    });

                    if (best && bestScore > 0) {
                        done(best);
                        return;
                    }

                    // метадата ещё прогревается (2s-бюджет сервера) —
                    // повторяем тот же запрос, а не следующий по каскаду
                    if (parsed.metadata_pending && (attempt.pendingRetries || 0) < 2) {
                        attempt.pendingRetries = (attempt.pendingRetries || 0) + 1;
                        setTimeout(fire, 3000);
                        return;
                    }

                    runAttempt(step + 1);
                }, function () {
                    runAttempt(step + 1);
                });
            }

            if (attempt.delay) setTimeout(fire, attempt.delay);
            else fire();
        }

        runAttempt(0);
    }

    function openSearchInput(initialValue, options) {
        var value = typeof initialValue === 'string' ? initialValue : '';
        Lampa.Input.edit({
            value: value,
            title: 'Поиск музыки',
            free: true,
            nosave: false,
            nomic: true
        }, function (newValue) {
            newValue = String(newValue || '').trim();

            if (!newValue) {
                if (options && options.onCancel) options.onCancel();
                return;
            }

            if (options && options.ignoreSameValue && newValue === value.trim()) {
                if (options.onCancel) options.onCancel();
                return;
            }

            openSearchQuery(newValue);
        });
    }

    // ===== AUTH =====

    function loadAuthStates(done, fail) {
        request(MUSIC.endpoints.authState, function (json) {
            var parsed = parseJson(json) || [];
            done(Array.isArray(parsed) ? parsed : []);
        }, fail || function () {
            done([]);
        });
    }

    function getYandexAuthState(states) {
        var list = Array.isArray(states) ? states : [];

        for (var i = 0; i < list.length; i++) {
            if (list[i] && list[i].provider_id === 'yandexmusic') return list[i];
        }

        return {
            provider_id: 'yandexmusic',
            provider_name: 'Yandex Music',
            authenticated: false,
            token_ready: false,
            web_ready: false,
            message: 'Token and cookies are required for full functionality.'
        };
    }

    function updateAuthStatus(target, state) {
        if (!target) return;
        target.text(state && state.message ? state.message : 'Yandex auth не задана');
    }

    function refreshAuthStatus(target, callback) {
        loadAuthStates(function (states) {
            var state = getYandexAuthState(states);
            updateAuthStatus(target, state);
            if (callback) callback(state);
        }, function () {
            updateAuthStatus(target, null);
            if (callback) callback(null);
        });
    }

    function saveAuthPayload(payload, authStatus, onDone, restoreContext) {
        requestPost(MUSIC.endpoints.authSave, 'provider=yandexmusic&payload=' + encodeURIComponent(payload || ''), function (json) {
            if (!json || json.saved !== true) {
                Lampa.Noty.show('Не удалось сохранить авторизацию.');
                restoreMusicFocusContext(restoreContext);
                return;
            }

            refreshAuthStatus(authStatus, function (state) {
                Lampa.Noty.show(state && state.message ? state.message : 'Авторизация сохранена.');
                if (onDone) onDone(state);
                restoreMusicFocusContext(restoreContext, 120);
            });
        }, function () {
            Lampa.Noty.show('Не удалось сохранить авторизацию.');
            restoreMusicFocusContext(restoreContext);
        });
    }

    function logoutAuth(authStatus, restoreContext) {
        requestPost(MUSIC.endpoints.authLogout, 'provider=yandexmusic', function () {
            refreshAuthStatus(authStatus, function () {
                Lampa.Noty.show('Авторизация очищена.');
                restoreMusicFocusContext(restoreContext, 120);
            });
        }, function () {
            Lampa.Noty.show('Не удалось очистить авторизацию.');
            restoreMusicFocusContext(restoreContext);
        });
    }

    function openAuthMenu(authStatus) {
        var restoreContext = captureMusicFocusContext('content');

        loadAuthStates(function (states) {
            var state = getYandexAuthState(states);
            var items = [
                { title: state.message || 'Состояние неизвестно', action: 'status' },
                { title: 'Ввести OAuth token', action: 'token' },
                { title: 'Вставить web cookies', action: 'cookies' },
                { title: 'Очистить авторизацию', action: 'logout' }
            ];

            Lampa.Select.show({
                title: 'Yandex Music',
                items: items,
                onBack: function () {
                    restoreMusicFocusContext(restoreContext);
                },
                onSelect: function (selected) {
                    if (!selected || !selected.action || selected.action === 'status') {
                        Lampa.Noty.show(state.message || 'Состояние неизвестно.');
                        restoreMusicFocusContext(restoreContext, 120);
                        return;
                    }

                    if (selected.action === 'token') {
                        Lampa.Input.edit({
                            value: '',
                            title: 'Yandex OAuth token',
                            free: true,
                            nosave: true,
                            nomic: true
                        }, function (value) {
                            value = (value || '').trim();
                            if (!value) {
                                restoreMusicFocusContext(restoreContext);
                                return;
                            }
                            saveAuthPayload('ym_token=' + encodeURIComponent(value), authStatus, null, restoreContext);
                        });
                        return;
                    }

                    if (selected.action === 'cookies') {
                        Lampa.Input.edit({
                            value: '',
                            title: 'Yandex cookies',
                            free: true,
                            nosave: true,
                            nomic: true
                        }, function (value) {
                            value = (value || '').trim();
                            if (!value) {
                                restoreMusicFocusContext(restoreContext);
                                return;
                            }
                            saveAuthPayload(value, authStatus, null, restoreContext);
                        });
                        return;
                    }

                    if (selected.action === 'logout') {
                        logoutAuth(authStatus, restoreContext);
                    }
                }
            });
        }, function () {
            Lampa.Noty.show('Не удалось получить состояние авторизации.');
            restoreMusicFocusContext(restoreContext);
        });
    }

    // ===== ENTITY NAVIGATION =====

    function openArtistCatalog(artist) {
        if (!artist || !artist.id || /^related:/i.test(String(artist.id)))
            return openSearchQuery(artist && artist.name ? artist.name : '');

        var provider = getEntityProviderId(artist);

        saveRecentEntity(MUSIC.storage.recent_artists, artist);
        Lampa.Activity.push({
            title: artist.name || 'Артист',
            component: 'lampac_music_artist',
            id: artist.id,
            provider: provider,
            page: 1,
            noinfo: true
        });
    }

    function getActiveMusicQuery() {
        try {
            var active = Lampa.Activity.active();
            if (!active || active.component !== 'lampac_music_home') return '';
            return String(active.query || '').trim();
        } catch (e) {
            return '';
        }
    }

    function openArtist(artist) {
        if (!artist) return;

        saveRecentEntity(MUSIC.storage.recent_artists, artist);

        var artistName = String(artist.name || '').trim();
        var activeQuery = getActiveMusicQuery();

        if (isDirectArtistEntity(artist)) {
            openArtistCatalog(artist);
            return;
        }

        if (artistName && normalizeText(activeQuery) === normalizeText(artistName)) {
            openArtistCatalog(artist);
            return;
        }

        openSearchQuery(artistName);
    }

    // фоллбек «Открыть альбом» для треков без album-меты (SoundCloud-аплоады,
    // часть YouTube): ищем сам ТРЕК в поиске — MusicBrainz отдаёт треки с
    // album_id — и открываем альбом найденной записи. Гейт по совпадению
    // артиста и названия: лучше честный отказ, чем чужой альбом
    function splitTrackArtistTitle(value) {
        value = String(value || '').trim();
        if (!value) return null;

        var match = value.match(/^(.{2,120}?)\s*[-–—]\s+(.{2,180})$/)
            || value.match(/^(.{2,120}?)\s+[-–—]\s*(.{2,180})$/);
        if (!match) return null;

        var artist = match[1].trim();
        var title = match[2].trim();
        if (!artist || !title) return null;

        return { artist: artist, title: title };
    }

    function normalizeAlbumLookupUnicode(value) {
        value = String(value || '');

        try {
            return value.normalize('NFC');
        } catch (e) {
            return value;
        }
    }

    function addAlbumLookupQuery(list, seen, artist, title) {
        var query = normalizeAlbumLookupUnicode([artist, title].filter(Boolean).join(' ').trim());

        function addVariant(value) {
            value = normalizeAlbumLookupUnicode(value);
            var key = normalizeText(value);
            if (!key || seen[key]) return;

            seen[key] = true;
            list.push(value);
        }

        addVariant(query);
        addVariant(query.replace(/эй/gi, 'ей'));
    }

    function stripAlbumLookupTitleNoise(value) {
        return normalizeAlbumLookupUnicode(value)
            .replace(/&amp;/gi, '&')
            .replace(/\s*[\[(][^\])]*(?:feat\.?|ft\.?|featuring|при\s*уч)[^\])]*[\])]\s*/gi, ' ')
            .replace(/\s*[\[(][^\])]*(?:official|audio|video|lyrics?|lyric|clip|mv|hd|4k|remix|sped|slowed|nightcore|bass|boosted|screwed|reverb|amateur|премьера|текст|клип)[^\])]*[\])]\s*/gi, ' ')
            .replace(/\s+[\/|]\s+.*$/i, ' ')
            .replace(/\s+#[^\s]+/g, ' ')
            .replace(/\s+/g, ' ')
            .trim();
    }

    function normalizeAlbumLookupText(value) {
        return normalizeText(normalizeAlbumLookupUnicode(stripAlbumLookupTitleNoise(value)))
            .replace(/эи/g, 'еи')
            .replace(/эй/g, 'ей')
            .replace(/\s+/g, ' ')
            .trim();
    }

    function albumLookupTokens(value) {
        var stop = {
            feat: true,
            ft: true,
            featuring: true,
            official: true,
            audio: true,
            video: true,
            lyrics: true,
            lyric: true,
            clip: true,
            remix: true
        };

        return normalizeAlbumLookupText(value)
            .split(' ')
            .filter(function (token) {
                return token && token.length > 1 && !stop[token];
            });
    }

    function countTokenOverlap(left, right) {
        var seen = {};
        var matched = {};
        var count = 0;

        left.forEach(function (token) {
            seen[token] = true;
        });

        right.forEach(function (token) {
            if (seen[token] && !matched[token]) {
                matched[token] = true;
                count++;
            }
        });

        return count;
    }

    function pushAlbumLookupExpectation(list, seen, artist, title, titleOnly) {
        title = stripAlbumLookupTitleNoise(title);
        if (!title) return;

        var titleTokens = albumLookupTokens(title);
        if (!titleTokens.length) return;

        artist = String(artist || '').trim();
        var artistTokens = albumLookupTokens(artist);

        if (!artistTokens.length && !titleOnly) return;
        if (titleOnly && titleTokens.length < 3) return;

        var key = normalizeAlbumLookupText(artist) + '|' + normalizeAlbumLookupText(title) + '|' + (titleOnly ? '1' : '0');
        if (seen[key]) return;

        seen[key] = true;
        list.push({
            artist: artist,
            title: title,
            titleOnly: !!titleOnly,
            artistTokens: artistTokens,
            titleTokens: titleTokens,
            normalizedArtist: normalizeAlbumLookupText(artist),
            normalizedTitle: normalizeAlbumLookupText(title)
        });
    }

    function buildAlbumLookupExpectations(track, artist, title) {
        var list = [];
        var seen = {};
        var cleanTitle = stripAlbumLookupTitleNoise(title);
        var split = splitTrackArtistTitle(title);

        pushAlbumLookupExpectation(list, seen, artist, cleanTitle, false);

        if (split) {
            pushAlbumLookupExpectation(list, seen, split.artist, split.title, false);
            if (artist) pushAlbumLookupExpectation(list, seen, artist, split.title, false);
        }

        // Случай без явного дефиса, но с артистом внутри названия:
        // "Eminem Till i collapse", "Мияги&Эндшпиль Люби меня".
        // Это слабый вариант: он не открывает альбом сам по себе, если кандидат
        // не совпал хотя бы частью artist-токенов из строки.
        pushAlbumLookupExpectation(list, seen, null, cleanTitle, true);

        return list;
    }

    function scoreAlbumLookupCandidateForExpectation(candidate, expectation) {
        var cTitle = normalizeAlbumLookupText(candidate.title);
        var cArtist = normalizeAlbumLookupText(candidate.artist_name);
        var cTitleTokens = albumLookupTokens(candidate.title);
        var cArtistTokens = albumLookupTokens(candidate.artist_name);
        var titleExact = cTitle && expectation.normalizedTitle
            && (cTitle === expectation.normalizedTitle
                || cTitle.indexOf(expectation.normalizedTitle) !== -1
                || (expectation.normalizedTitle.indexOf(cTitle) !== -1 && cTitleTokens.length >= 2));
        var titleOverlap = countTokenOverlap(expectation.titleTokens, cTitleTokens);
        var artistExact = !!expectation.normalizedArtist && !!cArtist
            && (cArtist.indexOf(expectation.normalizedArtist) !== -1
                || expectation.normalizedArtist.indexOf(cArtist) !== -1);
        var artistOverlap = countTokenOverlap(expectation.artistTokens, cArtistTokens);
        var titleMentionsArtist = countTokenOverlap(expectation.titleTokens, cArtistTokens);
        var titleOk = titleExact || (titleOverlap >= Math.min(2, expectation.titleTokens.length));
        var artistOk = false;
        var score = 0;

        if (!titleOk) return 0;

        if (expectation.titleOnly) {
            // Не даём title-only варианту открыть чужой одноимённый трек:
            // в строке должен быть хотя бы один токен артиста кандидата.
            artistOk = titleMentionsArtist > 0;
        } else {
            artistOk = !expectation.artistTokens.length || artistExact || artistOverlap > 0;
        }

        if (!artistOk) return 0;

        if (titleExact) score += 40;
        score += Math.min(titleOverlap, 4) * 12;
        if (artistExact) score += 28;
        score += Math.min(artistOverlap, 3) * 10;
        if (titleMentionsArtist) score += Math.min(titleMentionsArtist, 2) * 8;
        if (expectation.titleOnly) score -= 10;

        return score;
    }

    function scoreAlbumLookupCandidate(candidate, expectations) {
        if (!candidate || !candidate.album_id) return 0;

        var best = 0;
        for (var i = 0; i < expectations.length; i++)
            best = Math.max(best, scoreAlbumLookupCandidateForExpectation(candidate, expectations[i]));

        return best;
    }

    function resolveAlbumViaTrackSearch(track, done, fail) {
        var artist = getTrackArtistSearchQuery(track) || String(track.artist_name || '').trim();
        var title = String(track.title || '').trim();

        if (!title) {
            if (fail) fail();
            return;
        }

        var expectations = buildAlbumLookupExpectations(track, artist, title);
        var queryList = [];
        var querySeen = {};

        expectations.forEach(function (expectation) {
            if (!expectation.titleOnly) {
                addAlbumLookupQuery(queryList, querySeen, expectation.artist, expectation.title);
                addAlbumLookupQuery(queryList, querySeen, null, expectation.title);
            } else {
                addAlbumLookupQuery(queryList, querySeen, null, expectation.title);
            }
        });

        if (!queryList.length || !expectations.length) {
            if (fail) fail();
            return;
        }

        // холодный поиск отдаёт пустоту из-за 2s-бюджета сервера, а прогрев
        // треков (MusicBrainz, rate-limit) занимает 5-10s — поэтому ретраим
        // ТОТ ЖЕ запрос по флагу metadata_pending, а не следующий по списку;
        // слепой повтор через 2.5s приходил раньше конца прогрева
        var attempts = [];

        queryList.forEach(function (query) {
            attempts.push({ query: query, delay: 0, pendingRetries: 0 });
        });

        function runAttempt(step) {
            if (step >= attempts.length) {
                if (fail) fail();
                return;
            }

            function fire() {
                var attempt = attempts[step];

                request(MUSIC.endpoints.search + '?q=' + encodeURIComponent(attempt.query), function (json) {
                    var parsed = parseJson(json) || {};
                    var candidates = Array.isArray(parsed.tracks) ? parsed.tracks : [];
                    var best = null;
                    var bestScore = 0;

                    for (var i = 0; i < candidates.length; i++) {
                        var score = scoreAlbumLookupCandidate(candidates[i], expectations);
                        if (score > bestScore) {
                            best = candidates[i];
                            bestScore = score;
                        }
                    }

                    if (best) {
                        done(best);
                        return;
                    }

                    // сервер прямо сообщает, что метадата ещё прогревается —
                    // повторяем ТОТ ЖЕ запрос, следующий по каскаду бессмыслен
                    if (parsed.metadata_pending && attempt.pendingRetries < 2) {
                        attempt.pendingRetries++;
                        setTimeout(fire, 3000);
                        return;
                    }

                    runAttempt(step + 1);
                }, function () {
                    runAttempt(step + 1);
                });
            }

            if (attempts[step].delay) setTimeout(fire, attempts[step].delay);
            else fire();
        }

        runAttempt(0);
    }

    function openAlbumFromTrackSearch(track, restoreContext) {
        startMusicPlaybackLoading('Открываю альбом');

        resolveAlbumViaTrackSearch(track, function (found) {
            stopMusicPlaybackLoading();
            openAlbum({
                id: found.album_id,
                title: found.album_title || 'Альбом',
                artist_name: found.artist_name || '',
                images: Array.isArray(found.images) ? found.images : []
            }, {
                onFail: function () {
                    restoreMusicFocusContext(restoreContext);
                }
            });
        }, function () {
            stopMusicPlaybackLoading();
            Lampa.Noty.show('Не удалось определить альбом трека.');
            restoreMusicFocusContext(restoreContext);
        });
    }

    function openAlbum(album, options) {
        options = options || {};

        // резолв с retry может занять несколько секунд — показываем лоадер
        if (album && album.lookup_query) startMusicPlaybackLoading('Открываю альбом');

        resolveAlbum(album, function (resolvedAlbum) {
            stopMusicPlaybackLoading();
            if (!resolvedAlbum || !resolvedAlbum.id) {
                Lampa.Noty.show('Не удалось подобрать альбом.');
                if (options.onFail) options.onFail();
                return;
            }

            var provider = null;
            if (resolvedAlbum && Array.isArray(resolvedAlbum.provider_refs) && resolvedAlbum.provider_refs.length)
                provider = resolvedAlbum.provider_refs[0] && resolvedAlbum.provider_refs[0].provider ? resolvedAlbum.provider_refs[0].provider : null;

            saveRecentEntity(MUSIC.storage.recent_albums, resolvedAlbum);
            Lampa.Activity.push({
                title: resolvedAlbum.title || 'Альбом',
                component: 'lampac_music_album',
                id: resolvedAlbum.id,
                provider: provider,
                page: 1,
                noinfo: true
            });
        }, function () {
            stopMusicPlaybackLoading();
            Lampa.Noty.show('Не удалось подобрать альбом.');
            if (options.onFail) options.onFail();
        });
    }

    // ===== QUEUE / IOS PLAYBACK =====

    // --- очередь: треки и текущий индекс ---

    function queueTracks() {
        return Array.isArray(MUSIC_QUEUE.tracks) ? MUSIC_QUEUE.tracks : [];
    }

    function queueCurrentIndex() {
        var tracks = queueTracks();
        if (!tracks.length) return -1;

        if (MUSIC_QUEUE.currentTrackId) {
            for (var i = 0; i < tracks.length; i++) {
                if (tracks[i] && tracks[i].id === MUSIC_QUEUE.currentTrackId) {
                    MUSIC_QUEUE.currentIndex = i;
                    return i;
                }
            }
        }

        return Math.max(0, Math.min(MUSIC_QUEUE.currentIndex || 0, tracks.length - 1));
    }

    function queueCurrentTrack() {
        var index = queueCurrentIndex();
        var tracks = queueTracks();
        return index >= 0 && tracks[index] ? tracks[index] : null;
    }

    function rememberQueue(tracks, startIndex) {
        MUSIC_QUEUE.tracks = (tracks || []).slice();
        MUSIC_QUEUE.currentIndex = typeof startIndex === 'number' ? startIndex : 0;
        MUSIC_QUEUE.currentTrackId = MUSIC_QUEUE.tracks[MUSIC_QUEUE.currentIndex]
            ? MUSIC_QUEUE.tracks[MUSIC_QUEUE.currentIndex].id
            : null;

        MUSIC_QUEUE_RESTORE.available = false;
        scheduleQueueSnapshotSave(true);
    }

    function updateQueueCurrent(trackId) {
        if (!trackId) return;
        MUSIC_QUEUE.currentTrackId = trackId;

        var tracks = queueTracks();
        for (var i = 0; i < tracks.length; i++) {
            if (tracks[i] && tracks[i].id === trackId) {
                MUSIC_QUEUE.currentIndex = i;
                if (MUSIC_QUEUE_RESTORE.available && MUSIC_QUEUE_RESTORE.trackId !== trackId)
                    MUSIC_QUEUE_RESTORE.position = 0;
                MUSIC_QUEUE_RESTORE.trackId = trackId;
                scheduleQueueSnapshotSave(true);
                return;
            }
        }

        scheduleQueueSnapshotSave(true);
    }

    // --- снапшот очереди / restore после перезапуска клиента ---

    function compactQueueProviderRefs(refs) {
        if (!Array.isArray(refs)) return [];

        return refs.map(function (item) {
            if (!item) return null;

            return {
                provider: item.provider || '',
                external_id: item.external_id || '',
                url: item.url || ''
            };
        }).filter(function (item) {
            return !!(item && (item.provider || item.external_id || item.url));
        }).slice(0, 6);
    }

    function compactQueueImages(images) {
        if (!Array.isArray(images)) return [];

        return images.map(function (item) {
            if (!item) return null;

            return {
                url: item.url || '',
                width: item.width || 0,
                height: item.height || 0
            };
        }).filter(function (item) {
            return !!(item && item.url);
        }).slice(0, 4);
    }

    function compactQueueTrack(track) {
        if (!track || !track.id) return null;

        var copy = {
            id: track.id,
            title: track.title || '',
            artist_name: track.artist_name || '',
            album_title: track.album_title || '',
            duration_ms: track.duration_ms || 0,
            date: track.date || '',
            provider: track.provider || '',
            image: track.image || ''
        };
        var images = compactQueueImages(track.images);
        var providerRefs = compactQueueProviderRefs(track.provider_refs);

        if (images.length) copy.images = images;
        if (providerRefs.length) copy.provider_refs = providerRefs;
        if (track.auto_radio) copy.auto_radio = true;

        return copy;
    }

    function isRadioAutoplayEnabled() {
        return Lampa.Storage.get(MUSIC.storage.radio_autoplay_enabled, false) === true;
    }

    function setRadioAutoplayEnabled(enabled) {
        Lampa.Storage.set(MUSIC.storage.radio_autoplay_enabled, enabled === true);

        if (!enabled) {
            MUSIC_RADIO_STATE.pending = false;
            MUSIC_RADIO_STATE.lastGeneration = '';
        }
    }

    function normalizeRadioDedupeText(value) {
        return normalizeText(value)
            .replace(/\b(feat|ft|featuring|official|audio|video|lyrics|lyric|clip|mv|hd|4k)\b/g, ' ')
            .replace(/\s+/g, ' ')
            .trim();
    }

    function radioTrackDedupeKey(track) {
        if (!track) return '';

        var title = normalizeRadioDedupeText(track.title || '');
        var artist = normalizeRadioDedupeText(track.artist_name || '');

        return title ? (artist + '|' + title) : '';
    }

    function compactRadioTrack(track) {
        var compact = compactQueueTrack(track);
        if (!compact) return null;
        if (track && track.auto_radio) compact.auto_radio = true;
        return compact;
    }

    function radioQueueGenerationKey(tracks, currentIndex) {
        return [
            MUSIC_QUEUE_BLOB_STATE.snapshotId || '',
            currentIndex,
            (tracks || []).map(function (track) {
                return track && track.id ? track.id : '';
            }).join('|')
        ].join('::');
    }

    function radioSeedTracks(tracks, currentIndex) {
        var result = [];
        var seen = {};

        for (var i = currentIndex; i >= 0 && result.length < 5; i--) {
            var track = tracks[i];
            if (!track || !track.id) continue;

            var artistKey = normalizeRadioDedupeText(track.artist_name || '');
            var key = artistKey || track.id;
            if (seen[key]) continue;
            seen[key] = true;
            result.push(compactRadioTrack(track));
        }

        return result.filter(Boolean);
    }

    function radioExcludeTracks(tracks) {
        return (tracks || []).map(compactRadioTrack).filter(Boolean);
    }

    function shouldTriggerRadioAutoplay(origin) {
        if (!isRadioAutoplayEnabled()) return false;
        if (getPlaybackMode() !== 'audio') return false;
        if (getStandaloneIosRepeatMode() === 'all') return false;

        var player = currentExternalPlayer();
        var managedPlayer = player === 'ios' || player === 'inner' || player === 'lampa';
        if (!managedPlayer) return false;

        var tracks = queueTracks();
        var currentIndex = queueCurrentIndex();
        if (!tracks.length || currentIndex < 0) return false;

        var remaining = tracks.length - currentIndex - 1;

        // при shuffle физический индекс текущего трека случаен — остаток
        // очереди считаем по shuffle-порядку, иначе радио дольёт треки,
        // когда пол-очереди ещё не слушано
        if (shouldUseStandaloneIosAudio() && isStandaloneIosShuffle()) {
            var order = standaloneIosOrder();
            var orderPos = order ? order.indexOf(currentIndex) : -1;
            if (orderPos > -1) remaining = order.length - orderPos - 1;
        }

        if (remaining > 1) return false;

        if (shouldUseStandaloneIosAudio())
            return isStandaloneIosAudioActive();

        var data = activePlayerData();
        if (!data || !data.from_music_cluster) return false;
        if (!usesInternalPlaybackFlow()) return false;

        return canReuseActiveQueue(tracks);
    }

    function appendRadioTracksToManagedQueue(radioTracks) {
        var currentTracks = queueTracks();
        var currentIndex = queueCurrentIndex();
        if (!currentTracks.length || currentIndex < 0) return 0;

        var seenIds = {};
        var seenKeys = {};

        currentTracks.forEach(function (track) {
            if (track && track.id) seenIds[track.id] = true;
            var key = radioTrackDedupeKey(track);
            if (key) seenKeys[key] = true;
        });

        var additions = [];
        (radioTracks || []).forEach(function (track) {
            if (!track || !track.id || seenIds[track.id]) return;

            var key = radioTrackDedupeKey(track);
            if (key && seenKeys[key]) return;

            track.auto_radio = true;
            seenIds[track.id] = true;
            if (key) seenKeys[key] = true;
            additions.push(track);
        });

        if (!additions.length) return 0;

        if (shouldUseStandaloneIosAudio()) {
            if (!isStandaloneIosAudioActive()) return 0;

            additions.forEach(function (track) {
                MUSIC_IOS_AUDIO.tracks.push(track);
                MUSIC_IOS_AUDIO.playlist.push(buildPlayback(track));
            });

            MUSIC_IOS_AUDIO.shuffleOrder = null;
            MUSIC_QUEUE.tracks = MUSIC_IOS_AUDIO.tracks.slice();
            MUSIC_QUEUE.currentIndex = MUSIC_IOS_AUDIO.currentIndex;
            MUSIC_QUEUE.currentTrackId = MUSIC_IOS_AUDIO.tracks[MUSIC_IOS_AUDIO.currentIndex]
                ? MUSIC_IOS_AUDIO.tracks[MUSIC_IOS_AUDIO.currentIndex].id
                : MUSIC_QUEUE.currentTrackId;
        } else {
            var playlist = activePlaylist();
            if (!playlist.length || !canReuseActiveQueue(currentTracks)) return 0;

            additions.forEach(function (track) {
                currentTracks.push(track);
                playlist.push(buildPlayback(track));
            });

            MUSIC_QUEUE.tracks = currentTracks.slice();
            MUSIC_QUEUE.currentIndex = currentIndex;
            MUSIC_QUEUE.currentTrackId = currentTracks[currentIndex] ? currentTracks[currentIndex].id : MUSIC_QUEUE.currentTrackId;

            try {
                Lampa.Player.playlist(playlist);
            } catch (e) {}

            syncMusicMediaSession(activePlayerData());
        }

        scheduleQueueSnapshotSave(true);
        updateStandaloneIosPlayerBar();
        if (MUSIC_IOS_FULL_PLAYER_OPEN) updateStandaloneIosFullPlayer();

        return additions.length;
    }

    // «Радио от трека»: сид = выбранный трек, ответ радио становится НОВОЙ
    // очередью — сид первым (нажал радио от песни → она и стартует, дальше
    // волна). При любой неудаче текущая очередь не трогается
    function startRadioFromTrack(track, restoreContext) {
        if (!track || !track.id) return;

        startMusicPlaybackLoading('Подбираю волну');

        var seed = compactRadioTrack(track);
        var payload = 'seeds=' + encodeURIComponent(JSON.stringify(seed ? [seed] : []))
            + '&exclude=' + encodeURIComponent(JSON.stringify([]))
            + '&limit=20';

        requestPost(MUSIC.endpoints.radio, payload, function (json) {
            stopMusicPlaybackLoading();

            var radioTracks = json && json.available && Array.isArray(json.tracks) ? json.tracks : [];
            radioTracks = radioTracks.filter(function (item) {
                return item && item.id && item.id !== track.id;
            });

            if (!radioTracks.length) {
                Lampa.Noty.show('Не удалось подобрать треки.');
                restoreMusicFocusContext(restoreContext);
                return;
            }

            playTrack(track, [track].concat(radioTracks), 0, { forceFresh: true });
        }, function () {
            stopMusicPlaybackLoading();
            Lampa.Noty.show('Не удалось подобрать треки.');
            restoreMusicFocusContext(restoreContext);
        });
    }

    function maybeRequestRadioAutoplay(origin) {
        if (!shouldTriggerRadioAutoplay(origin)) return;

        var tracks = queueTracks();
        var currentIndex = queueCurrentIndex();
        var generation = radioQueueGenerationKey(tracks, currentIndex);

        if (!generation || MUSIC_RADIO_STATE.pending || MUSIC_RADIO_STATE.lastGeneration === generation)
            return;

        MUSIC_RADIO_STATE.pending = true;
        MUSIC_RADIO_STATE.lastGeneration = generation;
        MUSIC_RADIO_STATE.lastRequestAt = Date.now();

        var seeds = radioSeedTracks(tracks, currentIndex);
        var exclude = radioExcludeTracks(tracks);

        if (!seeds.length) {
            MUSIC_RADIO_STATE.pending = false;
            return;
        }

        var payload = 'seeds=' + encodeURIComponent(JSON.stringify(seeds))
            + '&exclude=' + encodeURIComponent(JSON.stringify(exclude))
            + '&limit=20';

        requestPost(MUSIC.endpoints.radio, payload, function (json) {
            MUSIC_RADIO_STATE.pending = false;

            if (!json || !json.available || !Array.isArray(json.tracks) || !json.tracks.length)
                return;

            var added = appendRadioTracksToManagedQueue(json.tracks);
            if (added > 0)
                Lampa.Noty.show('Радио добавило треки в очередь.');
        }, function () {
            MUSIC_RADIO_STATE.pending = false;
        });
    }

    function currentQueueSnapshotPosition() {
        var audio = MUSIC_IOS_AUDIO.audio;

        if (audio && isFinite(audio.currentTime) && audio.currentTime > 0)
            return Math.max(0, audio.currentTime);

        var data = activePlayerData();
        var media = data && data.from_music_cluster ? activeMusicMediaElement() : null;

        if (media && isFinite(media.currentTime) && media.currentTime > 0)
            return Math.max(0, media.currentTime);

        if (data && data.from_music_cluster && data.timeline && isFinite(data.timeline.time))
            return Math.max(0, Number(data.timeline.time || 0));

        return Math.max(0, Number(MUSIC_QUEUE_RESTORE.position || 0));
    }

    function saveQueueSnapshotNow() {
        if (MUSIC_QUEUE_RESTORE.timer) {
            clearTimeout(MUSIC_QUEUE_RESTORE.timer);
            MUSIC_QUEUE_RESTORE.timer = 0;
        }

        if (!isQueueRestoreEnabled()) return;

        var tracks = queueTracks();
        var currentIndex = queueCurrentIndex();

        if (!tracks.length || currentIndex < 0) return;

        var compact = [];
        var currentCompactIndex = -1;

        tracks.forEach(function (track, index) {
            var item = compactQueueTrack(track);
            if (!item) return;

            if (index === currentIndex || (MUSIC_QUEUE.currentTrackId && item.id === MUSIC_QUEUE.currentTrackId))
                currentCompactIndex = compact.length;

            compact.push(item);
        });

        if (!compact.length) return;
        if (currentCompactIndex < 0) currentCompactIndex = Math.max(0, Math.min(currentIndex, compact.length - 1));

        var offset = 0;
        var truncated = false;

        if (compact.length > MUSIC_QUEUE_SNAPSHOT_LIMIT) {
            truncated = true;
            offset = Math.max(0, Math.min(
                currentCompactIndex - Math.floor(MUSIC_QUEUE_SNAPSHOT_LIMIT / 3),
                compact.length - MUSIC_QUEUE_SNAPSHOT_LIMIT
            ));
            compact = compact.slice(offset, offset + MUSIC_QUEUE_SNAPSHOT_LIMIT);
            currentCompactIndex = Math.max(0, Math.min(compact.length - 1, currentCompactIndex - offset));
        }

        var currentTrack = compact[currentCompactIndex] || null;
        var position = currentQueueSnapshotPosition();
        var snapshotId = Date.now() + '-' + (++MUSIC_QUEUE_BLOB_STATE.seq);
        var now = Date.now();

        try {
            Lampa.Storage.set(MUSIC.storage.queue_blob_v2, {
                version: MUSIC_QUEUE_BLOB_VERSION,
                snapshotId: snapshotId,
                updatedAt: now,
                tracks: compact,
                currentIndex: currentCompactIndex,
                currentTrackId: currentTrack ? currentTrack.id : '',
                currentTime: position,
                repeatMode: getStandaloneIosRepeatMode(),
                shuffle: isStandaloneIosShuffle(),
                playbackMode: getPlaybackMode(),
                audioProvider: getAudioProviderId(),
                truncated: truncated,
                windowStart: offset,
                windowLength: compact.length
            });

            MUSIC_QUEUE_BLOB_STATE.snapshotId = snapshotId;
            MUSIC_QUEUE_BLOB_STATE.windowStart = offset;
            MUSIC_QUEUE_BLOB_STATE.windowLength = compact.length;
            MUSIC_QUEUE_BLOB_STATE.truncated = truncated;

            writeQueuePositionRecord(snapshotId, currentIndex, currentTrack ? currentTrack.id : '', position, now);

            // legacy-ключ v1 чистим один раз после первой успешной записи v2
            if (!MUSIC_QUEUE_BLOB_STATE.legacyCleared) {
                MUSIC_QUEUE_BLOB_STATE.legacyCleared = true;
                Lampa.Storage.set(MUSIC.storage.queue_snapshot, null);
            }
        } catch (e) {}
    }

    function writeQueuePositionRecord(snapshotId, currentIndex, trackId, position, now) {
        Lampa.Storage.set(MUSIC.storage.queue_position_v2, {
            snapshotId: snapshotId,
            trackId: trackId || '',
            currentIndex: currentIndex,
            currentTime: position,
            updatedAt: now
        });
    }

    // частый путь (тик timeupdate каждые 4s): пишем только позицию, blob не
    // пересобираем. Если блоба ещё нет или текущий трек подошёл к краю
    // усечённого окна — эскалация до полной записи
    function saveQueuePositionNow() {
        if (!isQueueRestoreEnabled()) return;

        var tracks = queueTracks();
        var currentIndex = queueCurrentIndex();
        if (!tracks.length || currentIndex < 0) return;

        var blob = MUSIC_QUEUE_BLOB_STATE;

        if (!blob.snapshotId) {
            saveQueueSnapshotNow();
            return;
        }

        if (blob.truncated) {
            var windowEnd = blob.windowStart + blob.windowLength;
            var outsideWindow = currentIndex < blob.windowStart || currentIndex >= windowEnd;
            // у края эскалируем только если окну есть куда сдвинуться в эту
            // сторону — иначе хвост длинной очереди переписывал бы blob каждый тик
            var nearMovableStart = currentIndex < blob.windowStart + MUSIC_QUEUE_WINDOW_EDGE_MARGIN && blob.windowStart > 0;
            var nearMovableEnd = currentIndex >= windowEnd - MUSIC_QUEUE_WINDOW_EDGE_MARGIN && windowEnd < tracks.length;

            if (outsideWindow || nearMovableStart || nearMovableEnd) {
                saveQueueSnapshotNow();
                return;
            }
        }

        bumpMusicHeatMetric('queuePositionSave');

        try {
            writeQueuePositionRecord(
                blob.snapshotId,
                currentIndex,
                MUSIC_QUEUE.currentTrackId || '',
                currentQueueSnapshotPosition(),
                Date.now()
            );
        } catch (e) {}
    }

    function scheduleQueueSnapshotSave(force) {
        if (!isQueueRestoreEnabled()) return;

        bumpMusicHeatMetric(force ? 'queueSnapshotForce' : 'queueSnapshotSchedule');

        if (force) {
            saveQueueSnapshotNow();
            return;
        }

        if (MUSIC_QUEUE_RESTORE.timer) {
            bumpMusicHeatMetric('queueSnapshotAlreadyPending');
            return;
        }

        MUSIC_QUEUE_RESTORE.timer = setTimeout(function () {
            MUSIC_QUEUE_RESTORE.timer = 0;
            saveQueuePositionNow();
        }, MUSIC_QUEUE_SNAPSHOT_SAVE_DELAY);
    }

    function clearQueueSnapshot() {
        if (MUSIC_QUEUE_RESTORE.timer) {
            clearTimeout(MUSIC_QUEUE_RESTORE.timer);
            MUSIC_QUEUE_RESTORE.timer = 0;
        }

        MUSIC_QUEUE_RESTORE.available = false;
        MUSIC_QUEUE_RESTORE.position = 0;
        MUSIC_QUEUE_RESTORE.updatedAt = 0;
        MUSIC_QUEUE_RESTORE.trackId = '';

        MUSIC_QUEUE_BLOB_STATE.snapshotId = '';
        MUSIC_QUEUE_BLOB_STATE.windowStart = 0;
        MUSIC_QUEUE_BLOB_STATE.windowLength = 0;
        MUSIC_QUEUE_BLOB_STATE.truncated = false;

        try {
            Lampa.Storage.set(MUSIC.storage.queue_snapshot, null);
            Lampa.Storage.set(MUSIC.storage.queue_blob_v2, null);
            Lampa.Storage.set(MUSIC.storage.queue_position_v2, null);
        } catch (e) {}
    }

    function hasRestoredQueueSnapshot() {
        return !!(MUSIC_QUEUE_RESTORE.available && queueTracks().length && queueCurrentIndex() >= 0);
    }

    function hasStandaloneIosRestoredQueue() {
        return shouldUseStandaloneIosAudio() && hasRestoredQueueSnapshot() && !isStandaloneIosAudioActive();
    }

    function selectRestoredQueueIndex(index) {
        var tracks = queueTracks();
        if (!tracks.length) return false;

        index = Math.max(0, Math.min(index, tracks.length - 1));
        MUSIC_QUEUE.currentIndex = index;
        MUSIC_QUEUE.currentTrackId = tracks[index] ? tracks[index].id : null;
        MUSIC_QUEUE_RESTORE.trackId = MUSIC_QUEUE.currentTrackId;
        MUSIC_QUEUE_RESTORE.position = 0;

        if (shouldUseStandaloneIosAudio()) {
            MUSIC_IOS_AUDIO.tracks = tracks.slice();
            MUSIC_IOS_AUDIO.playlist = tracks.map(function (track) { return buildPlayback(track); });
            MUSIC_IOS_AUDIO.currentIndex = index;
            MUSIC_IOS_AUDIO.shuffleOrder = null;
        }

        scheduleQueueSnapshotSave(true);
        updateStandaloneIosPlayerBar();
        return true;
    }

    function resumeRestoredQueueSnapshot() {
        var tracks = queueTracks();
        var index = queueCurrentIndex();
        var position = Math.max(0, Number(MUSIC_QUEUE_RESTORE.position || 0));

        if (!tracks.length || index < 0 || !tracks[index]) return false;

        MUSIC_QUEUE_RESTORE.available = false;
        MUSIC_QUEUE_RESTORE.position = 0;
        playTrack(tracks[index], tracks, index, {
            forceFresh: true,
            resumePosition: position
        });
        return true;
    }

    function readStoredQueueObject(key) {
        try {
            var value = Lampa.Storage.get(key, null);
            if (typeof value === 'string' && value)
                value = JSON.parse(value);
            return value && typeof value === 'object' ? value : null;
        } catch (e) {
            return null;
        }
    }

    // v2: blob (окно очереди) + отдельная запись позиции, связанные snapshotId.
    // Позиция от другого поколения блоба игнорируется — берём данные из блоба.
    // Возвращает объект в форме legacy-снапшота, чтобы код восстановления был общим
    function readQueueSnapshotV2() {
        var blob = readStoredQueueObject(MUSIC.storage.queue_blob_v2);
        if (!blob || blob.version !== MUSIC_QUEUE_BLOB_VERSION || !Array.isArray(blob.tracks)) return null;

        var position = readStoredQueueObject(MUSIC.storage.queue_position_v2);
        var positionValid = !!(position && position.snapshotId && position.snapshotId === blob.snapshotId);

        // свежесть определяет позиционная запись (blob пишется редко),
        // без валидной позиции — метка самого блоба
        var liveAt = positionValid ? Number(position.updatedAt || 0) : Number(blob.updatedAt || 0);

        return {
            tracks: blob.tracks,
            currentIndex: blob.currentIndex,
            currentTrackId: positionValid && position.trackId ? position.trackId : (blob.currentTrackId || ''),
            currentTime: positionValid ? Math.max(0, Number(position.currentTime || 0)) : Math.max(0, Number(blob.currentTime || 0)),
            updatedAt: liveAt,
            repeatMode: blob.repeatMode,
            shuffle: blob.shuffle
        };
    }

    function readLegacyQueueSnapshot() {
        var snapshot = readStoredQueueObject(MUSIC.storage.queue_snapshot);
        if (!snapshot || snapshot.version !== MUSIC_QUEUE_SNAPSHOT_VERSION || !Array.isArray(snapshot.tracks)) return null;
        return snapshot;
    }

    function restoreQueueSnapshot() {
        if (!isQueueRestoreEnabled()) return false;

        var snapshot = readQueueSnapshotV2() || readLegacyQueueSnapshot();

        if (!snapshot) return false;
        if (snapshot.updatedAt && Date.now() - snapshot.updatedAt > MUSIC_QUEUE_SNAPSHOT_TTL) {
            clearQueueSnapshot();
            return false;
        }

        var tracks = snapshot.tracks.map(compactQueueTrack).filter(Boolean);
        if (!tracks.length) {
            clearQueueSnapshot();
            return false;
        }

        var index = Math.max(0, Math.min(Number(snapshot.currentIndex || 0), tracks.length - 1));
        if (snapshot.currentTrackId) {
            for (var i = 0; i < tracks.length; i++) {
                if (tracks[i] && tracks[i].id === snapshot.currentTrackId) {
                    index = i;
                    break;
                }
            }
        }

        MUSIC_QUEUE.tracks = tracks;
        MUSIC_QUEUE.currentIndex = index;
        MUSIC_QUEUE.currentTrackId = tracks[index] ? tracks[index].id : null;
        MUSIC_QUEUE_RESTORE.available = true;
        MUSIC_QUEUE_RESTORE.position = Math.max(0, Number(snapshot.currentTime || 0));
        MUSIC_QUEUE_RESTORE.updatedAt = Number(snapshot.updatedAt || 0);
        MUSIC_QUEUE_RESTORE.trackId = MUSIC_QUEUE.currentTrackId || '';

        if (snapshot.repeatMode === 'off' || snapshot.repeatMode === 'all' || snapshot.repeatMode === 'one')
            Lampa.Storage.set(MUSIC.storage.repeat_mode, snapshot.repeatMode);
        if (typeof snapshot.shuffle === 'boolean')
            Lampa.Storage.set(MUSIC.storage.shuffle, snapshot.shuffle);

        if (shouldUseStandaloneIosAudio()) {
            MUSIC_IOS_AUDIO.tracks = tracks.slice();
            MUSIC_IOS_AUDIO.playlist = tracks.map(function (track) { return buildPlayback(track); });
            MUSIC_IOS_AUDIO.currentIndex = index;
            MUSIC_IOS_AUDIO.active = false;
            MUSIC_IOS_AUDIO.switching = false;
            MUSIC_IOS_AUDIO.playing = false;
            MUSIC_IOS_AUDIO.shuffleOrder = null;
            updateStandaloneIosPlayerBar();
        }

        return true;
    }

    // --- standalone <audio>: элемент, timeupdate, lock-kick ---

    function shouldUseStandaloneIosAudio() {
        return Lampa.Platform.is('apple')
            && currentExternalPlayer() === 'ios'
            && getPlaybackMode() === 'audio';
    }

    function isStandaloneIosAudioActive() {
        return !!(MUSIC_IOS_AUDIO.active && MUSIC_IOS_AUDIO.audio);
    }

    // по трейсам: скраббер локскрина включается только после rate-перехода,
    // случившегося ПОСЛЕ построения Now Playing UI (kick при старте не помог,
    // ручная пауза уже под блокировкой — помогала). Делаем переход сами
    // через 400ms после ухода в фон — на слух незаметно, буфер полон
    function scheduleStandaloneIosLockKick() {
        var audio = MUSIC_IOS_AUDIO.audio;

        if (!audio || audio.paused || MUSIC_IOS_AUDIO.switching) return;
        if (audio.getAttribute('data-source-url') === 'silent-warmup') return;

        setTimeout(function () {
            if (MUSIC_IOS_AUDIO.audio !== audio || audio.paused || MUSIC_IOS_AUDIO.switching) return;
            if (!document.hidden) return;

            traceStandaloneIosAudio('lock-kick', '', true);

            try {
                audio.pause();
                var promise = audio.play();
                if (promise && typeof promise.catch === 'function') promise.catch(function () {});
            } catch (e) {}
        }, 400);
    }

    function standaloneIosAudioTimeupdate() {
        bumpMusicHeatMetric('standaloneTimeupdate');
        if (document.hidden) bumpMusicHeatMetric('standaloneHiddenTimeupdate');
        if (maybeSyntheticStandaloneIosEnded('timeupdate')) return;
        if (expireStandaloneIosSleepTimer('timeupdate', false)) return;

        traceStandaloneIosAudio('timeupdate', '', false);
        scheduleQueueSnapshotSave(false);
        maybeRequestRadioAutoplay('standalone-timeupdate');
        updateStandaloneIosPositionState(false);

        // Во время lock-screen playback страница скрыта, но audio timeupdate
        // продолжает будить JS. DOM-бар пользователю не виден, поэтому не
        // перерисовываем его в фоне: оставляем только Media Session position.
        if (!document.hidden) {
            var now = Date.now();
            if (now - (MUSIC_IOS_AUDIO.lastUiUpdate || 0) >= 500) {
                MUSIC_IOS_AUDIO.lastUiUpdate = now;
                bumpMusicHeatMetric('standaloneUiUpdate');
                updateStandaloneIosPlayerBar();
            }
        }
    }

    function setStandaloneIosTimeupdateListener(active) {
        var audio = MUSIC_IOS_AUDIO.audio;
        if (!audio) return;

        if (!MUSIC_IOS_AUDIO.timeupdateHandler)
            MUSIC_IOS_AUDIO.timeupdateHandler = standaloneIosAudioTimeupdate;

        if (active) {
            if (MUSIC_IOS_AUDIO.timeupdateAttached) return;

            audio.addEventListener('timeupdate', MUSIC_IOS_AUDIO.timeupdateHandler);
            MUSIC_IOS_AUDIO.timeupdateAttached = true;
            bumpMusicHeatMetric('standaloneTimeupdateAttach');
            return;
        }

        if (!MUSIC_IOS_AUDIO.timeupdateAttached) return;

        try {
            audio.removeEventListener('timeupdate', MUSIC_IOS_AUDIO.timeupdateHandler);
        } catch (e) {}

        MUSIC_IOS_AUDIO.timeupdateAttached = false;
        bumpMusicHeatMetric('standaloneTimeupdateDetach');
    }

    function bindStandaloneIosAudioLifecycleTrace() {
        if (MUSIC_IOS_AUDIO.lifecycleBound) return;
        MUSIC_IOS_AUDIO.lifecycleBound = true;

        ['visibilitychange', 'pagehide', 'pageshow', 'focus', 'blur'].forEach(function (eventName) {
            var target = eventName === 'visibilitychange' ? document : window;
            target.addEventListener(eventName, function () {
                traceStandaloneIosAudio('lifecycle-' + eventName, '', true);
                updateStandaloneIosPlaybackState();

                if (eventName === 'visibilitychange' && !document.hidden) {
                    setStandaloneIosTimeupdateListener(true);
                    reviveStandaloneIosKeepAlive();
                    scheduleStandaloneIosVisibleUnfreeze();
                    updateStandaloneIosPositionState(true);
                    updateStandaloneIosPlayerBar();
                }

                // при блокировке переутверждаем сессию: свежие handlers +
                // positionState в момент, когда iOS строит локскрин-контролы
                if (eventName === 'visibilitychange' && document.hidden && isStandaloneIosAudioActive()) {
                    syncStandaloneIosMediaSession();
                    setStandaloneIosTimeupdateListener(false);
                    scheduleStandaloneIosLockKick();
                }
            });
        });
    }

    function standaloneIosAudioElement() {
        if (MUSIC_IOS_AUDIO.audio) return MUSIC_IOS_AUDIO.audio;

        MUSIC_IOS_AUDIO.timeupdateAttached = false;
        var audio = document.createElement('audio');
        audio.preload = 'auto';
        audio.crossOrigin = 'anonymous';
        audio.playsInline = true;
        audio.setAttribute('playsinline', 'playsinline');
        audio.setAttribute('webkit-playsinline', 'webkit-playsinline');
        audio.setAttribute('x-webkit-airplay', 'allow');
        audio.style.position = 'fixed';
        audio.style.width = '0';
        audio.style.height = '0';
        audio.style.opacity = '0';
        audio.style.pointerEvents = 'none';
        audio.style.left = '-9999px';
        audio.style.top = '-9999px';

        audio.addEventListener('play', function () {
            traceStandaloneIosAudio('audio-play', '', true);
        });

        audio.addEventListener('playing', function () {
            stopMusicPlaybackLoading();

            MUSIC_IOS_AUDIO.active = true;
            MUSIC_IOS_AUDIO.switching = false;
            MUSIC_IOS_AUDIO.playing = true;
            MUSIC_IOS_AUDIO.lastPlayingAt = Date.now();

            if (!MUSIC_IOS_AUDIO.keepAliveActive || (MUSIC_IOS_KEEPALIVE_WEBAUDIO && MUSIC_IOS_AUDIO.keepAliveCtx && MUSIC_IOS_AUDIO.keepAliveCtx.state !== 'running'))
                startStandaloneIosKeepAlive('audio-playing');

            var track = MUSIC_IOS_AUDIO.tracks[MUSIC_IOS_AUDIO.currentIndex];
            if (track && track.id) {
                updateQueueCurrent(track.id);
                flushPendingTrackPlayed({
                    from_music_cluster: true,
                    music_track_id: track.id
                });
            }

            traceStandaloneIosAudio('audio-playing', '', true);
            scheduleQueueSnapshotSave(true);
            maybeRequestRadioAutoplay('standalone-playing');
            syncStandaloneIosMediaSession();
            updateStandaloneIosPlayerBar();

        });

        audio.addEventListener('pause', function () {
            MUSIC_IOS_AUDIO.playing = false;
            traceStandaloneIosAudio('audio-pause', '', true);
            scheduleQueueSnapshotSave(true);
            updateStandaloneIosPlaybackState();
            updateStandaloneIosPlayerBar();
        });

        audio.addEventListener('loadedmetadata', function () {
            traceStandaloneIosAudio('audio-loadedmetadata', '', true);
            updateStandaloneIosPositionState(true);
            updateStandaloneIosPlayerBar();
        });

        audio.addEventListener('durationchange', function () {
            traceStandaloneIosAudio('audio-durationchange', '', true);
            updateStandaloneIosPositionState(true);
            updateStandaloneIosPlayerBar();
        });

        audio.addEventListener('ended', function () {
            MUSIC_IOS_AUDIO.switching = false;
            MUSIC_IOS_AUDIO.playing = false;
            traceStandaloneIosAudio('audio-ended', '', true);
            updateStandaloneIosPlayerBar();

            if (!standaloneIosHandleTrackEnd()) {
                stopStandaloneIosKeepAlive('ended');
                syncStandaloneIosMediaSession();
            }
        });

        audio.addEventListener('error', function () {
            MUSIC_IOS_AUDIO.switching = false;
            MUSIC_IOS_AUDIO.playing = false;
            traceStandaloneIosAudio('audio-error', audio.error ? String(audio.error.code || '') : '', true);
            Lampa.Noty.show('Не удалось воспроизвести трек.');
            updateStandaloneIosPlaybackState();
            updateStandaloneIosPlayerBar();
        });

        ['waiting', 'stalled', 'suspend', 'abort', 'emptied', 'canplay', 'canplaythrough', 'seeking', 'seeked'].forEach(function (eventName) {
            audio.addEventListener(eventName, function () {
                // зацикленная warmup-тишина эмитит seeking/seeked каждые ~400ms —
                // не спамим трейсами и обновлениями, пока резолвится реальный src
                if (audio.getAttribute('data-source-url') === 'silent-warmup') return;

                if (eventName === 'waiting' || eventName === 'stalled' || eventName === 'suspend') {
                    MUSIC_IOS_AUDIO.playing = !!(!audio.paused && !audio.ended);

                    // зависание у хвоста при завышенной длительности = конец данных
                    if (maybeSyntheticStandaloneIosEnded(eventName)) return;
                } else if (eventName === 'abort' || eventName === 'emptied') {
                    MUSIC_IOS_AUDIO.playing = false;
                }

                traceStandaloneIosAudio('audio-' + eventName, '', true);
                updateStandaloneIosPlaybackState();

                // после реального завершения seek позиция локскрин-скраббера
                // должна обновиться сразу, не дожидаясь timeupdate
                if (eventName === 'seeked')
                    updateStandaloneIosPositionState(true);
            });
        });

        if (document.body && !audio.parentNode)
            document.body.appendChild(audio);

        MUSIC_IOS_AUDIO.audio = audio;
        MUSIC_IOS_AUDIO.timeupdateHandler = standaloneIosAudioTimeupdate;
        setStandaloneIosTimeupdateListener(!document.hidden);
        bindStandaloneIosAudioLifecycleTrace();
        traceStandaloneIosAudio('audio-create', '', true);
        return audio;
    }

    // --- keep-alive аудио-сессии (resume на заблокированном экране) ---

    // 0.3s тишины, генерится в JS как data: URI — src доступен синхронно
    // прямо в обработчике жеста (сеть бы съела user-gesture кредит) и не
    // зависит от сервера. Используется gesture-warmup'ом и legacy keep-alive
    function buildSilentWavDataUri() {
        var rate = 8000;
        var samples = Math.floor(rate * 0.3);
        var bytes = new Uint8Array(44 + samples);
        var pos = 0;

        function str(value) { for (var i = 0; i < value.length; i++) bytes[pos++] = value.charCodeAt(i); }
        function u32(value) { bytes[pos++] = value & 255; bytes[pos++] = (value >> 8) & 255; bytes[pos++] = (value >> 16) & 255; bytes[pos++] = (value >> 24) & 255; }
        function u16(value) { bytes[pos++] = value & 255; bytes[pos++] = (value >> 8) & 255; }

        str('RIFF'); u32(36 + samples); str('WAVE');
        str('fmt '); u32(16); u16(1); u16(1); u32(rate); u32(rate); u16(1); u16(8);
        str('data'); u32(samples);
        for (var i = 0; i < samples; i++) bytes[pos++] = 128;

        var binary = '';
        for (var j = 0; j < bytes.length; j++) binary += String.fromCharCode(bytes[j]);
        return 'data:audio/wav;base64,' + btoa(binary);
    }

    function standaloneIosKeepAliveElement() {
        if (MUSIC_IOS_AUDIO.keepAlive) return MUSIC_IOS_AUDIO.keepAlive;

        var el = document.createElement('audio');
        el.src = buildSilentWavDataUri();
        el.loop = true;
        el.preload = 'auto';
        el.playsInline = true;
        el.setAttribute('playsinline', 'playsinline');
        el.setAttribute('webkit-playsinline', 'webkit-playsinline');
        el.style.position = 'fixed';
        el.style.width = '0';
        el.style.height = '0';
        el.style.opacity = '0';
        el.style.pointerEvents = 'none';
        el.style.left = '-9999px';
        el.style.top = '-9999px';

        if (document.body) document.body.appendChild(el);

        MUSIC_IOS_AUDIO.keepAlive = el;
        return el;
    }

    // Web Audio держит аудио-сессию так же, как <audio>-луп, но НЕ участвует в
    // Now Playing: иконка play/pause на локскрине следует за состоянием основного
    // трека. false → откат на старый <audio>-луп (иконка будет врать на паузе)
    var MUSIC_IOS_KEEPALIVE_WEBAUDIO = true;

    function standaloneIosKeepAliveContext() {
        if (MUSIC_IOS_AUDIO.keepAliveCtx) return MUSIC_IOS_AUDIO.keepAliveCtx;

        var Ctor = window.AudioContext || window.webkitAudioContext;
        if (!Ctor) return null;

        var ctx;
        try {
            ctx = new Ctor();
        } catch (e) {
            return null;
        }

        try {
            var gain = ctx.createGain();
            gain.gain.value = 0;
            gain.connect(ctx.destination);

            var source;
            if (typeof ctx.createConstantSource === 'function') {
                source = ctx.createConstantSource();
            } else {
                source = ctx.createOscillator();
                source.frequency.value = 20;
            }
            source.connect(gain);
            source.start();
        } catch (e) {}

        ctx.onstatechange = function () {
            traceStandaloneIosAudio('keepalive-ctx-state', ctx.state || '', true);

            if (MUSIC_IOS_AUDIO.keepAliveActive && ctx.state !== 'running') {
                try { ctx.resume(); } catch (e) {}
            }
        };

        MUSIC_IOS_AUDIO.keepAliveCtx = ctx;
        armStandaloneIosKeepAliveGestureUnlock();
        return ctx;
    }

    // iOS переводит AudioContext в running только из реального жеста на видимой
    // странице: если плейлист поднялся через restore без тапа, контекст висит
    // interrupted и keep-alive ничего не держит (resume() из фона даёт
    // InvalidStateError). Любой тап по странице поднимает контекст
    function armStandaloneIosKeepAliveGestureUnlock() {
        if (MUSIC_IOS_AUDIO.keepAliveGestureArmed) return;
        MUSIC_IOS_AUDIO.keepAliveGestureArmed = true;

        var unlock = function () {
            var ctx = MUSIC_IOS_AUDIO.keepAliveCtx;

            if (!ctx || !MUSIC_IOS_AUDIO.keepAliveActive) return;
            if (ctx.state === 'running') return;

            traceStandaloneIosAudio('keepalive-gesture-resume', ctx.state || '', true);

            try {
                var promise = ctx.resume();
                if (promise && typeof promise.catch === 'function') promise.catch(function () {});
            } catch (e) {}
        };

        ['touchend', 'mousedown', 'keydown'].forEach(function (eventName) {
            document.addEventListener(eventName, unlock, true);
        });
    }

    // бесшумный источник работает ПОСТОЯННО, пока standalone-плеер активен:
    // стартовать его в момент паузы под блокировкой поздно — запуск из фоновой
    // страницы iOS замораживает; запущенный на видимой странице в жестовом
    // контексте, он переживает блокировку и не даёт деактивировать аудио-сессию
    function startStandaloneIosKeepAlive(origin) {
        bumpMusicHeatMetric('keepAliveStartCall');
        MUSIC_IOS_AUDIO.keepAliveActive = true;

        if (MUSIC_IOS_KEEPALIVE_WEBAUDIO) {
            var ctx = standaloneIosKeepAliveContext();

            if (!ctx) {
                MUSIC_IOS_AUDIO.keepAliveActive = false;
                traceStandaloneIosAudio('keepalive-error', origin + ' no-webaudio', true);
                return;
            }

            try {
                var resumePromise = ctx.resume();
                if (resumePromise && typeof resumePromise.catch === 'function') resumePromise.catch(function (error) {
                    traceStandaloneIosAudio('keepalive-rejected', origin + ' ' + (error && error.name ? error.name : String(error || '')), true);
                });
                bumpMusicHeatMetric('keepAliveStartWebAudio');
                traceStandaloneIosAudio('keepalive-start', origin + ' webaudio ' + (ctx.state || ''), true);
            } catch (e) {
                MUSIC_IOS_AUDIO.keepAliveActive = false;
                traceStandaloneIosAudio('keepalive-error', origin + ' ' + (e && e.name ? e.name : String(e || '')), true);
            }

            return;
        }

        var el = standaloneIosKeepAliveElement();

        try {
            var promise = el.play();
            if (promise && typeof promise.catch === 'function') promise.catch(function (error) {
                MUSIC_IOS_AUDIO.keepAliveActive = false;
                traceStandaloneIosAudio('keepalive-rejected', origin + ' ' + (error && error.name ? error.name : String(error || '')), true);
            });
            bumpMusicHeatMetric('keepAliveStartLoop');
            traceStandaloneIosAudio('keepalive-start', origin || '', true);
        } catch (e) {
            MUSIC_IOS_AUDIO.keepAliveActive = false;
            traceStandaloneIosAudio('keepalive-error', origin + ' ' + (e && e.name ? e.name : String(e || '')), true);
        }
    }

    // если iOS остановила/заморозила keep-alive пока страница была скрыта,
    // на видимой странице его можно перезапустить
    function reviveStandaloneIosKeepAlive() {
        if (!MUSIC_IOS_AUDIO.keepAliveActive) return;

        if (MUSIC_IOS_KEEPALIVE_WEBAUDIO) {
            var ctx = MUSIC_IOS_AUDIO.keepAliveCtx;
            if (!ctx || ctx.state === 'running') return;

            bumpMusicHeatMetric('keepAliveReviveWebAudio');
            traceStandaloneIosAudio('keepalive-revive', 'webaudio ' + (ctx.state || ''), true);

            try {
                var resumePromise = ctx.resume();
                if (resumePromise && typeof resumePromise.catch === 'function') resumePromise.catch(function () {});
            } catch (e) {}

            return;
        }

        var el = MUSIC_IOS_AUDIO.keepAlive;
        if (!el || !el.paused) return;

        bumpMusicHeatMetric('keepAliveReviveLoop');
        traceStandaloneIosAudio('keepalive-revive', '', true);

        try {
            var promise = el.play();
            if (promise && typeof promise.catch === 'function') promise.catch(function () {});
        } catch (e) {}
    }

    function stopStandaloneIosKeepAlive(origin) {
        if (MUSIC_IOS_AUDIO.keepAliveActive)
            traceStandaloneIosAudio('keepalive-stop', origin || '', true);

        bumpMusicHeatMetric('keepAliveStop');
        MUSIC_IOS_AUDIO.keepAliveActive = false;

        if (MUSIC_IOS_AUDIO.keepAliveCtx) {
            try {
                MUSIC_IOS_AUDIO.keepAliveCtx.suspend();
            } catch (e) {}
        }

        if (MUSIC_IOS_AUDIO.keepAlive) {
            try {
                MUSIC_IOS_AUDIO.keepAlive.pause();
            } catch (e) {}
        }
    }

    function clearStandaloneIosMediaSession() {
        var session = mediaSessionObject();
        if (!session) return;

        ['play', 'pause', 'seekto', 'seekforward', 'seekbackward', 'previoustrack', 'nexttrack'].forEach(function (action) {
            setMediaSessionHandler(action, null);
        });

        try {
            session.metadata = null;
        } catch (e) {}

        try {
            if ('playbackState' in session) session.playbackState = 'none';
        } catch (e) {}

        MUSIC_IOS_AUDIO.mediaSessionTrackId = null;
        MUSIC_IOS_AUDIO.mediaSessionArmed = false;
        MUSIC_IOS_AUDIO.lastPositionSync = 0;
    }

    // --- shuffle / repeat / порядок воспроизведения ---

    function getStandaloneIosRepeatMode() {
        var mode = String(Lampa.Storage.get(MUSIC.storage.repeat_mode, 'off') || 'off');
        return mode === 'one' || mode === 'all' ? mode : 'off';
    }

    function isStandaloneIosShuffle() {
        return Lampa.Storage.get(MUSIC.storage.shuffle, false) === true;
    }

    function rebuildStandaloneIosShuffleOrder() {
        var length = hasStandaloneIosRestoredQueue()
            ? queueTracks().length
            : MUSIC_IOS_AUDIO.playlist.length;
        var currentIndex = hasStandaloneIosRestoredQueue()
            ? queueCurrentIndex()
            : MUSIC_IOS_AUDIO.currentIndex;
        var order = [];

        for (var i = 0; i < length; i++) order.push(i);

        for (var j = order.length - 1; j > 0; j--) {
            var k = Math.floor(Math.random() * (j + 1));
            var swap = order[j];
            order[j] = order[k];
            order[k] = swap;
        }

        // текущий трек — в начало порядка: «дальше» предсказуемо продолжает игру
        var currentPos = order.indexOf(currentIndex);
        if (currentPos > 0) {
            order.splice(currentPos, 1);
            order.unshift(currentIndex);
        }

        MUSIC_IOS_AUDIO.shuffleOrder = order;
    }

    function standaloneIosOrder() {
        if (!isStandaloneIosShuffle()) return null;

        var length = hasStandaloneIosRestoredQueue()
            ? queueTracks().length
            : MUSIC_IOS_AUDIO.playlist.length;
        if (!length) return null;

        if (!Array.isArray(MUSIC_IOS_AUDIO.shuffleOrder) || MUSIC_IOS_AUDIO.shuffleOrder.length !== length)
            rebuildStandaloneIosShuffleOrder();

        return MUSIC_IOS_AUDIO.shuffleOrder;
    }

    // соседний индекс воспроизведения с учётом shuffle-порядка и repeat=all
    function standaloneIosNeighborIndex(offset) {
        var length = hasStandaloneIosRestoredQueue()
            ? queueTracks().length
            : MUSIC_IOS_AUDIO.playlist.length;
        var current = hasStandaloneIosRestoredQueue()
            ? queueCurrentIndex()
            : MUSIC_IOS_AUDIO.currentIndex;

        if (!length || current < 0) return -1;

        var order = standaloneIosOrder();
        var pos = order ? order.indexOf(current) : current;
        if (pos < 0) pos = 0;

        var nextPos = pos + offset;

        if (getStandaloneIosRepeatMode() === 'all' && length > 1)
            nextPos = ((nextPos % length) + length) % length;

        if (nextPos < 0 || nextPos >= length) return -1;

        return order ? order[nextPos] : nextPos;
    }

    // конец трека: repeat one → сыграть заново, иначе — следующий по порядку
    function standaloneIosHandleTrackEnd() {
        if (getStandaloneIosRepeatMode() === 'one') {
            var audio = MUSIC_IOS_AUDIO.audio;
            if (!audio) return false;

            try {
                audio.currentTime = 0;
            } catch (e) {}

            try {
                var promise = audio.play();
                if (promise && typeof promise.catch === 'function') promise.catch(function () {});
            } catch (e) {
                return false;
            }

            updateStandaloneIosPlaybackState();
            updateStandaloneIosPositionState(true);
            return true;
        }

        var nextIndex = standaloneIosNeighborIndex(1);
        return nextIndex >= 0 && standaloneIosPlayIndex(nextIndex);
    }

    function toggleStandaloneIosShuffle() {
        var enabled = !isStandaloneIosShuffle();

        Lampa.Storage.set(MUSIC.storage.shuffle, enabled);
        MUSIC_IOS_AUDIO.shuffleOrder = null;

        Lampa.Noty.show(enabled ? 'Перемешивание включено.' : 'Перемешивание выключено.');
        updateStandaloneIosPlayerBar();
        syncStandaloneIosMediaSession();
    }

    function cycleStandaloneIosRepeatMode() {
        var mode = getStandaloneIosRepeatMode();
        var next = mode === 'off' ? 'all' : (mode === 'all' ? 'one' : 'off');

        Lampa.Storage.set(MUSIC.storage.repeat_mode, next);

        Lampa.Noty.show(next === 'all' ? 'Повтор очереди.' : (next === 'one' ? 'Повтор трека.' : 'Повтор выключен.'));
        updateStandaloneIosPlayerBar();
        syncStandaloneIosMediaSession();
    }

    var MUSIC_IOS_ART_TINT_CACHE = {};

    // средний цвет обложки → верх фонового градиента (обложки идут через
    // image-proxy того же origin, поэтому canvas не tainted; чужой хост
    // упадёт в catch и оставит дефолтный тинт)
    // --- тинт обложки, состояние плеера и общие действия (бар + фулл-плеер) ---

    function computeStandaloneIosArtTint(url, callback) {
        if (!url) { callback(''); return; }
        if (Object.prototype.hasOwnProperty.call(MUSIC_IOS_ART_TINT_CACHE, url)) {
            callback(MUSIC_IOS_ART_TINT_CACHE[url]);
            return;
        }

        var img = new Image();

        img.onload = function () {
            var tint = '';

            try {
                var size = 12;
                var canvas = document.createElement('canvas');
                canvas.width = size;
                canvas.height = size;

                var ctx = canvas.getContext('2d');
                ctx.drawImage(img, 0, 0, size, size);

                var data = ctx.getImageData(0, 0, size, size).data;
                var r = 0, g = 0, b = 0, count = data.length / 4;

                for (var i = 0; i < data.length; i += 4) {
                    r += data[i];
                    g += data[i + 1];
                    b += data[i + 2];
                }

                r /= count; g /= count; b /= count;

                // подтянуть к комфортной яркости фона, сохранив оттенок
                var peak = Math.max(r, g, b, 1);
                var scale = Math.min(2.2, 118 / peak);
                r = Math.round(Math.min(255, r * scale));
                g = Math.round(Math.min(255, g * scale));
                b = Math.round(Math.min(255, b * scale));

                tint = 'rgba(' + r + ',' + g + ',' + b + ',0.5)';
            } catch (e) {
                tint = '';
            }

            setCappedCacheEntry(MUSIC_IOS_ART_TINT_CACHE, url, tint, 80);
            callback(tint);
        };

        img.onerror = function () {
            setCappedCacheEntry(MUSIC_IOS_ART_TINT_CACHE, url, '', 80);
            callback('');
        };

        img.src = url;
    }

    function standaloneIosPlayerState() {
        var audio = MUSIC_IOS_AUDIO.audio;
        var restored = hasStandaloneIosRestoredQueue();
        var tracks = restored ? queueTracks() : (MUSIC_IOS_AUDIO.tracks || []);
        var currentIndex = restored ? queueCurrentIndex() : MUSIC_IOS_AUDIO.currentIndex;
        var active = (isStandaloneIosAudioActive() || restored) && tracks.length > 0;
        var track = active ? tracks[currentIndex] : null;
        var duration = active && track ? standaloneIosEffectiveDuration(restored ? null : audio, track) : 0;
        var current = restored
            ? Math.max(0, Number(MUSIC_QUEUE_RESTORE.position || 0))
            : (audio && isFinite(audio.currentTime) ? audio.currentTime : 0);

        if (duration) current = Math.max(0, Math.min(duration, current));

        return {
            active: active,
            restored: restored,
            audio: restored ? null : audio,
            track: track,
            tracks: tracks,
            currentIndex: currentIndex,
            hasPrev: active && standaloneIosNeighborIndex(-1) >= 0,
            hasNext: active && standaloneIosNeighborIndex(1) >= 0,
            canQueue: active && tracks.length > 1,
            duration: duration || 0,
            current: current || 0,
            playing: !!(!restored && audio && MUSIC_IOS_AUDIO.playing && !audio.paused)
        };
    }

    function seekStandaloneIosByRangeValue(value) {
        var state = standaloneIosPlayerState();
        var position = state.duration ? (state.duration * Number(value || 0) / 1000) : 0;

        if (state.restored) {
            MUSIC_QUEUE_RESTORE.position = Math.max(0, Math.min(state.duration || position, position));
            scheduleQueueSnapshotSave(true);
        } else if (state.audio && isFinite(position)) {
            try {
                state.audio.currentTime = Math.max(0, Math.min(state.duration || position, position));
            } catch (e) {}
        }

        updateStandaloneIosPositionState(true);
        updateStandaloneIosPlayerBar();
    }

    function handleStandaloneIosPlayerAction(action) {
        var audio = MUSIC_IOS_AUDIO.audio;

        if (action === 'expand') {
            openStandaloneIosFullPlayer();
            return;
        }

        if (action === 'collapse') {
            closeStandaloneIosFullPlayer();
            return;
        }

        if (action === 'sheet-close') {
            closeStandaloneIosSheet();
            return;
        }

        if (action === 'queue') {
            if (MUSIC_IOS_FULL_PLAYER_OPEN) openStandaloneIosQueueSheet();
            else openStandaloneIosFullPlayer();
            return;
        }

        if (action === 'radio') {
            var radioEnabled = !isRadioAutoplayEnabled();
            setRadioAutoplayEnabled(radioEnabled);
            Lampa.Noty.show(radioEnabled ? 'Автоподборка треков включена.' : 'Автоподборка треков выключена.');
            if (MUSIC_IOS_FULL_PLAYER_OPEN) updateStandaloneIosFullPlayer();
            return;
        }

        if (action === 'sources') {
            openStandaloneIosSourcesSheet();
            return;
        }

        if (action === 'lyrics') {
            openStandaloneIosLyricsSheet();
            return;
        }

        if (action === 'bookmark') {
            toggleStandaloneIosCurrentBookmark();
            return;
        }

        if (action === 'timer') {
            openStandaloneIosTimerSheet();
            return;
        }

        if (action === 'stop') {
            closeStandaloneIosFullPlayer();
            stopStandaloneIosAudioPlayback();
            return;
        }

        if (hasStandaloneIosRestoredQueue()) {
            if (action === 'shuffle') {
                toggleStandaloneIosShuffle();
                return;
            }

            if (action === 'repeat') {
                cycleStandaloneIosRepeatMode();
                return;
            }

            if (action === 'prev') {
                var restoredPrev = standaloneIosNeighborIndex(-1);
                if (restoredPrev >= 0) selectRestoredQueueIndex(restoredPrev);
                return;
            }

            if (action === 'next') {
                var restoredNext = standaloneIosNeighborIndex(1);
                if (restoredNext >= 0) selectRestoredQueueIndex(restoredNext);
                return;
            }

            if (action === 'playpause') {
                resumeRestoredQueueSnapshot();
                return;
            }
        }

        if (!isStandaloneIosAudioActive() || !audio) return;

        if (action === 'shuffle') {
            toggleStandaloneIosShuffle();
            return;
        }

        if (action === 'repeat') {
            cycleStandaloneIosRepeatMode();
            return;
        }

        if (action === 'prev') {
            var prevIndex = standaloneIosNeighborIndex(-1);
            if (prevIndex >= 0) standaloneIosPlayIndex(prevIndex);
            return;
        }

        if (action === 'next') {
            var nextIndex = standaloneIosNeighborIndex(1);
            if (nextIndex >= 0) standaloneIosPlayIndex(nextIndex);
            return;
        }

        if (action === 'playpause') {
            if (audio.paused) {
                traceStandaloneIosAudio('bar-play', '', true);
                startStandaloneIosKeepAlive('bar-play');
                var promise = audio.play();
                if (promise && typeof promise.catch === 'function') promise.catch(function (error) {
                    traceStandaloneIosAudio('bar-play-rejected', error && error.name ? error.name : String(error || ''), true);
                });
            } else {
                traceStandaloneIosAudio('bar-pause', '', true);
                audio.pause();
                startStandaloneIosKeepAlive('bar-pause');
            }

            updateStandaloneIosPlaybackState();
            updateStandaloneIosPlayerBar();
        }
    }

    // ===== IOS FULL PLAYER =====

    // --- открытие/закрытие фулл-плеера, back-навигация ---

    function openStandaloneIosFullPlayer() {
        var player = ensureStandaloneIosFullPlayer();

        if (!player || !player.length) return;

        if (!MUSIC_IOS_FULL_PLAYER_OPEN)
            MUSIC_IOS_FULL_PLAYER_RETURN_CONTROLLER = currentStandaloneIosControllerName();

        MUSIC_IOS_FULL_PLAYER_OPEN = true;
        player.addClass('lm-ios-full-player--visible');
        player.attr('data-scroll-current', 'true');
        $('body').addClass('lm-ios-full-player-open');
        bumpMusicHeatMetric('fullPlayerOpen');
        logStandaloneIosFullEvent('open');
        updateStandaloneIosFullPlayer();

        if (Lampa.Controller && typeof Lampa.Controller.add === 'function') {
            Lampa.Controller.add('lampac_music_full_player', {
                toggle: function () {},
                back: function () {
                    closeStandaloneIosFullPlayer();
                }
            });
            Lampa.Controller.toggle('lampac_music_full_player');
        }
    }

    function closeStandaloneIosFullPlayer() {
        bumpMusicHeatMetric('fullPlayerClose');
        logStandaloneIosFullEvent('close');
        MUSIC_IOS_FULL_PLAYER_OPEN = false;

        if (MUSIC_IOS_FULL_PLAYER && MUSIC_IOS_FULL_PLAYER.length) {
            MUSIC_IOS_FULL_PLAYER.removeClass('lm-ios-full-player--visible');
            MUSIC_IOS_FULL_PLAYER.removeAttr('data-seeking');
            MUSIC_IOS_FULL_PLAYER.removeAttr('data-scroll-current');
            closeStandaloneIosSheet();
        }

        $('body').removeClass('lm-ios-full-player-open');

        if (Lampa.Controller && typeof Lampa.Controller.toggle === 'function') {
            var controller = MUSIC_IOS_FULL_PLAYER_RETURN_CONTROLLER || 'content';

            MUSIC_IOS_FULL_PLAYER_RETURN_CONTROLLER = 'content';
            if (controller !== 'lampac_music_full_player') Lampa.Controller.toggle(controller);
        }
    }

    function currentStandaloneIosControllerName() {
        try {
            if (Lampa.Controller && typeof Lampa.Controller.enabled === 'function') {
                var enabled = Lampa.Controller.enabled();
                var name = enabled && enabled.name ? enabled.name : '';

                if (name && name !== 'lampac_music_full_player') return name;
            }
        } catch (e) {}

        return 'content';
    }

    function handleStandaloneIosFullPlayerBack(event) {
        if (!MUSIC_IOS_FULL_PLAYER_OPEN) return;

        var key = event && (event.key || event.code || '');
        var code = event && (event.keyCode || event.which || 0);
        var isBack = key === 'Escape'
            || key === 'Backspace'
            || key === 'BrowserBack'
            || code === 8
            || code === 27
            || code === 461
            || code === 10009;

        if (!isBack) return;

        event.preventDefault();
        event.stopPropagation();
        closeStandaloneIosFullPlayer();
    }

    // --- нативные свайпы: закрытие плеера и шита жестом ---

    function isStandaloneIosGestureTargetBlocked(target) {
        return !!$(target).closest('input, textarea, select, [data-action], .lm-ios-full-player__sheet-panel, .lm-ios-full-player__queue-list').length;
    }

    function standaloneIosGestureVelocity(touch, axis) {
        if (!touch || !touch.prevTime || !touch.lastTime || touch.lastTime <= touch.prevTime) return 0;

        var distance = axis === 'x' ? (touch.lastX - touch.prevX) : (touch.lastY - touch.prevY);
        return Math.abs(distance) / Math.max(1, touch.lastTime - touch.prevTime);
    }

    function standaloneIosShellCanScroll(player) {
        return false;
    }

    function standaloneIosShellCanCaptureVertical(player, dy) {
        return true;
    }

    function setStandaloneIosFullGestureTransform(player, mode, dx, dy) {
        var shell = player.find('.lm-ios-full-player__shell');

        if (!shell.length) return;

        player.addClass('lm-ios-full-player--gesture-dragging');

        if (mode === 'vertical') {
            var y = dy > 0 ? dy * 0.62 : dy * 0.24;
            shell.css({
                transform: 'translate3d(0,' + y + 'px,0)',
                opacity: String(Math.max(0.72, 1 - Math.max(0, dy) / 900))
            });
            return;
        }

        var x = dx * 0.22;
        shell.css({
            transform: 'translate3d(' + x + 'px,0,0)',
            opacity: String(Math.max(0.78, 1 - Math.abs(dx) / 850))
        });
    }

    function resetStandaloneIosFullGestureTransform(player) {
        if (!player || !player.length) return;

        player.removeClass('lm-ios-full-player--gesture-dragging');
        player.find('.lm-ios-full-player__shell').css({
            transform: '',
            opacity: ''
        });
    }

    function closeStandaloneIosFullPlayerWithGesture(player) {
        var shell = player.find('.lm-ios-full-player__shell');

        bumpMusicHeatMetric('fullPlayerGestureClose');
        player.removeClass('lm-ios-full-player--gesture-dragging');
        shell.css({
            transform: 'translate3d(0,110%,0)',
            opacity: '0'
        });

        setTimeout(function () {
            resetStandaloneIosFullGestureTransform(player);
            closeStandaloneIosFullPlayer();
        }, 200);
    }

    function resetStandaloneIosSheetGestureTransform(player) {
        if (!player || !player.length) return;

        player.removeClass('lm-ios-full-player--sheet-dragging');
        player.find('.lm-ios-full-player__sheet-panel').css('transform', '');
    }

    function setStandaloneIosSheetGestureTransform(player, dy) {
        if (!player || !player.length || dy <= 0) return;

        player.addClass('lm-ios-full-player--sheet-dragging');
        player.find('.lm-ios-full-player__sheet-panel').css('transform', 'translate3d(0,' + (dy * 0.82) + 'px,0)');
    }

    function shouldCloseStandaloneIosSheetByGesture(dy, vy) {
        return dy > 72 || (dy > 40 && vy > 0.34);
    }

    function closeStandaloneIosSheetWithGesture(player) {
        if (!player || !player.length) return closeStandaloneIosSheet();

        player.removeClass('lm-ios-full-player--sheet-dragging');
        player.find('.lm-ios-full-player__sheet-panel').css('transform', 'translate3d(0,110%,0)');

        setTimeout(function () {
            closeStandaloneIosSheet();
            resetStandaloneIosSheetGestureTransform(player);
        }, 200);
    }

    function bindStandaloneIosFullPlayerGestures(player) {
        if (!player || !player.length || player.attr('data-gestures-bound') === 'true') return;

        var element = player.get(0);
        var fullTouch = null;
        var sheetTouch = null;
        var queuePullTouch = null;

        player.attr('data-gestures-bound', 'true');

        element.addEventListener('touchstart', function (event) {
            if (!MUSIC_IOS_FULL_PLAYER_OPEN || player.hasClass('lm-ios-full-player--sheet-open')) return;
            if (!event.touches || event.touches.length !== 1) return;
            if (isStandaloneIosGestureTargetBlocked(event.target)) return;

            var touch = event.touches[0];
            fullTouch = {
                x: touch.clientX,
                y: touch.clientY,
                lastX: touch.clientX,
                lastY: touch.clientY,
                prevX: touch.clientX,
                prevY: touch.clientY,
                time: Date.now(),
                lastTime: Date.now(),
                prevTime: Date.now(),
                mode: ''
            };
        }, { passive: true });

        element.addEventListener('touchmove', function (event) {
            if (!fullTouch || !event.touches || event.touches.length !== 1) return;

            var touch = event.touches[0];
            var now = Date.now();
            var dx = touch.clientX - fullTouch.x;
            var dy = touch.clientY - fullTouch.y;
            var absX = Math.abs(dx);
            var absY = Math.abs(dy);

            fullTouch.prevX = fullTouch.lastX;
            fullTouch.prevY = fullTouch.lastY;
            fullTouch.prevTime = fullTouch.lastTime;
            fullTouch.lastX = touch.clientX;
            fullTouch.lastY = touch.clientY;
            fullTouch.lastTime = now;

            if (!fullTouch.mode && Math.max(absX, absY) > 14) {
                if (absX > absY * 1.18) {
                    fullTouch.mode = 'horizontal';
                } else if (absY > absX * 1.18 && standaloneIosShellCanCaptureVertical(player, dy)) {
                    fullTouch.mode = 'vertical';
                } else if (standaloneIosShellCanScroll(player)) {
                    fullTouch = null;
                    return;
                }
            }

            if (!fullTouch || !fullTouch.mode) return;

            event.preventDefault();
            bumpMusicHeatMetric('fullPlayerGestureMove');
            setStandaloneIosFullGestureTransform(player, fullTouch.mode, dx, dy);
        }, { passive: false });

        element.addEventListener('touchend', function () {
            if (!fullTouch) return;

            var dx = fullTouch.lastX - fullTouch.x;
            var dy = fullTouch.lastY - fullTouch.y;
            var absX = Math.abs(dx);
            var absY = Math.abs(dy);
            var mode = fullTouch.mode;
            var vx = standaloneIosGestureVelocity(fullTouch, 'x');
            var vy = standaloneIosGestureVelocity(fullTouch, 'y');
            var height = Math.max(1, element.clientHeight || window.innerHeight || 1);

            fullTouch = null;

            if (mode === 'vertical' && absY > absX * 1.18 && (absY > Math.min(150, height * 0.18) || (absY > 40 && vy > 0.32))) {
                if (dy > 0) {
                    closeStandaloneIosFullPlayerWithGesture(player);
                } else {
                    resetStandaloneIosFullGestureTransform(player);
                    openStandaloneIosQueueSheet();
                }
                return;
            }

            resetStandaloneIosFullGestureTransform(player);

            if (mode === 'horizontal' && absX > 78 && absX > absY * 1.25 && vx > 0.16) {
                if (dx < 0) handleStandaloneIosPlayerAction('next');
                else handleStandaloneIosPlayerAction('prev');
            }
        }, { passive: true });

        element.addEventListener('touchcancel', function () {
            fullTouch = null;
            resetStandaloneIosFullGestureTransform(player);
        }, { passive: true });

        player.on('touchstart', '.lm-ios-full-player__sheet-head', function (event) {
            var original = event.originalEvent;
            if (!original || !original.touches || original.touches.length !== 1) return;

            var touch = original.touches[0];
            sheetTouch = {
                y: touch.clientY,
                lastY: touch.clientY,
                prevY: touch.clientY,
                lastTime: Date.now(),
                prevTime: Date.now()
            };
        });

        player.on('touchmove', '.lm-ios-full-player__sheet-head', function (event) {
            var original = event.originalEvent;
            if (!sheetTouch || !original || !original.touches || original.touches.length !== 1) return;

            var touch = original.touches[0];
            var now = Date.now();
            var dy = touch.clientY - sheetTouch.y;

            sheetTouch.prevY = sheetTouch.lastY;
            sheetTouch.prevTime = sheetTouch.lastTime;
            sheetTouch.lastY = touch.clientY;
            sheetTouch.lastTime = now;

            if (dy <= 0) return;

            event.preventDefault();
            bumpMusicHeatMetric('fullPlayerSheetGestureMove');
            setStandaloneIosSheetGestureTransform(player, dy);
        });

        player.on('touchend touchcancel', '.lm-ios-full-player__sheet-head', function () {
            if (!sheetTouch) return;

            var dy = sheetTouch.lastY - sheetTouch.y;
            var vy = standaloneIosGestureVelocity(sheetTouch, 'y');

            sheetTouch = null;

            if (shouldCloseStandaloneIosSheetByGesture(dy, vy)) {
                closeStandaloneIosSheetWithGesture(player);
            } else {
                resetStandaloneIosSheetGestureTransform(player);
            }
        });

        player.on('touchstart', '.lm-ios-full-player__queue-list', function (event) {
            var original = event.originalEvent;
            if (!original || !original.touches || original.touches.length !== 1) return;

            var touch = original.touches[0];
            queuePullTouch = {
                x: touch.clientX,
                y: touch.clientY,
                lastX: touch.clientX,
                lastY: touch.clientY,
                prevX: touch.clientX,
                prevY: touch.clientY,
                lastTime: Date.now(),
                prevTime: Date.now(),
                captured: false,
                scroller: this
            };
        });

        player.on('touchmove', '.lm-ios-full-player__queue-list', function (event) {
            var original = event.originalEvent;
            if (!queuePullTouch || !original || !original.touches || original.touches.length !== 1) return;

            var touch = original.touches[0];
            var now = Date.now();
            var dx = touch.clientX - queuePullTouch.x;
            var dy = touch.clientY - queuePullTouch.y;
            var absX = Math.abs(dx);
            var absY = Math.abs(dy);
            var atTop = (queuePullTouch.scroller ? queuePullTouch.scroller.scrollTop : this.scrollTop) <= 1;

            queuePullTouch.prevX = queuePullTouch.lastX;
            queuePullTouch.prevY = queuePullTouch.lastY;
            queuePullTouch.prevTime = queuePullTouch.lastTime;
            queuePullTouch.lastX = touch.clientX;
            queuePullTouch.lastY = touch.clientY;
            queuePullTouch.lastTime = now;

            if (!queuePullTouch.captured) {
                if (dy <= 0 || absY <= 14) return;
                if (!atTop || absY <= absX * 1.18) return;
                queuePullTouch.captured = true;
                queuePullTouch.x = touch.clientX;
                queuePullTouch.y = touch.clientY;
                queuePullTouch.prevX = touch.clientX;
                queuePullTouch.prevY = touch.clientY;
                queuePullTouch.lastX = touch.clientX;
                queuePullTouch.lastY = touch.clientY;
                queuePullTouch.prevTime = now;
                queuePullTouch.lastTime = now;
                dx = 0;
                dy = 0;
            }

            event.preventDefault();
            bumpMusicHeatMetric('fullPlayerQueuePullMove');
            setStandaloneIosSheetGestureTransform(player, dy);
        });

        player.on('touchend touchcancel', '.lm-ios-full-player__queue-list', function () {
            if (!queuePullTouch) return;

            var dy = queuePullTouch.lastY - queuePullTouch.y;
            var vy = standaloneIosGestureVelocity(queuePullTouch, 'y');
            var captured = queuePullTouch.captured;

            queuePullTouch = null;

            if (captured && shouldCloseStandaloneIosSheetByGesture(dy, vy)) {
                closeStandaloneIosSheetWithGesture(player);
            } else if (captured) {
                resetStandaloneIosSheetGestureTransform(player);
            }
        });
    }

    // --- bottom-sheet: каркас, очередь ---

    function closeStandaloneIosSheet() {
        if (!MUSIC_IOS_FULL_PLAYER || !MUSIC_IOS_FULL_PLAYER.length) return;

        bumpMusicHeatMetric('fullPlayerSheetClose');
        logStandaloneIosFullEvent('sheet-close', MUSIC_IOS_FULL_PLAYER.attr('data-sheet-kind') || '');
        MUSIC_IOS_FULL_PLAYER.removeClass('lm-ios-full-player--sheet-open');
        MUSIC_IOS_FULL_PLAYER.find('.lm-ios-full-player__sheet-body').removeClass('lm-ios-full-player__sheet-body--queue').empty();
        MUSIC_IOS_FULL_PLAYER.removeAttr('data-sheet-track-id');
        MUSIC_IOS_FULL_PLAYER.removeAttr('data-sheet-kind');
        MUSIC_IOS_FULL_PLAYER.removeAttr('data-lyrics-line');
        MUSIC_IOS_FULL_PLAYER.removeAttr('data-lyrics-manual');
        MUSIC_IOS_FULL_PLAYER.removeAttr('data-queue-key');
        MUSIC_IOS_FULL_PLAYER.removeAttr('data-current-index');
        MUSIC_IOS_FULL_PLAYER.removeAttr('data-scroll-current');
    }

    function showStandaloneIosSheet(title) {
        var player = ensureStandaloneIosFullPlayer();
        if (!player || !player.length) return $();

        var body = player.find('.lm-ios-full-player__sheet-body');
        player.find('.lm-ios-full-player__sheet-title').text(title || '');
        body.removeClass('lm-ios-full-player__sheet-body--queue').empty();
        player.removeAttr('data-sheet-kind');
        player.addClass('lm-ios-full-player--sheet-open');
        bumpMusicHeatMetric('fullPlayerSheetOpen');
        logStandaloneIosFullEvent('sheet-open', title || '');
        return body;
    }

    function appendStandaloneIosSheetMessage(body, message, loading) {
        var item = $('<div class="lm-ios-full-player__sheet-message"></div>');
        item.toggleClass('lm-ios-full-player__sheet-message--loading', !!loading);
        item.text(message || '');
        body.append(item);
        return item;
    }

    function appendStandaloneIosSheetRow(body, options) {
        options = options || {};

        var row = $('<div class="lm-ios-full-player__sheet-row"></div>');
        var main = $('<div class="lm-ios-full-player__sheet-row-main"></div>');
        var title = $('<div class="lm-ios-full-player__sheet-row-title"></div>').text(options.title || '');
        var subtitle = $('<div class="lm-ios-full-player__sheet-row-subtitle"></div>').text(options.subtitle || '');
        var trailing = $('<div class="lm-ios-full-player__sheet-row-trailing"></div>').text(options.trailing || '');

        row.toggleClass('active', !!options.active);
        row.toggleClass('disabled', !!options.disabled);
        main.append(title);
        if (options.subtitle) main.append(subtitle);
        row.append(main);
        if (options.trailing) row.append(trailing);

        if (options.onSelect) {
            row.on('click', function (event) {
                event.preventDefault();
                event.stopPropagation();
                if (row.hasClass('disabled')) return;
                options.onSelect(row);
            });
        }

        body.append(row);
        return row;
    }

    // операции с очередью из шита (идея из Spotube: player_queue_actions).
    // Мутируем синхронно ОБА представления очереди — standalone-массивы
    // (tracks+playlist параллельны!) и зеркало MUSIC_QUEUE — со сдвигом
    // currentIndex; shuffleOrder сбрасывается (пересоберётся лениво),
    // снапшот форсится. Текущий играющий трек не трогаем.
    function mutateStandaloneIosQueue(action, index) {
        var tracks = MUSIC_IOS_AUDIO.tracks || [];
        var playlist = MUSIC_IOS_AUDIO.playlist || [];
        var current = MUSIC_IOS_AUDIO.currentIndex;
        var fromRestore = false;

        // восстановленная после рестарта очередь может жить только в
        // MUSIC_QUEUE (standalone-массивы пустые, пока play не нажат) —
        // тогда правим её напрямую
        if (!tracks.length && hasRestoredQueueSnapshot()) {
            fromRestore = true;
            tracks = queueTracks();
            playlist = [];
            current = queueCurrentIndex();
        }

        if (index < 0 || index >= tracks.length || tracks.length < 2) return false;
        if (index === current) return false;

        var insertedAt = -1;

        function moveTo(target) {
            var track = tracks.splice(index, 1)[0];
            var playback = playlist.length > index ? playlist.splice(index, 1)[0] : null;

            if (index < current) current--;
            if (target > tracks.length) target = tracks.length;

            tracks.splice(target, 0, track);
            if (playback !== null) playlist.splice(target, 0, playback);

            if (target <= current) current++;
            insertedAt = target;
        }

        if (action === 'remove') {
            tracks.splice(index, 1);
            if (playlist.length > index) playlist.splice(index, 1);
            if (index < current) current--;
        } else if (action === 'next') {
            if (index === current + 1) {
                if (!isStandaloneIosShuffle()) return true; // уже следующий
                insertedAt = index; // физически на месте — поправим только shuffle-порядок
            } else {
                moveTo(index < current ? current : current + 1);
            }
        } else if (action === 'first') {
            if (index === 0) return true;
            moveTo(0);
        } else if (action === 'last') {
            if (index === tracks.length - 1) return true;
            moveTo(tracks.length);
        } else {
            return false;
        }

        if (!fromRestore) MUSIC_IOS_AUDIO.currentIndex = current;
        MUSIC_IOS_AUDIO.shuffleOrder = null;

        MUSIC_QUEUE.tracks = tracks.slice();
        MUSIC_QUEUE.currentIndex = current;
        MUSIC_QUEUE.currentTrackId = tracks[current] ? tracks[current].id : null;

        // при shuffle физический порядок на «следующий» не влияет — трек надо
        // ещё и вставить сразу после текущего в свежем shuffle-порядке, иначе
        // «Играть следующим» отдаст случайного соседа
        if (action === 'next' && insertedAt > -1 && isStandaloneIosShuffle()) {
            var order = standaloneIosOrder(); // лениво пересоберётся, текущий в начале
            if (order) {
                var movedPos = order.indexOf(insertedAt);
                if (movedPos > -1) order.splice(movedPos, 1);

                var currentPos = order.indexOf(current);
                order.splice((currentPos > -1 ? currentPos : 0) + 1, 0, insertedAt);
            }
        }

        scheduleQueueSnapshotSave(true);
        updateStandaloneIosPlayerBar();
        return true;
    }

    // вставка трека из ЛЮБОГО списка (альбом/плейлист/поиск/home) в играющую
    // standalone-очередь без прерывания воспроизведения (Spotify-стиль)
    function canEnqueueToStandaloneQueue(track) {
        if (!track || !track.id || !shouldUseStandaloneIosAudio()) return false;
        return isStandaloneIosAudioActive() || hasRestoredQueueSnapshot();
    }

    // позиция трека в живой standalone-очереди (учитывая restored-режим);
    // -1 = трека в очереди нет
    function standaloneQueueIndexOfTrack(trackId) {
        if (!trackId) return -1;

        var tracks = (MUSIC_IOS_AUDIO.tracks || []).length
            ? MUSIC_IOS_AUDIO.tracks
            : (hasRestoredQueueSnapshot() ? queueTracks() : []);

        for (var i = 0; i < tracks.length; i++) {
            if (tracks[i] && tracks[i].id === trackId) return i;
        }

        return -1;
    }

    function standaloneQueueCurrentIndexValue() {
        return (MUSIC_IOS_AUDIO.tracks || []).length
            ? MUSIC_IOS_AUDIO.currentIndex
            : queueCurrentIndex();
    }

    function enqueueTrackToStandaloneQueue(track, mode) {
        if (!canEnqueueToStandaloneQueue(track)) return false;

        var fromRestore = !(MUSIC_IOS_AUDIO.tracks || []).length && hasRestoredQueueSnapshot();
        var tracks = fromRestore ? queueTracks() : (MUSIC_IOS_AUDIO.tracks || []);
        var playlist = fromRestore ? [] : (MUSIC_IOS_AUDIO.playlist || []);
        var current = fromRestore ? queueCurrentIndex() : MUSIC_IOS_AUDIO.currentIndex;

        if (!tracks.length || current < 0) return false;

        // трек уже в очереди — двигаем существующий экземпляр, а не дублируем:
        // учёт текущего трека (updateQueueCurrent/queueCurrentIndex) ищет по id,
        // дубликаты сломали бы его
        var existing = standaloneQueueIndexOfTrack(track.id);

        if (existing > -1) {
            if (existing === current) return true; // уже играет
            return mutateStandaloneIosQueue(mode === 'next' ? 'next' : 'last', existing);
        }

        var target = mode === 'next' ? current + 1 : tracks.length;

        tracks.splice(target, 0, track);
        if (!fromRestore) playlist.splice(target, 0, buildPlayback(track));

        MUSIC_IOS_AUDIO.shuffleOrder = null;

        MUSIC_QUEUE.tracks = tracks.slice();
        MUSIC_QUEUE.currentIndex = current;
        MUSIC_QUEUE.currentTrackId = tracks[current] ? tracks[current].id : null;

        // тот же приём, что в mutateStandaloneIosQueue: при shuffle новый трек
        // надо ещё и поставить сразу после текущего в свежем shuffle-порядке
        if (mode === 'next' && isStandaloneIosShuffle()) {
            var order = standaloneIosOrder();
            if (order) {
                var movedPos = order.indexOf(target);
                if (movedPos > -1) order.splice(movedPos, 1);

                var currentPos = order.indexOf(current);
                order.splice((currentPos > -1 ? currentPos : 0) + 1, 0, target);
            }
        }

        scheduleQueueSnapshotSave(true);
        updateStandaloneIosPlayerBar();
        return true;
    }

    function openStandaloneIosQueueItemMenu(index) {
        var state = standaloneIosPlayerState();
        var track = state.tracks && state.tracks[index];
        if (!track) return;

        if (index === state.currentIndex) {
            Lampa.Noty.show('Этот трек сейчас играет.');
            return;
        }

        var player = ensureStandaloneIosFullPlayer();
        var body = showStandaloneIosSheet(track.title || 'Трек');

        player.attr('data-sheet-kind', 'queue-item');

        function apply(action) {
            mutateStandaloneIosQueue(action, index);
            openStandaloneIosQueueSheet();
        }

        appendStandaloneIosSheetRow(body, { title: 'Играть следующим', subtitle: track.artist_name || '', onSelect: function () { apply('next'); } });
        appendStandaloneIosSheetRow(body, { title: 'В начало очереди', onSelect: function () { apply('first'); } });
        appendStandaloneIosSheetRow(body, { title: 'В конец очереди', onSelect: function () { apply('last'); } });
        appendStandaloneIosSheetRow(body, { title: 'Убрать из очереди', onSelect: function () { apply('remove'); } });
    }

    function openStandaloneIosQueueSheet() {
        var state = standaloneIosPlayerState();
        if (!state.active || !state.track) {
            Lampa.Noty.show('Очередь пуста.');
            return;
        }

        var player = ensureStandaloneIosFullPlayer();
        var body = showStandaloneIosSheet('Очередь');

        body.addClass('lm-ios-full-player__sheet-body--queue');
        body.append('<div class="lm-ios-full-player__queue-summary"></div>');
        body.append('<div class="lm-ios-full-player__queue-list"></div>');

        player.attr('data-sheet-kind', 'queue');
        player.removeAttr('data-queue-key');
        player.removeAttr('data-current-index');
        player.attr('data-scroll-current', 'true');

        updateStandaloneIosFullQueue(player, state);
    }

    // --- шит «Источники»: матчи, pin/unpin, ручной поиск ---

    function renderSourceMatchRows(body, track, matches, selectedMatch, saveQuery) {
        matches.forEach(function (match) {
            var artists = Array.isArray(match.artists) ? match.artists.join(', ') : '';
            var isSelected = selectedMatch
                && selectedMatch.id === match.id
                && selectedMatch.provider_id === match.provider_id;

            appendStandaloneIosSheetRow(body, {
                title: match.title || formatMatchTitle(match) || 'Источник',
                subtitle: artists || match.provider_id || '',
                trailing: isSelected ? 'Выбран' : (match.duration_ms ? formatDuration(match.duration_ms) : ''),
                active: isSelected,
                onSelect: function (row) {
                    if (isSelected) {
                        closeStandaloneIosSheet();
                        return;
                    }

                    row.addClass('disabled');
                    saveTrackMatch(track, match, function (saved) {
                        if (!saved) {
                            row.removeClass('disabled');
                            Lampa.Noty.show('Не удалось выбрать источник.');
                            return;
                        }

                        Lampa.Noty.show('Источник сохранён.');
                        closeStandaloneIosSheet();
                        updateStandaloneIosPlayerBar();
                    }, saveQuery);
                }
            });
        });
    }

    function appendManualSourceSearchRow(body, track, initialValue) {
        appendStandaloneIosSheetRow(body, {
            title: 'Найти вручную…',
            subtitle: 'Поиск источника своим запросом',
            onSelect: function () {
                openStandaloneIosManualSourceSearch(track, initialValue);
            }
        });
    }

    // последний рубеж для битых метаданных: пользователь ищет источник
    // произвольным запросом, выбор пинуется той же механикой
    function openStandaloneIosManualSourceSearch(track, initialValue) {
        var prefill = typeof initialValue === 'string' && initialValue
            ? initialValue
            : (((track && track.artist_name) || '') + ' ' + ((track && track.title) || '')).trim();

        Lampa.Input.edit({
            value: prefill,
            title: 'Поиск источника',
            free: true,
            nosave: true,
            nomic: true
        }, function (queryValue) {
            queryValue = String(queryValue || '').trim();
            if (!queryValue) return;

            renderManualSourceResults(track, queryValue);
        });
    }

    function renderManualSourceResults(track, queryValue) {
        var player = ensureStandaloneIosFullPlayer();
        var body = showStandaloneIosSheet('Поиск источника');
        var trackId = (track && track.id) || '';

        player.attr('data-sheet-track-id', trackId);
        appendStandaloneIosSheetMessage(body, 'Ищу «' + queryValue + '»...', true);

        loadTrackMatches(track, function (json) {
            if (!MUSIC_IOS_FULL_PLAYER || player.attr('data-sheet-track-id') !== trackId) return;

            var matches = json && Array.isArray(json.matches) ? json.matches : [];
            var selectedMatch = json && json.selected_match ? json.selected_match : null;

            body.empty();

            appendStandaloneIosSheetRow(body, {
                title: 'Изменить запрос',
                subtitle: '«' + queryValue + '»',
                onSelect: function () {
                    openStandaloneIosManualSourceSearch(track, queryValue);
                }
            });

            if (!matches.length) {
                appendStandaloneIosSheetMessage(body, 'Ничего не найдено. Попробуй другой запрос.');
                return;
            }

            renderSourceMatchRows(body, track, matches, selectedMatch, queryValue);
        }, queryValue);
    }

    function openStandaloneIosSourcesSheet() {
        var state = standaloneIosPlayerState();
        if (!state.active || !state.track) {
            Lampa.Noty.show('Трек не выбран.');
            return;
        }

        var player = ensureStandaloneIosFullPlayer();
        var body = showStandaloneIosSheet('Источники');
        var trackId = state.track.id || '';

        player.attr('data-sheet-track-id', trackId);
        appendStandaloneIosSheetMessage(body, 'Загружаю источники...', true);

        loadTrackMatches(state.track, function (json) {
            if (!MUSIC_IOS_FULL_PLAYER || MUSIC_IOS_FULL_PLAYER.attr('data-sheet-track-id') !== trackId) return;

            var matches = json && Array.isArray(json.matches) ? json.matches : [];
            var selectedMatch = json && json.selected_match ? json.selected_match : null;

            body.empty();

            if (!matches.length) {
                appendStandaloneIosSheetMessage(body, 'Альтернативные источники не найдены.');
                appendManualSourceSearchRow(body, state.track);
                return;
            }

            if (selectedMatch && selectedMatch.pinned) {
                appendStandaloneIosSheetRow(body, {
                    title: 'Сбросить выбор',
                    subtitle: 'Вернуть автоматический подбор источника',
                    onSelect: function (row) {
                        row.addClass('disabled');
                        resetTrackMatch(state.track, function (reset) {
                            if (!reset) {
                                row.removeClass('disabled');
                                Lampa.Noty.show('Не удалось сбросить выбор.');
                                return;
                            }

                            Lampa.Noty.show('Источник подберётся автоматически.');
                            closeStandaloneIosSheet();
                            updateStandaloneIosPlayerBar();
                        });
                    }
                });
            }

            renderSourceMatchRows(body, state.track, matches, selectedMatch, null);
            appendManualSourceSearchRow(body, state.track);
        });
    }

    var MUSIC_LYRICS_CACHE = {};

    // --- лирика: шит с построчной подсветкой ---

    function buildLyricsCacheKey(id, title, artist, album, durationMs, youtubeId) {
        var bucket = durationMs ? Math.max(0, Math.floor(Number(durationMs) / 10000)) : 0;

        return [
            id || '',
            title || '',
            artist || '',
            album || '',
            bucket,
            youtubeId || ''
        ].join('::');
    }

    function buildLyricsUrl(title, artist, album, durationMs, youtubeId) {
        return MUSIC.endpoints.lyrics + '?title=' + encodeURIComponent(title || '')
            + '&artist_name=' + encodeURIComponent(artist || '')
            + '&album_title=' + encodeURIComponent(album || '')
            + (durationMs ? '&duration_ms=' + encodeURIComponent(durationMs) : '')
            + (youtubeId ? '&youtube_id=' + encodeURIComponent(youtubeId) : '');
    }

    function buildLyricsLineMeta(root, selector) {
        var meta = {
            times: [],
            elements: [],
            activeIndex: -1,
            activeElement: null
        };

        root.find(selector).each(function () {
            meta.times.push(Number($(this).attr('data-time') || 0));
            meta.elements.push(this);
        });

        return meta;
    }

    function findLyricsLineIndex(times, timeMs) {
        var left = 0;
        var right = times.length - 1;
        var index = -1;

        while (left <= right) {
            var middle = (left + right) >> 1;

            if (times[middle] <= timeMs) {
                index = middle;
                left = middle + 1;
            } else {
                right = middle - 1;
            }
        }

        return index;
    }

    function activateLyricsLine(meta, activeIndex) {
        if (!meta) return null;

        if (meta.activeElement && meta.activeIndex !== activeIndex)
            $(meta.activeElement).removeClass('active');

        meta.activeIndex = activeIndex;
        meta.activeElement = activeIndex >= 0 ? meta.elements[activeIndex] : null;

        if (meta.activeElement)
            $(meta.activeElement).addClass('active');

        return meta.activeElement;
    }

    function openStandaloneIosLyricsSheet() {
        var state = standaloneIosPlayerState();
        if (!state.active || !state.track) {
            Lampa.Noty.show('Трек не выбран.');
            return;
        }

        var player = ensureStandaloneIosFullPlayer();
        var body = showStandaloneIosSheet('Текст');
        var track = state.track;
        var playback = Array.isArray(MUSIC_IOS_AUDIO.playlist) ? MUSIC_IOS_AUDIO.playlist[state.currentIndex] : null;
        var title = track.title || (playback && playback.title) || '';
        var artist = track.artist_name || (playback && playback.artist) || '';
        var album = track.album_title || (playback && playback.music_album_title) || '';
        var durationMs = track.duration_ms || (playback && playback.music_duration_ms) || 0;
        var youtubeId = extractYouTubeTrackId(track) || (playback && playback.music_youtube_id) || '';
        var trackId = buildLyricsCacheKey(track.id || '', title, artist, album, durationMs, youtubeId);

        player.attr('data-sheet-kind', 'lyrics');
        player.attr('data-sheet-track-id', trackId);
        player.removeAttr('data-lyrics-line');

        var render = function (json) {
            if (!MUSIC_IOS_FULL_PLAYER || player.attr('data-sheet-track-id') !== trackId) return;
            if (player.attr('data-sheet-kind') !== 'lyrics') return;

            body.empty();
            player.removeData('lyricsLineMeta');

            var hasLines = json && json.available && Array.isArray(json.lines) && json.lines.length;
            var hasPlain = json && json.available && json.plain;

            if (!hasLines && !hasPlain) {
                appendStandaloneIosSheetMessage(body, json && json.retry ? 'Сервис текстов не ответил. Открой ещё раз.' : 'Текст не найден.');
                return;
            }

            if (json.synced && hasLines) {
                var list = $('<div class="lm-ios-full-player__lyrics"></div>');

                json.lines.forEach(function (line, index) {
                    var row = $('<div class="lm-ios-full-player__lyrics-line"></div>');

                    row.attr('data-line', index);
                    row.attr('data-time', line.time_ms || 0);

                    if (line.text) row.text(line.text);
                    else row.addClass('lm-ios-full-player__lyrics-line--empty').text('♪');

                    list.append(row);
                });

                body.append(list);
                player.data('lyricsLineMeta', buildLyricsLineMeta(body, '.lm-ios-full-player__lyrics-line'));
                updateStandaloneIosLyricsHighlight(true);
            } else {
                var plain = $('<div class="lm-ios-full-player__lyrics lm-ios-full-player__lyrics--plain"></div>');
                plain.text(json.plain || json.lines.map(function (line) { return line.text || ''; }).join('\n'));
                body.append(plain);
            }
        };

        if (MUSIC_LYRICS_CACHE[trackId]) {
            render(MUSIC_LYRICS_CACHE[trackId]);
            return;
        }

        appendStandaloneIosSheetMessage(body, 'Ищу текст...', true);

        request(buildLyricsUrl(title, artist, album, durationMs, youtubeId), function (json) {
            json = parseJson(json);
            // неудачи не кэшируем: повторное открытие шита должно попробовать снова
            if (json && json.available) setCappedCacheEntry(MUSIC_LYRICS_CACHE, trackId, json, 60);
            render(json);
        }, function () {
            render(null);
        });
    }

    function updateStandaloneIosLyricsHighlight(force) {
        var heatStartedAt = musicHeatNow();
        var player = MUSIC_IOS_FULL_PLAYER;
        if (!player || !player.length || player.attr('data-sheet-kind') !== 'lyrics') return;
        bumpMusicHeatMetric('fullPlayerLyricsHighlight');

        var audio = MUSIC_IOS_AUDIO.audio;
        if (!audio || !isFinite(audio.currentTime)) {
            bumpMusicHeatDuration('fullPlayerLyricsHighlight', heatStartedAt);
            return;
        }

        var meta = player.data('lyricsLineMeta');
        if (!meta || !meta.elements || !meta.elements.length) {
            meta = buildLyricsLineMeta(player, '.lm-ios-full-player__lyrics-line');
            player.data('lyricsLineMeta', meta);
        }

        if (!meta.elements.length) {
            bumpMusicHeatDuration('fullPlayerLyricsHighlight', heatStartedAt);
            return;
        }

        var timeMs = audio.currentTime * 1000;
        var activeIndex = findLyricsLineIndex(meta.times, timeMs);

        if (!force && player.attr('data-lyrics-line') === String(activeIndex)) return;
        player.attr('data-lyrics-line', String(activeIndex));

        var element = activateLyricsLine(meta, activeIndex);
        if (!element) {
            bumpMusicHeatDuration('fullPlayerLyricsHighlight', heatStartedAt);
            return;
        }

        // пока пользователь листает текст руками, автоскролл не дёргаем
        var manualAt = Number(player.attr('data-lyrics-manual') || 0);
        if (!force && Date.now() - manualAt < 4000) {
            bumpMusicHeatDuration('fullPlayerLyricsHighlight', heatStartedAt);
            return;
        }

        if (element && typeof element.scrollIntoView === 'function') {
            try {
                element.scrollIntoView({ block: 'center', behavior: force ? 'auto' : 'smooth' });
            } catch (e) {
                element.scrollIntoView();
            }
            bumpMusicHeatMetric('fullPlayerLyricsScroll');
        }

        bumpMusicHeatDuration('fullPlayerLyricsHighlight', heatStartedAt);
    }

    function toggleStandaloneIosCurrentBookmark() {
        var state = standaloneIosPlayerState();
        if (!state.active || !state.track || !state.track.id) {
            Lampa.Noty.show('Трек не выбран.');
            return;
        }

        var added = toggleBookmarkedEntity(MUSIC.storage.bookmarked_tracks, state.track);
        Lampa.Noty.show(added ? 'Трек добавлен в закладки.' : 'Трек удалён из закладок.');
        updateStandaloneIosFullPlayer();
    }

    // --- sleep-таймер ---

    function formatStandaloneIosTimerLeft() {
        if (!MUSIC_IOS_SLEEP_TIMER.endAt) return '';

        var left = Math.max(0, Math.ceil((MUSIC_IOS_SLEEP_TIMER.endAt - Date.now()) / 1000));
        var minutes = Math.ceil(left / 60);

        if (minutes >= 60) {
            var hours = Math.floor(minutes / 60);
            var rest = minutes % 60;
            return rest ? (hours + ' ч ' + rest + ' мин') : (hours + ' ч');
        }

        return minutes + ' мин';
    }

    function clearStandaloneIosSleepTimer(showNotice) {
        if (MUSIC_IOS_SLEEP_TIMER.timer) {
            clearTimeout(MUSIC_IOS_SLEEP_TIMER.timer);
            MUSIC_IOS_SLEEP_TIMER.timer = 0;
        }

        MUSIC_IOS_SLEEP_TIMER.endAt = 0;
        updateStandaloneIosFullPlayer();

        if (showNotice) Lampa.Noty.show('Таймер сна отключён.');
    }

    function expireStandaloneIosSleepTimer(origin, force) {
        if (!MUSIC_IOS_SLEEP_TIMER.endAt) return false;
        if (!force && Date.now() < MUSIC_IOS_SLEEP_TIMER.endAt) return false;

        if (MUSIC_IOS_SLEEP_TIMER.timer) {
            clearTimeout(MUSIC_IOS_SLEEP_TIMER.timer);
            MUSIC_IOS_SLEEP_TIMER.timer = 0;
        }

        MUSIC_IOS_SLEEP_TIMER.endAt = 0;

        var audio = MUSIC_IOS_AUDIO.audio;

        if (audio && !audio.paused) {
            try {
                audio.pause();
                startStandaloneIosKeepAlive('sleep-timer-' + (origin || 'expire'));
            } catch (e) {}
        }

        updateStandaloneIosPlaybackState();
        updateStandaloneIosPlayerBar();
        Lampa.Noty.show('Таймер сна: музыка поставлена на паузу.');
        return true;
    }

    function setStandaloneIosSleepTimer(minutes) {
        clearStandaloneIosSleepTimer(false);

        minutes = Math.max(1, Number(minutes || 0));
        MUSIC_IOS_SLEEP_TIMER.endAt = Date.now() + minutes * 60 * 1000;
        MUSIC_IOS_SLEEP_TIMER.timer = setTimeout(function () {
            expireStandaloneIosSleepTimer('timeout', true);
        }, minutes * 60 * 1000);

        updateStandaloneIosFullPlayer();
        Lampa.Noty.show('Таймер сна: ' + minutes + ' мин.');
    }

    function openStandaloneIosTimerSheet() {
        var body = showStandaloneIosSheet('Таймер сна');
        var options = [
            { title: '15 минут', minutes: 15 },
            { title: '30 минут', minutes: 30 },
            { title: '1 час', minutes: 60 },
            { title: '2 часа', minutes: 120 }
        ];

        if (MUSIC_IOS_SLEEP_TIMER.endAt)
            appendStandaloneIosSheetMessage(body, 'Активен: ' + formatStandaloneIosTimerLeft());

        options.forEach(function (item) {
            appendStandaloneIosSheetRow(body, {
                title: item.title,
                subtitle: 'Поставить музыку на паузу',
                onSelect: function () {
                    setStandaloneIosSleepTimer(item.minutes);
                    closeStandaloneIosSheet();
                }
            });
        });

        if (MUSIC_IOS_SLEEP_TIMER.endAt) {
            appendStandaloneIosSheetRow(body, {
                title: 'Отключить таймер',
                subtitle: 'Музыка продолжит играть',
                onSelect: function () {
                    clearStandaloneIosSleepTimer(true);
                    closeStandaloneIosSheet();
                }
            });
        }
    }

    // --- DOM фулл-плеера и его обновление ---

    function updateStandaloneIosFullActions(player, state) {
        var bookmarked = !!(state.track && state.track.id && isBookmarkedEntity(MUSIC.storage.bookmarked_tracks, state.track.id));
        var timerActive = !!MUSIC_IOS_SLEEP_TIMER.endAt;
        var bookmark = player.find('[data-action="bookmark"]');
        var timerStatus = player.find('.lm-ios-full-player__timer-status');

        bookmark.toggleClass('active', bookmarked);
        setTextIfChanged(bookmark.find('span'), bookmarked ? 'В закладках' : 'В закладки');
        player.find('[data-action="radio"]').toggleClass('active', isRadioAutoplayEnabled());
        player.find('[data-action="timer"]').toggleClass('active', timerActive);
        timerStatus.toggleClass('lm-ios-full-player__timer-status--visible', timerActive);
        setTextIfChanged(timerStatus, timerActive ? ('Таймер сна: ' + formatStandaloneIosTimerLeft()) : '');
    }

    function ensureStandaloneIosFullPlayer() {
        if (MUSIC_IOS_FULL_PLAYER && MUSIC_IOS_FULL_PLAYER.length) return MUSIC_IOS_FULL_PLAYER;

        var player = $(
            '<div class="lm-ios-full-player">'
            + '<div class="lm-ios-full-player__backdrop"></div>'
            + '<div class="lm-ios-full-player__shell">'
            + '<div class="lm-ios-full-player__grabber"></div>'
            + '<div class="lm-ios-full-player__head">'
            + '<div class="lm-ios-full-player__tool" data-action="collapse">' + IOS_PLAYER_DOWN_ICON + '</div>'
            + '<div class="lm-ios-full-player__head-title">Сейчас играет</div>'
            + '<div class="lm-ios-full-player__tool" data-action="collapse">' + IOS_PLAYER_CLOSE_ICON + '</div>'
            + '</div>'
            + '<div class="lm-ios-full-player__hero">'
            + '<div class="lm-ios-full-player__art"><img src="' + IMG_BG + '" alt=""></div>'
            + '<div class="lm-ios-full-player__title">Музыка</div>'
            + '<div class="lm-ios-full-player__artist"></div>'
            + '<div class="lm-ios-full-player__progress">'
            + '<input class="lm-ios-full-player__seek" type="range" min="0" max="1000" step="1" value="0">'
            + '<div class="lm-ios-full-player__times">'
            + '<div class="lm-ios-full-player__time lm-ios-full-player__time--current">0:00</div>'
            + '<div class="lm-ios-full-player__time lm-ios-full-player__time--total">0:00</div>'
            + '</div>'
            + '</div>'
            + '<div class="lm-ios-full-player__actions">'
            + '<div class="lm-ios-full-player__btn lm-ios-full-player__btn--mini" data-action="shuffle">' + IOS_PLAYER_SHUFFLE_ICON + '</div>'
            + '<div class="lm-ios-full-player__btn lm-ios-full-player__btn--ghost" data-action="prev">' + IOS_PLAYER_PREV_ICON + '</div>'
            + '<div class="lm-ios-full-player__btn lm-ios-full-player__btn--primary" data-action="playpause">' + IOS_PLAYER_PAUSE_ICON + '</div>'
            + '<div class="lm-ios-full-player__btn lm-ios-full-player__btn--ghost" data-action="next">' + IOS_PLAYER_NEXT_ICON + '</div>'
            + '<div class="lm-ios-full-player__btn lm-ios-full-player__btn--mini" data-action="repeat">' + IOS_PLAYER_REPEAT_ICON + '</div>'
            + '</div>'
            + '<div class="lm-ios-full-player__quick-actions">'
            + '<div class="lm-ios-full-player__quick" data-action="queue">' + IOS_PLAYER_QUEUE_ICON + '<span>Очередь</span></div>'
            + '<div class="lm-ios-full-player__quick" data-action="sources">' + IOS_PLAYER_SOURCE_ICON + '<span>Источники</span></div>'
            + '<div class="lm-ios-full-player__quick" data-action="lyrics">' + IOS_PLAYER_LYRICS_ICON + '<span>Текст</span></div>'
            + '<div class="lm-ios-full-player__quick" data-action="bookmark">' + BOOKMARK_ICON + '<span>В закладки</span></div>'
            + '<div class="lm-ios-full-player__quick" data-action="timer">' + IOS_PLAYER_TIMER_ICON + '<span>Таймер</span></div>'
            + '<div class="lm-ios-full-player__quick" data-action="radio">' + IOS_PLAYER_RADIO_ICON + '<span>Подборка</span></div>'
            + '</div>'
            + '<div class="lm-ios-full-player__timer-status"></div>'
            + '<div class="lm-ios-full-player__stop" data-action="stop">Остановить</div>'
            + '</div>'
            + '<div class="lm-ios-full-player__sheet">'
            + '<div class="lm-ios-full-player__sheet-panel">'
            + '<div class="lm-ios-full-player__sheet-head">'
            + '<div class="lm-ios-full-player__sheet-title"></div>'
            + '<div class="lm-ios-full-player__sheet-close" data-action="sheet-close">' + IOS_PLAYER_CLOSE_ICON + '</div>'
            + '</div>'
            + '<div class="lm-ios-full-player__sheet-body"></div>'
            + '</div>'
            + '</div>'
            + '</div>'
        );

        player.on('click', '[data-action]', function (event) {
            event.preventDefault();
            event.stopPropagation();
            handleStandaloneIosPlayerAction($(this).attr('data-action'));
        });

        player.on('input', '.lm-ios-full-player__seek', function () {
            var state = standaloneIosPlayerState();
            var value = Number($(this).val() || 0);
            var position = state.duration ? (state.duration * value / 1000) : 0;

            player.attr('data-seeking', 'true');
            this.style.setProperty('--lm-seek-fill', Math.max(0, Math.min(100, value / 10)) + '%');
            player.find('.lm-ios-full-player__time--current').text(Lampa.Utils.secondsToTime(position));
        });

        player.on('change', '.lm-ios-full-player__seek', function () {
            player.removeAttr('data-seeking');
            seekStandaloneIosByRangeValue($(this).val());
        });

        // лонг-тап по треку очереди → меню действий (убрать/переставить);
        // touchmove отменяет таймер, чтобы скролл списка не открывал меню
        var queueItemHold = { timer: 0, fired: false };

        function clearQueueItemHoldTimer() {
            if (queueItemHold.timer) {
                clearTimeout(queueItemHold.timer);
                queueItemHold.timer = 0;
            }
        }

        player.on('touchstart', '.lm-ios-full-player__queue-item', function () {
            var index = Number($(this).attr('data-index') || 0);

            queueItemHold.fired = false;
            clearQueueItemHoldTimer();
            queueItemHold.timer = setTimeout(function () {
                queueItemHold.timer = 0;
                queueItemHold.fired = true;
                openStandaloneIosQueueItemMenu(index);
            }, 550);
        });

        player.on('touchmove touchend touchcancel', '.lm-ios-full-player__queue-item', function () {
            clearQueueItemHoldTimer();
        });

        player.on('contextmenu', '.lm-ios-full-player__queue-item', function (event) {
            event.preventDefault();
            queueItemHold.fired = true;
            openStandaloneIosQueueItemMenu(Number($(this).attr('data-index') || 0));
        });

        player.on('click', '.lm-ios-full-player__queue-item', function (event) {
            event.preventDefault();
            event.stopPropagation();

            if (queueItemHold.fired) {
                queueItemHold.fired = false;
                return;
            }

            standaloneIosPlayIndex(Number($(this).attr('data-index') || 0));
        });

        player.on('click', '.lm-ios-full-player__sheet', function (event) {
            if (event.target === this) closeStandaloneIosSheet();
        });

        player.on('click', '.lm-ios-full-player__lyrics-line', function (event) {
            event.preventDefault();
            event.stopPropagation();

            var audio = MUSIC_IOS_AUDIO.audio;
            var time = Number($(this).attr('data-time') || 0);

            if (!audio || !isFinite(time)) return;

            try {
                audio.currentTime = time / 1000;
            } catch (e) {}

            updateStandaloneIosPositionState(true);
            updateStandaloneIosLyricsHighlight(true);
        });

        player.on('touchstart', '.lm-ios-full-player__lyrics', function () {
            player.attr('data-lyrics-manual', String(Date.now()));
        });

        bindStandaloneIosFullPlayerGestures(player);

        $(document).on('keydown.lampacMusicFullPlayer', handleStandaloneIosFullPlayerBack);

        $('body').append(player);
        MUSIC_IOS_FULL_PLAYER = player;
        return MUSIC_IOS_FULL_PLAYER;
    }

    function updateStandaloneIosFullQueue(player, state) {
        var heatStartedAt = musicHeatNow();
        var tracks = state.tracks || [];
        var ids = tracks.map(function (track) { return track && track.id ? track.id : ''; }).join('|');
        var queueKey = ids;
        var list = player.find('.lm-ios-full-player__queue-list');

        if (!list.length) return;
        bumpMusicHeatMetric('fullPlayerQueueUpdate');

        var now = Date.now();
        var currentIndexText = String(state.currentIndex);
        var queueAlreadySynced = player.attr('data-queue-key') === queueKey
            && player.attr('data-queue-duration-key')
            && player.attr('data-current-index') === currentIndexText
            && player.attr('data-scroll-current') !== 'true';
        var throttleKey = queueKey + '|' + currentIndexText;
        var lastThrottleKey = player.data('queueThrottleKey') || '';
        var lastThrottleAt = Number(player.data('queueThrottleAt') || 0);

        if (queueAlreadySynced && lastThrottleKey === throttleKey && now - lastThrottleAt < 900) {
            bumpMusicHeatMetric('fullPlayerQueueUpdateSkipped');
            bumpMusicHeatDuration('fullPlayerQueueUpdate', heatStartedAt);
            return;
        }

        player.data('queueThrottleKey', throttleKey);
        player.data('queueThrottleAt', now);

        player.find('.lm-ios-full-player__queue-summary').text(tracks.length ? ((state.currentIndex + 1) + ' из ' + tracks.length) : 'Очередь пуста');

        if (player.attr('data-queue-key') !== queueKey) {
            var renderStartedAt = musicHeatNow();
            list.empty();
            player.attr('data-queue-key', queueKey);
            player.removeAttr('data-queue-duration-key');

            // граница ручной очереди и автоподборки — один спокойный
            // разделитель перед первым auto_radio-треком, без пометок в строках
            var radioDividerAdded = false;

            tracks.forEach(function (track, index) {
                if (!radioDividerAdded && track && track.auto_radio) {
                    radioDividerAdded = true;
                    list.append($('<div class="lm-ios-full-player__queue-divider"></div>').text('Дальше автоподборка'));
                }

                var item = $('<div class="lm-ios-full-player__queue-item"></div>');
                var imageWrap = $('<div class="lm-ios-full-player__queue-img"></div>');
                var img = $('<img src="" alt="">');
                var body = $('<div class="lm-ios-full-player__queue-body"></div>');
                var title = $('<div class="lm-ios-full-player__queue-track"></div>').text(track && track.title ? track.title : 'Track');
                var artistText = track && track.artist_name ? track.artist_name : '';
                var artist = $('<div class="lm-ios-full-player__queue-artist"></div>').text(artistText);
                var duration = track && track.duration_ms ? Math.max(0, Math.round(track.duration_ms / 1000)) : 0;
                var time = $('<div class="lm-ios-full-player__queue-time"></div>').text(duration ? Lampa.Utils.secondsToTime(duration) : '');

                item.attr('data-index', index);
                img.attr('loading', 'lazy');
                img.attr('decoding', 'async');
                img.attr('src', trackImage(track) || IMG_BG);
                imageWrap.append(img);
                body.append(title);
                body.append(artist);
                item.append(imageWrap);
                item.append(body);
                item.append(time);
                list.append(item);
            });
            bumpMusicHeatMetric('fullPlayerQueueRender');
            bumpMusicHeatMetric('fullPlayerQueueRenderItems', tracks.length);
            bumpMusicHeatDuration('fullPlayerQueueRender', renderStartedAt);
        }

        var durationKey = tracks.map(function (track) {
            return track && track.duration_ms ? String(track.duration_ms) : '';
        }).join('|');

        if (player.attr('data-queue-duration-key') !== durationKey) {
            bumpMusicHeatMetric('fullPlayerQueueDurationRefresh');
            tracks.forEach(function (track, index) {
                var duration = track && track.duration_ms ? Math.max(0, Math.round(track.duration_ms / 1000)) : 0;
                var text = duration ? Lampa.Utils.secondsToTime(duration) : '';
                var time = list.find('.lm-ios-full-player__queue-item[data-index="' + index + '"] .lm-ios-full-player__queue-time');

                setTextIfChanged(time, text);
            });

            player.attr('data-queue-duration-key', durationKey);
        }

        if (player.attr('data-current-index') !== currentIndexText) {
            list.find('.lm-ios-full-player__queue-item--current').removeClass('lm-ios-full-player__queue-item--current');
            list.find('.lm-ios-full-player__queue-item[data-index="' + state.currentIndex + '"]').addClass('lm-ios-full-player__queue-item--current');
            player.attr('data-current-index', currentIndexText);
            bumpMusicHeatMetric('fullPlayerQueueCurrentSync');
        }

        if (player.attr('data-scroll-current') === 'true') {
            var scroller = list.get(0);
            var current = list.find('.lm-ios-full-player__queue-item[data-index="' + state.currentIndex + '"]').get(0);

            player.removeAttr('data-scroll-current');

            if (scroller && current) {
                try {
                    current.scrollIntoView({ block: 'center' });
                } catch (e) {
                    scroller.scrollTop = Math.max(0, current.offsetTop - (scroller.clientHeight / 2) + (current.clientHeight / 2));
                }
            }
        }

        bumpMusicHeatDuration('fullPlayerQueueUpdate', heatStartedAt);
    }

    function updateStandaloneIosFullPlayer() {
        var heatStartedAt = musicHeatNow();
        if (!MUSIC_IOS_FULL_PLAYER || !MUSIC_IOS_FULL_PLAYER.length) return;

        var state = standaloneIosPlayerState();
        var player = MUSIC_IOS_FULL_PLAYER;

        if (!MUSIC_IOS_FULL_PLAYER_OPEN && !player.hasClass('lm-ios-full-player--visible')) return;
        bumpMusicHeatMetric('fullPlayerUpdate');

        if (!state.active || !state.track) {
            closeStandaloneIosFullPlayer();
            player.removeAttr('data-queue-key');
            bumpMusicHeatDuration('fullPlayerUpdate', heatStartedAt);
            return;
        }

        var image = trackImage(state.track) || IMG_BG;
        var title = state.track.title || 'Track';
        var artist = state.track.artist_name || '';
        var trackKey = [state.track.id || '', image, title, artist].join('|');
        var isSeeking = player.attr('data-seeking') === 'true';

        if (MUSIC_IOS_AUDIO.fullUiKey !== trackKey) {
            var safeImage = String(image || '').replace(/"/g, '\\"');

            player.find('.lm-ios-full-player__backdrop').css('background-image', 'url("' + safeImage + '")');
            player.find('.lm-ios-full-player__art img').attr('src', image);
            setTextIfChanged(player.find('.lm-ios-full-player__title'), title);
            setTextIfChanged(player.find('.lm-ios-full-player__artist'), artist);
            MUSIC_IOS_AUDIO.fullUiKey = trackKey;
            bumpMusicHeatMetric('fullPlayerTrackUi');
        }

        var repeatMode = getStandaloneIosRepeatMode();
        var playbackKey = [
            state.playing ? '1' : '0',
            state.hasPrev ? '1' : '0',
            state.hasNext ? '1' : '0',
            isStandaloneIosShuffle() ? '1' : '0',
            repeatMode,
            state.duration || 0,
            state.currentIndex,
            state.tracks.length,
            state.canQueue ? '1' : '0'
        ].join('|');

        if (MUSIC_IOS_AUDIO.fullPlaybackKey !== playbackKey) {
            player.find('[data-action="playpause"]').html(state.playing ? IOS_PLAYER_PAUSE_ICON : IOS_PLAYER_PLAY_ICON);
            player.toggleClass('lm-ios-full-player--paused', !state.playing);
            player.find('[data-action="shuffle"]').toggleClass('active', isStandaloneIosShuffle());
            player.attr('data-repeat-mode', repeatMode);
            player.find('[data-action="repeat"]')
                .html(repeatMode === 'one' ? IOS_PLAYER_REPEAT_ONE_ICON : IOS_PLAYER_REPEAT_ICON)
                .toggleClass('active', repeatMode !== 'off');
            player.find('[data-action="prev"]').toggleClass('disabled', !state.hasPrev);
            player.find('[data-action="next"]').toggleClass('disabled', !state.hasNext);
            setTextIfChanged(player.find('.lm-ios-full-player__time--total'), Lampa.Utils.secondsToTime(state.duration || 0));
            setTextIfChanged(player.find('[data-action="queue"] span'), state.tracks.length > 1 ? ('Очередь ' + (state.currentIndex + 1) + '/' + state.tracks.length) : 'Очередь');
            MUSIC_IOS_AUDIO.fullPlaybackKey = playbackKey;
            bumpMusicHeatMetric('fullPlayerPlaybackUi');
        }

        if (player.attr('data-tint-url') !== image) {
            player.attr('data-tint-url', image);
            bumpMusicHeatMetric('fullPlayerTintRequest');
            computeStandaloneIosArtTint(image, function (tint) {
                if (!MUSIC_IOS_FULL_PLAYER || player.attr('data-tint-url') !== image) return;

                try {
                    player.get(0).style.setProperty('--lm-full-tint', tint || '');
                } catch (e) {}
            });
        }

        updateStandaloneIosFullActions(player, state);
        bumpMusicHeatMetric('fullPlayerActionsUpdate');

        if (!isSeeking) {
            var seekValue = state.duration > 0 ? Math.max(0, Math.min(1000, Math.round((state.current / state.duration) * 1000))) : 0;
            var currentText = Lampa.Utils.secondsToTime(state.current || 0);
            var progressKey = currentText + '|' + seekValue;

            if (MUSIC_IOS_AUDIO.fullProgressKey !== progressKey) {
                var seekInput = player.find('.lm-ios-full-player__seek');

                setTextIfChanged(player.find('.lm-ios-full-player__time--current'), currentText);
                seekInput.val(seekValue);
                if (seekInput.get(0)) seekInput.get(0).style.setProperty('--lm-seek-fill', (seekValue / 10) + '%');
                MUSIC_IOS_AUDIO.fullProgressKey = progressKey;
                bumpMusicHeatMetric('fullPlayerProgressUi');
            }
        }

        if (player.attr('data-sheet-kind') === 'queue') updateStandaloneIosFullQueue(player, state);
        if (player.attr('data-sheet-kind') === 'lyrics') updateStandaloneIosLyricsHighlight(false);
        bumpMusicHeatDuration('fullPlayerUpdate', heatStartedAt);
    }

    // --- мини-бар плеера ---

    function ensureStandaloneIosPlayerBar() {
        if (MUSIC_IOS_BAR && MUSIC_IOS_BAR.length) return MUSIC_IOS_BAR;

        var bar = $(
            '<div class="lm-ios-player">'
            + '<div class="lm-ios-player__top">'
            + '<div class="lm-ios-player__meta" data-action="expand">'
            + '<div class="lm-ios-player__title">Музыка</div>'
            + '<div class="lm-ios-player__artist"></div>'
            + '</div>'
            + '<div class="lm-ios-player__tools">'
            + '<div class="lm-ios-player__queue" data-action="queue">' + IOS_PLAYER_QUEUE_ICON + '<span>Сейчас играет</span></div>'
            + '<div class="lm-ios-player__toolbtn lm-ios-player__toolbtn--close" data-action="stop">' + IOS_PLAYER_CLOSE_ICON + '</div>'
            + '</div>'
            + '</div>'
            + '<div class="lm-ios-player__progress">'
            + '<div class="lm-ios-player__time lm-ios-player__time--current">0:00</div>'
            + '<input class="lm-ios-player__seek" type="range" min="0" max="1000" step="1" value="0">'
            + '<div class="lm-ios-player__time lm-ios-player__time--total">0:00</div>'
            + '</div>'
            + '<div class="lm-ios-player__actions">'
            + '<div class="lm-ios-player__btn lm-ios-player__btn--ghost" data-action="prev">' + IOS_PLAYER_PREV_ICON + '</div>'
            + '<div class="lm-ios-player__btn lm-ios-player__btn--primary" data-action="playpause">' + IOS_PLAYER_PAUSE_ICON + '</div>'
            + '<div class="lm-ios-player__btn lm-ios-player__btn--ghost" data-action="next">' + IOS_PLAYER_NEXT_ICON + '</div>'
            + '</div>'
            + '</div>'
        );

        bar.on('click', '[data-action]', function (event) {
            event.preventDefault();
            event.stopPropagation();
            handleStandaloneIosPlayerAction($(this).attr('data-action'));
        });

        bar.on('hover:enter', '[data-action]', function () {
            handleStandaloneIosPlayerAction($(this).attr('data-action'));
        });

        bar.on('input', '.lm-ios-player__seek', function () {
            var state = standaloneIosPlayerState();
            var value = Number($(this).val() || 0);
            var position = state.duration ? (state.duration * value / 1000) : 0;

            bar.attr('data-seeking', 'true');
            bar.find('.lm-ios-player__time--current').text(Lampa.Utils.secondsToTime(position));
        });

        bar.on('change', '.lm-ios-player__seek', function () {
            bar.removeAttr('data-seeking');
            seekStandaloneIosByRangeValue($(this).val());
        });

        $('body').append(bar);
        MUSIC_IOS_BAR = bar;
        return MUSIC_IOS_BAR;
    }

    function updateStandaloneIosPlayerBar() {
        var bar = ensureStandaloneIosPlayerBar();
        var state = standaloneIosPlayerState();
        var isSeeking = bar.attr('data-seeking') === 'true';

        if (!bar || !bar.length) return;

        if (!state.active || !state.track) {
            bar.removeAttr('data-seeking');
            MUSIC_IOS_AUDIO.barUiKey = '';
            MUSIC_IOS_AUDIO.barProgressKey = '';
            MUSIC_IOS_AUDIO.fullUiKey = '';
            MUSIC_IOS_AUDIO.fullPlaybackKey = '';
            MUSIC_IOS_AUDIO.fullProgressKey = '';
            bar.removeClass('lm-ios-player--visible');
            updateStandaloneIosFullPlayer();
            return;
        }

        var title = state.track.title || 'Track';
        var artist = state.track.artist_name || '';
        var uiKey = [
            state.track.id || '',
            title,
            artist,
            state.playing ? '1' : '0',
            state.hasPrev ? '1' : '0',
            state.hasNext ? '1' : '0',
            state.canQueue ? '1' : '0',
            state.duration || 0
        ].join('|');

        if (MUSIC_IOS_AUDIO.barUiKey !== uiKey) {
            setTextIfChanged(bar.find('.lm-ios-player__title'), title);
            setTextIfChanged(bar.find('.lm-ios-player__artist'), artist);
            bar.find('[data-action="playpause"]').html(state.playing ? IOS_PLAYER_PAUSE_ICON : IOS_PLAYER_PLAY_ICON);
            bar.find('[data-action="prev"]').toggleClass('disabled', !state.hasPrev);
            bar.find('[data-action="next"]').toggleClass('disabled', !state.hasNext);
            bar.find('[data-action="queue"]').toggleClass('disabled', !state.canQueue);
            setTextIfChanged(bar.find('.lm-ios-player__time--total'), Lampa.Utils.secondsToTime(state.duration || 0));
            MUSIC_IOS_AUDIO.barUiKey = uiKey;
        }

        if (!isSeeking) {
            var currentText = Lampa.Utils.secondsToTime(state.current || 0);
            var seekValue = state.duration > 0 ? Math.max(0, Math.min(1000, Math.round((state.current / state.duration) * 1000))) : 0;
            var progressKey = currentText + '|' + seekValue;

            if (MUSIC_IOS_AUDIO.barProgressKey !== progressKey) {
                setTextIfChanged(bar.find('.lm-ios-player__time--current'), currentText);
                bar.find('.lm-ios-player__seek').val(seekValue);
                MUSIC_IOS_AUDIO.barProgressKey = progressKey;
            }
        }

        bar.addClass('lm-ios-player--visible');
        updateStandaloneIosFullPlayer();
    }

    // --- длительность/прогресс, synthetic-ended, watchdog и recovery ---

    function updateStandaloneIosPlaybackState() {
        var session = mediaSessionObject();
        var audio = MUSIC_IOS_AUDIO.audio;

        if (!session || !audio) return;

        try {
            if ('playbackState' in session) session.playbackState = MUSIC_IOS_AUDIO.playing && !audio.paused ? 'playing' : 'paused';
        } catch (e) {}

        updateStandaloneIosPlayerBar();
    }

    // для скраббера нужна реальная длительность стрима (метаданные расходятся с
    // рипом на секунды и seek попадает мимо), НО WebKit парсит YouTube fMP4 с
    // удвоенной длительностью: при диком расхождении со стримом верим метаданным
    function standaloneIosEffectiveDuration(audio, track) {
        var fallbackDuration = track && track.duration_ms ? Math.max(0, Math.round(track.duration_ms / 1000)) : 0;
        var streamDuration = audio && isFinite(audio.duration) && audio.duration > 0 ? audio.duration : 0;

        if (streamDuration && fallbackDuration && (streamDuration > fallbackDuration * 1.5 || streamDuration * 1.5 < fallbackDuration))
            return fallbackDuration;

        return streamDuration || fallbackDuration;
    }

    // когда WebKit завысил длительность (fMP4 2×), реальные данные кончаются на
    // effective-отметке и штатный 'ended' не стреляет — очередь двигаем вручную
    function maybeSyntheticStandaloneIosEnded(origin) {
        var audio = MUSIC_IOS_AUDIO.audio;
        var track = MUSIC_IOS_AUDIO.tracks[MUSIC_IOS_AUDIO.currentIndex];

        if (!audio || !track || MUSIC_IOS_AUDIO.switching || audio.paused || audio.ended) return false;

        var effective = standaloneIosEffectiveDuration(audio, track);
        var streamDuration = isFinite(audio.duration) && audio.duration > 0 ? audio.duration : 0;

        if (!effective || streamDuration <= effective + 1) return false;

        var tail = origin === 'timeupdate' ? 0.3 : 3;
        if (!isFinite(audio.currentTime) || audio.currentTime < effective - tail) return false;

        traceStandaloneIosAudio('synthetic-ended', origin + ' ct=' + (Math.round(audio.currentTime * 100) / 100) + ' effective=' + effective, true);

        MUSIC_IOS_AUDIO.playing = false;

        if (!standaloneIosHandleTrackEnd()) {
            try { audio.pause(); } catch (e) {}
            stopStandaloneIosKeepAlive('ended');
            syncStandaloneIosMediaSession();
            updateStandaloneIosPlayerBar();
        }

        return true;
    }

    function updateStandaloneIosPositionState(force) {
        var session = mediaSessionObject();
        var audio = MUSIC_IOS_AUDIO.audio;
        var track = MUSIC_IOS_AUDIO.tracks[MUSIC_IOS_AUDIO.currentIndex];
        var duration = standaloneIosEffectiveDuration(audio, track);
        var position = MUSIC_IOS_AUDIO.switching ? 0 : (audio && isFinite(audio.currentTime) ? audio.currentTime : 0);
        var now = Date.now();

        if (!session || !audio || typeof session.setPositionState !== 'function') return;
        if (audio.getAttribute('data-source-url') === 'silent-warmup') return;
        if (!duration) return;

        // В фоне iOS сам ведёт позицию Media Session по последнему
        // setPositionState. Частая запись раз в секунду только будит JS.
        var minPositionInterval = document.hidden ? 5000 : 1000;
        if (!force && now - (MUSIC_IOS_AUDIO.lastPositionSync || 0) < minPositionInterval) return;

        try {
            session.setPositionState({
                duration: duration,
                playbackRate: audio.playbackRate || 1,
                position: Math.max(0, Math.min(duration, position))
            });
            MUSIC_IOS_AUDIO.lastPositionSync = now;
            bumpMusicHeatMetric('standalonePositionState');
        } catch (e) {}
    }

    // watchdog после play(): резолв промиса play() и readyState=4 НЕ доказывают,
    // что звук идёт — iOS умеет держать элемент "играющим" с замороженным
    // currentTime (типично после lock screen команд). Проверяем реальный прогресс
    // через 700ms: attempt 0 — цикл pause()+play() («nudge», единственное, что
    // размораживает clock), attempt 1 — hidden: полная переприцепка src
    // (recoverStandaloneIosPlayback), visible: только трейс play-unfreeze-failed
    function watchStandaloneIosPlayProgress(media, origin, attempt) {
        var token = ++MUSIC_IOS_AUDIO.playWatchToken;
        var basePosition = media && isFinite(media.currentTime) ? media.currentTime : 0;
        var startedHidden = document.visibilityState === 'hidden' || !!document.hidden;
        var delay = startedHidden ? 1400 : 700;

        attempt = attempt || 0;

        setTimeout(function () {
            if (token !== MUSIC_IOS_AUDIO.playWatchToken) return;

            var audio = MUSIC_IOS_AUDIO.audio;
            if (!audio || audio !== media || !isStandaloneIosAudioActive()) return;
            if (MUSIC_IOS_AUDIO.switching || audio.paused || audio.ended) return;

            var progressed = (isFinite(audio.currentTime) ? audio.currentTime : 0) - basePosition;
            if (progressed > 0.3) return;

            // элемент "играет" (paused=false, readyState в норме), но clock заморожен:
            // единственный приём, который эмпирически снимает заморозку — цикл pause()+play()
            if (attempt === 0) {
                nudgeStandaloneIosPlayback(audio, origin, basePosition, true);
                watchStandaloneIosPlayProgress(media, origin, 1);
                return;
            }

            if (document.visibilityState === 'hidden' || document.hidden) {
                recoverStandaloneIosPlayback(origin, basePosition);
                return;
            }

            traceStandaloneIosAudio('play-unfreeze-failed', origin + ' delta=' + (Math.round(progressed * 100) / 100), true);
            updateStandaloneIosPlaybackState();
        }, delay);
    }

    function nudgeStandaloneIosPlayback(audio, origin, position, cycle) {
        traceStandaloneIosAudio(cycle ? 'play-nudge-cycle' : 'play-nudge', origin + ' position=' + (Math.round((position || 0) * 100) / 100), true);

        if (cycle) {
            try {
                audio.pause();
            } catch (e) {}
        }

        try {
            var promise = audio.play();
            if (promise && typeof promise.catch === 'function') promise.catch(function (error) {
                traceStandaloneIosAudio('play-nudge-rejected', error && error.name ? error.name : String(error || ''), true);
                updateStandaloneIosPlaybackState();
            });
        } catch (e) {
            traceStandaloneIosAudio('play-nudge-error', e && e.name ? e.name : String(e || ''), true);
            updateStandaloneIosPlaybackState();
        }

        setTimeout(function () {
            if (MUSIC_IOS_AUDIO.audio !== audio || MUSIC_IOS_AUDIO.switching || audio.paused || audio.ended) return;

            var moved = (isFinite(audio.currentTime) ? audio.currentTime : 0) - (position || 0);
            traceStandaloneIosAudio(moved > 0.3 ? 'play-nudge-ok' : 'play-nudge-stalled', 'delta=' + (Math.round(moved * 100) / 100), true);
            updateStandaloneIosPlaybackState();
        }, 900);
    }

    function scheduleStandaloneIosVisibleUnfreeze() {
        var audio = MUSIC_IOS_AUDIO.audio;

        if (!audio || !isStandaloneIosAudioActive()) return;
        if (MUSIC_IOS_AUDIO.switching || audio.paused || audio.ended) return;

        // после разблокировки элемент может остаться в "играет, но clock стоит":
        // watchdog проверит прогресс и при заморозке сделает pause()+play()
        watchStandaloneIosPlayProgress(audio, 'visible-unfreeze', 0);
    }

    // последний рубеж watchdog'а: полная переприцепка src (data-source-url) с
    // восстановлением позиции. По трейсам июля 2026 из заморозки под блокировкой
    // НИ РАЗУ не спасала (iOS не реактивирует аудио-сессию фоновой страницы —
    // это лечит keep-alive) — держим как страховку от других причин залипания.
    // Удалять только отдельным коммитом после теста именно заблокированного экрана
    function recoverStandaloneIosPlayback(origin, position) {
        var audio = MUSIC_IOS_AUDIO.audio;
        var sourceUrl = audio ? (audio.getAttribute('data-source-url') || '') : '';

        if (!audio || !sourceUrl) return;

        traceStandaloneIosAudio('play-recovery', origin + ' position=' + (Math.round((position || 0) * 100) / 100), true);

        var resumed = false;
        var resume = function () {
            if (resumed) return;
            resumed = true;
            audio.removeEventListener('loadedmetadata', resume);

            if (MUSIC_IOS_AUDIO.audio !== audio || MUSIC_IOS_AUDIO.switching) return;

            try {
                if (position > 0) audio.currentTime = position;
            } catch (e) {}

            try {
                var promise = audio.play();
                if (promise && typeof promise.catch === 'function') promise.catch(function (error) {
                    traceStandaloneIosAudio('play-recovery-rejected', error && error.name ? error.name : String(error || ''), true);
                    updateStandaloneIosPlaybackState();
                });
            } catch (e) {
                traceStandaloneIosAudio('play-recovery-error', e && e.name ? e.name : String(e || ''), true);
                updateStandaloneIosPlaybackState();
            }
        };

        audio.addEventListener('loadedmetadata', resume);

        try {
            audio.src = sourceUrl;
            audio.load();
        } catch (e) {
            audio.removeEventListener('loadedmetadata', resume);
            traceStandaloneIosAudio('play-recovery-load-error', e && e.name ? e.name : String(e || ''), true);
            return;
        }

        setTimeout(function () {
            if (MUSIC_IOS_AUDIO.audio !== audio || MUSIC_IOS_AUDIO.switching || audio.paused || audio.ended) return;

            var moved = (isFinite(audio.currentTime) ? audio.currentTime : 0) - (position || 0);
            traceStandaloneIosAudio(moved > 0.3 ? 'play-recovery-ok' : 'play-recovery-stalled', 'delta=' + (Math.round(moved * 100) / 100), true);

            if (moved <= 0.3) updateStandaloneIosPlaybackState();
        }, 1400);
    }

    // --- Media Session standalone-плеера (lock screen iOS) ---

    function syncStandaloneIosMediaSession() {
        var session = mediaSessionObject();
        var audio = MUSIC_IOS_AUDIO.audio;
        var track = MUSIC_IOS_AUDIO.tracks[MUSIC_IOS_AUDIO.currentIndex];
        var trackId = track && track.id ? track.id : '';

        if (!session || !audio || !track) {
            clearStandaloneIosMediaSession();
            return;
        }

        if (MUSIC_IOS_AUDIO.mediaSessionTrackId !== trackId && typeof window.MediaMetadata === 'function') {
            try {
                session.metadata = new MediaMetadata({
                    title: track.title || 'Track',
                    artist: track.artist_name || '',
                    album: track.album_title || '',
                    artwork: buildMediaSessionArtwork(track, MUSIC_IOS_AUDIO.playlist[MUSIC_IOS_AUDIO.currentIndex] || null)
                });
            } catch (e) {}
        }

        MUSIC_IOS_AUDIO.mediaSessionTrackId = trackId;
        armStandaloneIosMediaSessionHandlers();

        updateStandaloneIosPlaybackState();
        updateStandaloneIosPositionState(true);
    }

    // все обработчики регистрируются ОДИН раз и ДО первого play(): iOS
    // фиксирует возможности Now Playing-сессии в момент её создания —
    // seekto, зарегистрированный после старта, не включал скраббер до паузы
    function armStandaloneIosMediaSessionHandlers() {
        if (MUSIC_IOS_AUDIO.mediaSessionArmed) return;

        var session = mediaSessionObject();
        if (!session) return;

        MUSIC_IOS_AUDIO.mediaSessionArmed = true;

        {
            setMediaSessionHandler('play', function () {
                var media = standaloneIosAudioElement();
                if (!media) return;

                traceStandaloneIosAudio('media-session-play', '', true);

                try {
                    startStandaloneIosKeepAlive('media-session-play');
                    var promise = media.play();
                    if (promise && typeof promise.then === 'function') {
                        promise.then(function () {
                            traceStandaloneIosAudio('media-session-play-resolved', '', true);
                            updateStandaloneIosPlaybackState();
                            updateStandaloneIosPositionState(true);
                            watchStandaloneIosPlayProgress(media, 'media-session-play');
                        }).catch(function () {
                            traceStandaloneIosAudio('media-session-play-rejected', '', true);
                            updateStandaloneIosPlaybackState();
                        });
                    } else {
                        traceStandaloneIosAudio('media-session-play-sync', '', true);
                        updateStandaloneIosPlaybackState();
                        watchStandaloneIosPlayProgress(media, 'media-session-play-sync');
                    }
                } catch (e) {
                    traceStandaloneIosAudio('media-session-play-error', e && e.name ? e.name : String(e || ''), true);
                    updateStandaloneIosPlaybackState();
                }
            });
            setMediaSessionHandler('pause', function () {
                var media = standaloneIosAudioElement();
                if (!media) return;

                // с активным keep-alive iOS может считать страницу играющей и слать
                // pause вместо play: повторный pause на уже стоящем треке = команда play
                if (media.paused && MUSIC_IOS_AUDIO.keepAliveActive) {
                    traceStandaloneIosAudio('media-session-pause-as-play', '', true);

                    try {
                        var playPromise = media.play();
                        if (playPromise && typeof playPromise.catch === 'function') playPromise.catch(function (error) {
                            traceStandaloneIosAudio('media-session-pause-as-play-rejected', error && error.name ? error.name : String(error || ''), true);
                            updateStandaloneIosPlaybackState();
                        });
                        watchStandaloneIosPlayProgress(media, 'pause-as-play');
                    } catch (e) {
                        traceStandaloneIosAudio('media-session-pause-as-play-error', e && e.name ? e.name : String(e || ''), true);
                    }

                    updateStandaloneIosPlaybackState();
                    return;
                }

                traceStandaloneIosAudio('media-session-pause', '', true);

                try {
                    media.pause();
                } catch (e) {
                    traceStandaloneIosAudio('media-session-pause-error', e && e.name ? e.name : String(e || ''), true);
                }

                startStandaloneIosKeepAlive('media-session-pause');
                updateStandaloneIosPlaybackState();
                updateStandaloneIosPositionState(true);
            });

            setMediaSessionHandler('seekto', function (details) {
                var media = standaloneIosAudioElement();
                if (!media || typeof details.seekTime !== 'number') return;

                var seekTrack = MUSIC_IOS_AUDIO.tracks[MUSIC_IOS_AUDIO.currentIndex];
                var seekDuration = standaloneIosEffectiveDuration(media, seekTrack);
                var target = Math.max(0, seekDuration ? Math.min(seekDuration, details.seekTime) : details.seekTime);

                traceStandaloneIosAudio('media-session-seekto', 'to=' + (Math.round(target * 100) / 100) + (details.fastSeek ? ' fast' : ''), true);

                try {
                    if (details.fastSeek && typeof media.fastSeek === 'function')
                        media.fastSeek(target);
                    else
                        media.currentTime = target;
                } catch (e) {
                    traceStandaloneIosAudio('media-session-seekto-error', e && e.name ? e.name : String(e || ''), true);
                }

                updateStandaloneIosPositionState(true);
                updateStandaloneIosPlayerBar();
            });
            setMediaSessionHandler('seekforward', null);
            setMediaSessionHandler('seekbackward', null);

            setMediaSessionHandler('previoustrack', function () {
                var prevIndex = standaloneIosNeighborIndex(-1);
                if (prevIndex >= 0) standaloneIosPlayIndex(prevIndex);
            });

            setMediaSessionHandler('nexttrack', function () {
                var nextIndex = standaloneIosNeighborIndex(1);
                if (nextIndex >= 0) standaloneIosPlayIndex(nextIndex);
            });
        }
    }

    // --- запуск/остановка standalone-воспроизведения, смена трека ---

    function stopStandaloneIosAudioPlayback() {
        clearStandaloneIosSleepTimer(false);

        if (MUSIC_IOS_AUDIO.audio) {
            setStandaloneIosTimeupdateListener(false);

            try {
                MUSIC_IOS_AUDIO.audio.pause();
            } catch (e) {}

            try {
                MUSIC_IOS_AUDIO.audio.removeAttribute('src');
                MUSIC_IOS_AUDIO.audio.load();
            } catch (e) {}

            try {
                if (MUSIC_IOS_AUDIO.audio.parentNode)
                    MUSIC_IOS_AUDIO.audio.parentNode.removeChild(MUSIC_IOS_AUDIO.audio);
            } catch (e) {}
        }

        MUSIC_IOS_AUDIO.active = false;
        MUSIC_IOS_AUDIO.switching = false;
        MUSIC_IOS_AUDIO.playing = false;
        MUSIC_IOS_AUDIO.playlist = [];
        MUSIC_IOS_AUDIO.tracks = [];
        MUSIC_IOS_AUDIO.currentIndex = -1;
        MUSIC_IOS_AUDIO.audio = null;
        MUSIC_IOS_AUDIO.timeupdateHandler = null;
        MUSIC_IOS_AUDIO.timeupdateAttached = false;
        MUSIC_IOS_AUDIO.prepareToken = (MUSIC_IOS_AUDIO.prepareToken || 0) + 1;
        MUSIC_IOS_AUDIO.playWatchToken++;
        stopStandaloneIosKeepAlive('stop');
        MUSIC_QUEUE.tracks = [];
        MUSIC_QUEUE.currentIndex = 0;
        MUSIC_QUEUE.currentTrackId = null;
        clearQueueSnapshot();

        clearPendingTrackPlayed();
        clearStandaloneIosMediaSession();
        updateStandaloneIosPlayerBar();
    }

    function applyStandaloneIosResumePosition(audio, track) {
        var position = Math.max(0, Number(MUSIC_IOS_AUDIO.resumePosition || 0));

        MUSIC_IOS_AUDIO.resumePosition = 0;
        if (!audio || !position || position < 2) return;

        var duration = track && track.duration_ms ? Math.max(0, Math.round(track.duration_ms / 1000)) : 0;
        if (duration && position >= duration - 3) return;

        function seek() {
            try {
                audio.currentTime = Math.max(0, position);
                scheduleQueueSnapshotSave(true);
            } catch (e) {}
        }

        if (audio.readyState >= 1) {
            seek();
            return;
        }

        try {
            audio.addEventListener('loadedmetadata', seek, { once: true });
        } catch (e) {}
    }

    function standaloneIosPlayIndex(index) {
        var audio = standaloneIosAudioElement();
        var playback = MUSIC_IOS_AUDIO.playlist[index];
        var track = MUSIC_IOS_AUDIO.tracks[index];

        if (!audio || !playback || !playback.url || !track) return false;
        if (MUSIC_IOS_AUDIO.switching) return false;

        MUSIC_IOS_AUDIO.switching = true;
        MUSIC_IOS_AUDIO.playing = false;
        MUSIC_IOS_AUDIO.playWatchToken++;
        MUSIC_IOS_AUDIO.currentIndex = index;
        updateQueueCurrent(track.id);
        scheduleTrackPlayed(track);
        updateStandaloneIosPlayerBar();

        if (typeof playback.url === 'function') {
            syncStandaloneIosMediaSession();
            updateStandaloneIosPositionState();

            playback.url(function () {
                MUSIC_IOS_AUDIO.switching = false;

                if (!playback.url || typeof playback.url !== 'string') {
                    // резолв не удался — глушим warmup-тишину, если она играла
                    if (audio.getAttribute('data-source-url') === 'silent-warmup') {
                        try {
                            audio.loop = false;
                            audio.pause();
                        } catch (e) {}
                    }

                    syncStandaloneIosMediaSession();
                    updateStandaloneIosPlayerBar();
                    return;
                }

                standaloneIosPlayIndex(index);
            });

            return true;
        }

        try {
            var currentSourceUrl = audio.getAttribute('data-source-url') || '';

            if (currentSourceUrl !== playback.url) {
                try {
                    audio.muted = false;
                    audio.playbackRate = 1;
                    audio.loop = false;
                } catch (e) {}

                audio.src = playback.url;
                audio.setAttribute('data-source-url', playback.url);
                audio.currentTime = 0;
                traceStandaloneIosAudio('track-source', '', true);
            }

            applyStandaloneIosResumePosition(audio, track);
            syncStandaloneIosMediaSession();
            updateStandaloneIosPositionState();

            traceStandaloneIosAudio('track-play', '', true);
            var promise = audio.play();
            if (promise && typeof promise.catch === 'function') {
                promise.catch(function (error) {
                    MUSIC_IOS_AUDIO.switching = false;
                    MUSIC_IOS_AUDIO.playing = false;
                    traceStandaloneIosAudio('track-play-rejected', error && error.name ? error.name : String(error || ''), true);
                });
            }
        } catch (e) {
            MUSIC_IOS_AUDIO.switching = false;
            MUSIC_IOS_AUDIO.playing = false;
            traceStandaloneIosAudio('track-play-error', e && e.name ? e.name : String(e || ''), true);
            return false;
        }

        return true;
    }

    function startStandaloneIosAudioPlayback(tracks, preparedList, startIndex, resumePosition) {
        MUSIC_IOS_AUDIO.playlist = (preparedList || []).slice();
        MUSIC_IOS_AUDIO.tracks = (tracks || []).slice();
        MUSIC_IOS_AUDIO.currentIndex = -1;
        MUSIC_IOS_AUDIO.active = true;
        MUSIC_IOS_AUDIO.switching = false;
        MUSIC_IOS_AUDIO.playing = false;
        MUSIC_IOS_AUDIO.mediaSessionTrackId = null;
        MUSIC_IOS_AUDIO.lastPositionSync = 0;
        MUSIC_IOS_AUDIO.prepareToken = (MUSIC_IOS_AUDIO.prepareToken || 0) + 1;
        MUSIC_IOS_AUDIO.shuffleOrder = null;
        MUSIC_IOS_AUDIO.resumePosition = Math.max(0, Number(resumePosition || 0));
        updateStandaloneIosPlayerBar();
        startStandaloneIosKeepAlive('playlist-start');

        return standaloneIosPlayIndex(startIndex) ? MUSIC_IOS_AUDIO.prepareToken : 0;
    }

    // --- Media Session: общие хелперы обоих режимов ---

    function mediaSessionObject() {
        try {
            return navigator && navigator.mediaSession ? navigator.mediaSession : null;
        } catch (e) {
            return null;
        }
    }

    function setMediaSessionHandler(action, handler) {
        var session = mediaSessionObject();
        if (!session || typeof session.setActionHandler !== 'function') return;

        try {
            session.setActionHandler(action, handler || null);
        } catch (e) {}
    }

    function guessArtworkMimeType(url) {
        var value = String(url || '');

        if (value.indexOf('data:image/png') === 0) return 'image/png';
        if (value.indexOf('data:image/webp') === 0) return 'image/webp';
        if (value.indexOf('data:image/svg+xml') === 0) return 'image/svg+xml';
        if (value.indexOf('png') !== -1) return 'image/png';
        if (value.indexOf('webp') !== -1) return 'image/webp';
        return 'image/jpeg';
    }

    function buildMediaSessionArtwork(track, data) {
        var image = trackImage(track) || (data && (data.img || data.poster)) || '';
        if (!image) return [];

        return [{
            src: image,
            sizes: '512x512',
            type: guessArtworkMimeType(image)
        }];
    }

    // --- embedded-режим (player=inner): lock screen поверх Lampa.PlayerVideo.
    //     NB: живёт здесь, а не в LAMPA PLAYER BRIDGE — тесно завязан на общие
    //     media-session-хелперы выше ---

    function activeMusicMediaElement() {
        return embeddedIosStockMediaElement();
    }

    function embeddedIosStockMediaElement() {
        return musicPlayerPanelMedia || document.querySelector('.player-video video, .player-video audio');
    }

    function prepareEmbeddedIosMediaElement(media, origin) {
        if (!media || !shouldUseEmbeddedIosLockscreenMode()) return false;

        var alreadyPrepared = media.getAttribute('data-music-ios-prepared') === '1';

        try {
            media.playsInline = true;
            media.preload = 'auto';
            media.setAttribute('playsinline', 'playsinline');
            media.setAttribute('webkit-playsinline', 'webkit-playsinline');
            media.setAttribute('x-webkit-airplay', 'allow');
            media.setAttribute('data-music-ios-prepared', '1');
        } catch (e) {}

        if (!alreadyPrepared)
            traceEmbeddedIos('media-prepare', origin || '', true);

        return true;
    }

    function scheduleEmbeddedIosMediaPrepare(origin) {
        if (!shouldUseEmbeddedIosLockscreenMode()) return;

        [0, 16, 50, 120, 300].forEach(function (delay) {
            setTimeout(function () {
                prepareEmbeddedIosMediaElement(document.querySelector('.player-video video, .player-video audio'), origin);
            }, delay);
        });
    }

    function shouldPreferTrackNavigationControls() {
        var player = currentExternalPlayer();

        return Lampa.Platform.is('apple')
            && player === 'ios'
            && !shouldUseStandaloneIosAudio();
    }

    function shouldUseEmbeddedIosLockscreenMode() {
        return Lampa.Platform.is('apple')
            && getPlaybackMode() === 'audio'
            && !shouldUseStandaloneIosAudio()
            && usesInternalPlaybackFlow();
    }

    function updateMediaSessionPositionState() {
        var session = mediaSessionObject();
        var media = activeMusicMediaElement();
        var data = activePlayerData() || MUSIC_EMBEDDED_IOS.lastData || {};
        var track = queueCurrentTrack();
        var duration = media && isFinite(media.duration) && media.duration > 0 ? media.duration : 0;

        if (!session || typeof session.setPositionState !== 'function') return;
        if (shouldUseEmbeddedIosLockscreenSupport(data)) return;
        if (!duration && data && data.from_music_cluster)
            duration = data.music_duration || data.duration || (data.timeline && data.timeline.duration) || 0;
        if (!duration && track && track.duration_ms)
            duration = Math.max(0, Math.round(Number(track.duration_ms) / 1000));

        if (!isFinite(duration) || duration <= 0) return;

        try {
            session.setPositionState({
                duration: duration,
                playbackRate: media && media.playbackRate ? media.playbackRate : 1,
                position: Math.max(0, Math.min(duration, media && isFinite(media.currentTime)
                    ? media.currentTime
                    : ((data.timeline && data.timeline.time) || 0)))
            });
            bumpMusicHeatMetric('embeddedPositionState');
        } catch (e) {}
    }

    function shouldUseEmbeddedIosLockscreenSupport(data) {
        return shouldUseEmbeddedIosLockscreenMode()
            && data
            && data.from_music_cluster
            && getPlaybackMode() === 'audio';
    }

    function embeddedIosActiveData() {
        var data = activePlayerData();
        return data && data.from_music_cluster ? data : MUSIC_EMBEDDED_IOS.lastData;
    }

    function updateEmbeddedIosPlaybackState() {
        var session = mediaSessionObject();
        var media = activeMusicMediaElement();

        if (!session || !media) return;

        try {
            if ('playbackState' in session) session.playbackState = media.paused || media.ended ? 'paused' : 'playing';
        } catch (e) {}

        updateMediaSessionPositionState();
    }

    function embeddedIosEffectiveDuration(media, data) {
        var duration = media && isFinite(media.duration) && media.duration > 0 ? media.duration : 0;

        if (duration) return duration;

        data = data || embeddedIosActiveData();
        return (data && (data.music_duration || data.duration || (data.timeline && data.timeline.duration))) || 0;
    }

    function applyEmbeddedIosMediaSessionMetadata(data, track) {
        var session = mediaSessionObject();

        if (!session || typeof window.MediaMetadata !== 'function') return;

        track = track || queueCurrentTrack();
        if ((!track || !track.id) && data && data.music_track_id) {
            track = {
                id: data.music_track_id,
                title: data.title || 'Track',
                artist_name: data.artist || '',
                img: data.img || data.poster || ''
            };
        }

        if (!track) return;

        try {
            session.metadata = new MediaMetadata({
                title: track.title || (data && data.title) || 'Track',
                artist: track.artist_name || (data && data.artist) || '',
                album: track.album_title || '',
                artwork: buildMediaSessionArtwork(track, data)
            });
        } catch (e) {}
    }

    function warmupEmbeddedIosGesture(track) {
        if (!shouldUseEmbeddedIosLockscreenMode() || !track) return;

        var data = {
            from_music_cluster: true,
            music_track_id: track.id || '',
            title: track.title || 'Track',
            artist: track.artist_name || '',
            poster: trackImage(track),
            img: trackImage(track),
            music_duration_ms: track.duration_ms || 0,
            music_duration: track.duration_ms ? Math.max(0, Math.round(Number(track.duration_ms) / 1000)) : 0
        };

        MUSIC_EMBEDDED_IOS.active = true;
        MUSIC_EMBEDDED_IOS.lastData = data;
        bindEmbeddedIosLifecycleTrace();
        armEmbeddedIosMediaSessionHandlers();
        applyEmbeddedIosMediaSessionMetadata(data, track);
        updateMediaSessionPositionState();
        scheduleEmbeddedIosMediaPrepare('gesture-warmup');
        startStandaloneIosKeepAlive('embedded-gesture-warmup');
        traceEmbeddedIos('gesture-warmup', '', true);
    }

    function primeEmbeddedIosMediaSession(data, track, origin) {
        if (!shouldUseEmbeddedIosLockscreenSupport(data)) return;

        MUSIC_EMBEDDED_IOS.active = true;
        MUSIC_EMBEDDED_IOS.lastData = data;
        bindEmbeddedIosLifecycleTrace();
        armEmbeddedIosMediaSessionHandlers();
        applyEmbeddedIosMediaSessionMetadata(data, track);
        updateMediaSessionPositionState();
        scheduleEmbeddedIosMediaPrepare(origin || 'prime');
        startStandaloneIosKeepAlive('embedded-' + (origin || 'prime'));
        traceEmbeddedIos('prime', origin || '', true);
    }

    function armEmbeddedIosMediaSessionHandlers() {
        if (MUSIC_EMBEDDED_IOS.mediaSessionArmed) return;

        var session = mediaSessionObject();
        if (!session) return;

        MUSIC_EMBEDDED_IOS.mediaSessionArmed = true;
        traceEmbeddedIos('media-session-arm', '', true);

        setMediaSessionHandler('play', function () {
            var data = embeddedIosActiveData();
            if (!shouldUseEmbeddedIosLockscreenSupport(data)) return;

            traceEmbeddedIos('media-session-play', '', true);
            playEmbeddedMusicMedia('media-session-play');
        });

        setMediaSessionHandler('pause', function () {
            var data = embeddedIosActiveData();
            var media = activeMusicMediaElement();
            if (!shouldUseEmbeddedIosLockscreenSupport(data)) return;

            if (media && media.paused && MUSIC_IOS_AUDIO.keepAliveActive) {
                traceEmbeddedIos('media-session-pause-as-play', '', true);
                playEmbeddedMusicMedia('media-session-pause-as-play');
                return;
            }

            traceEmbeddedIos('media-session-pause', '', true);
            pauseEmbeddedMusicMedia('media-session-pause');
        });

        setMediaSessionHandler('seekto', null);
        setMediaSessionHandler('seekforward', null);
        setMediaSessionHandler('seekbackward', null);
        setMediaSessionHandler('previoustrack', function () {
            traceEmbeddedIos('media-session-previoustrack', '', true);
            playEmbeddedQueueOffset(-1);
        });
        setMediaSessionHandler('nexttrack', function () {
            traceEmbeddedIos('media-session-nexttrack', '', true);
            playEmbeddedQueueOffset(1);
        });
    }

    function copyEmbeddedPlaybackData(target, playback) {
        if (!target || !playback) return;

        Object.keys(playback).forEach(function (key) {
            target[key] = playback[key];
        });
    }

    function applyEmbeddedPlaybackData(track, playback, index) {
        var work = activePlayerData();
        var playlist = activePlaylist();

        copyEmbeddedPlaybackData(work, playback);

        if (playlist && playlist[index])
            copyEmbeddedPlaybackData(playlist[index], playback);

        updateQueueCurrent(track.id);

        try {
            if (Lampa.PlayerPlaylist && typeof Lampa.PlayerPlaylist.url === 'function')
                Lampa.PlayerPlaylist.url(playback.url);
        } catch (e) {}

        try {
            if (Lampa.Player && typeof Lampa.Player.playlist === 'function')
                Lampa.Player.playlist(playlist);
        } catch (e) {}

        try {
            if (Lampa.PlayerInfo && typeof Lampa.PlayerInfo.set === 'function')
                Lampa.PlayerInfo.set('name', playback.title || track.title || 'Track');
        } catch (e) {}

        try {
            if (Lampa.PlayerPanel && typeof Lampa.PlayerPanel.quality === 'function')
                Lampa.PlayerPanel.quality(playback.quality, playback.url);
        } catch (e) {}

        try {
            if (Lampa.PlayerPanel && typeof Lampa.PlayerPanel.showNextEpisodeName === 'function')
                Lampa.PlayerPanel.showNextEpisodeName({
                    playlist: playlist,
                    position: index
                });
        } catch (e) {}

        musicPlayerPanelSyntheticEndKey = '';
        renderMusicPlayerVisual(playback);
    }

    function selectedMatchDurationMs(match) {
        var duration = Number(match && match.duration_ms ? match.duration_ms : 0);
        return isFinite(duration) && duration > 0 ? Math.round(duration) : 0;
    }

    function patchTrackDuration(track, durationMs) {
        if (!track || !durationMs) return false;

        var previous = Number(track.duration_ms || 0);
        track.duration_ms = durationMs;
        return previous !== durationMs;
    }

    function patchPlaybackDuration(playback, durationMs) {
        if (!playback || !durationMs) return false;

        var duration = Math.max(0, Math.round(durationMs / 1000));
        var changed = playback.music_duration_ms !== durationMs || playback.music_duration !== duration;

        playback.music_duration_ms = durationMs;
        playback.music_duration = duration;

        if (playback.timeline) {
            playback.timeline.duration = duration;
            if (playback.timeline.time > duration) playback.timeline.time = duration;
            playback.timeline.percent = duration > 0
                ? Math.max(0, Math.min(100, (playback.timeline.time || 0) / duration * 100))
                : 0;
        }

        return changed;
    }

    function patchTrackCollectionDuration(items, trackId, durationMs) {
        if (!Array.isArray(items) || !trackId || !durationMs) return false;

        var changed = false;
        items.forEach(function (item) {
            if (item && item.id === trackId)
                changed = patchTrackDuration(item, durationMs) || changed;
        });

        return changed;
    }

    function patchPlaybackCollectionDuration(items, trackId, durationMs) {
        if (!Array.isArray(items) || !trackId || !durationMs) return false;

        var changed = false;
        items.forEach(function (item) {
            if (item && item.music_track_id === trackId)
                changed = patchPlaybackDuration(item, durationMs) || changed;
        });

        return changed;
    }

    function applySelectedMatchMetadata(track, match) {
        var durationMs = selectedMatchDurationMs(match);
        var trackId = track && track.id ? track.id : '';
        var changed = false;

        if (!trackId || !durationMs) return false;

        changed = patchTrackDuration(track, durationMs) || changed;
        changed = patchTrackCollectionDuration(queueTracks(), trackId, durationMs) || changed;
        changed = patchTrackCollectionDuration(MUSIC_IOS_AUDIO.tracks, trackId, durationMs) || changed;
        changed = patchPlaybackCollectionDuration(MUSIC_IOS_AUDIO.playlist, trackId, durationMs) || changed;
        changed = patchPlaybackCollectionDuration(activePlaylist(), trackId, durationMs) || changed;

        var work = activePlayerData();
        if (work && work.from_music_cluster && work.music_track_id === trackId)
            changed = patchPlaybackDuration(work, durationMs) || changed;

        if (MUSIC_EMBEDDED_IOS.lastData && MUSIC_EMBEDDED_IOS.lastData.music_track_id === trackId)
            patchPlaybackDuration(MUSIC_EMBEDDED_IOS.lastData, durationMs);

        if (changed) {
            scheduleQueueSnapshotSave(true);
            applyMusicPlayerPanelFix(false);
            updateStandaloneIosPlayerBar();
            updateMediaSessionPositionState();
        }

        return changed;
    }

    function stopEmbeddedDuplicateMedia(keep) {
        try {
            document.querySelectorAll('.player-video video, .player-video audio').forEach(function (item) {
                if (item === keep) return;

                try { item.pause(); } catch (e) {}
                try { item.removeAttribute('src'); } catch (e) {}
                try { item.load(); } catch (e) {}
                try { item.parentNode && item.parentNode.removeChild(item); } catch (e) {}
            });
        } catch (e) {}
    }

    function switchEmbeddedMediaSource(media, url) {
        if (!media || !url) return false;

        prepareEmbeddedIosMediaElement(media, 'switch-source');
        stopEmbeddedDuplicateMedia(media);

        try {
            media.pause();
        } catch (e) {}

        try {
            media.removeAttribute('src');
            media.load();
        } catch (e) {}

        try {
            media.src = url;
            media.currentTime = 0;
            media.load();

            var promise = media.play();
            if (promise && typeof promise.catch === 'function') promise.catch(function (error) {
                traceEmbeddedIos('queue-inplace-play-rejected', error && error.name ? error.name : String(error || ''), true);
            });
        } catch (e) {
            traceEmbeddedIos('queue-inplace-error', e && e.name ? e.name : String(e || ''), true);
            return false;
        }

        return true;
    }

    function playEmbeddedQueueIndexInPlace(index, origin) {
        var tracks = queueTracks();
        var track = tracks[index];
        var switchToken = ++MUSIC_EMBEDDED_IOS.switchToken;

        if (!track) return false;

        traceEmbeddedIos('queue-inplace-request', 'index=' + index + ' origin=' + (origin || ''), true);

        requestPlay(track, function (json) {
            if (switchToken !== MUSIC_EMBEDDED_IOS.switchToken) {
                traceEmbeddedIos('queue-inplace-stale', 'index=' + index, true);
                return;
            }

            var media = activeMusicMediaElement();
            if (!media) {
                traceEmbeddedIos('queue-inplace-no-media', 'index=' + index, true);
                return;
            }

            var playback = buildResolvedPlayback(track, json);
            applyEmbeddedPlaybackData(track, playback, index);

            traceEmbeddedIos('queue-inplace-source', 'index=' + index + ' url=' + describeMusicValue(playback.url), true);

            if (!switchEmbeddedMediaSource(media, playback.url))
                return;

            startStandaloneIosKeepAlive('embedded-inplace');
            syncMusicMediaSession(playback);
            updateMediaSessionPositionState();
            traceEmbeddedIos('queue-inplace-started', 'index=' + index, true);
        }, function (json) {
            traceEmbeddedIos('queue-inplace-failed', (json && json.message) || ('index=' + index), true);
            Lampa.Noty.show(json && json.message ? json.message : 'Источник для трека пока не найден.');
        });

        return true;
    }

    function playEmbeddedQueueOffset(offset) {
        var tracks = queueTracks();
        var currentIndex = queueCurrentIndex();
        var nextIndex = currentIndex + offset;

        if (!tracks.length || currentIndex < 0) return false;
        if (nextIndex < 0 || nextIndex >= tracks.length) return false;

        traceEmbeddedIos(offset > 0 ? 'nexttrack' : 'previoustrack', 'from=' + currentIndex + ' to=' + nextIndex, true);

        scheduleTrackPlayed(tracks[nextIndex]);
        return playEmbeddedQueueIndexInPlace(nextIndex, offset > 0 ? 'nexttrack' : 'previoustrack');
    }

    function playEmbeddedMusicMedia(origin) {
        var media = activeMusicMediaElement();
        if (!media) return null;

        traceEmbeddedIos('play', origin || '', true);
        startStandaloneIosKeepAlive('embedded-' + (origin || 'play'));

        try {
            if (Lampa.PlayerVideo && typeof Lampa.PlayerVideo.play === 'function')
                Lampa.PlayerVideo.play();
        } catch (e) {}

        try {
            var promise = media.play();
            if (promise && typeof promise.then === 'function') {
                promise.then(function () {
                    traceEmbeddedIos('play-resolved', origin || '', true);
                    updateEmbeddedIosPlaybackState();
                    watchEmbeddedIosPlayProgress(media, origin || 'play', 0);
                }).catch(function () {
                    traceEmbeddedIos('play-rejected', origin || '', true);
                    updateEmbeddedIosPlaybackState();
                });
            } else {
                traceEmbeddedIos('play-sync', origin || '', true);
                updateEmbeddedIosPlaybackState();
                watchEmbeddedIosPlayProgress(media, origin || 'play-sync', 0);
            }
        } catch (e) {
            traceEmbeddedIos('play-error', (e && e.name ? e.name : String(e || '')), true);
            updateEmbeddedIosPlaybackState();
        }

        return media;
    }

    function pauseEmbeddedMusicMedia(origin) {
        var media = activeMusicMediaElement();
        if (!media) return;

        traceEmbeddedIos('pause', origin || '', true);

        try {
            if (Lampa.PlayerVideo && typeof Lampa.PlayerVideo.pause === 'function')
                Lampa.PlayerVideo.pause();
            else
                media.pause();
        } catch (e) {
            try { media.pause(); } catch (_) {}
        }

        startStandaloneIosKeepAlive('embedded-' + (origin || 'pause'));
        updateEmbeddedIosPlaybackState();
    }

    function watchEmbeddedIosPlayProgress(media, origin, attempt) {
        var token = ++MUSIC_EMBEDDED_IOS.playWatchToken;
        var basePosition = media && isFinite(media.currentTime) ? media.currentTime : 0;
        var startedHidden = document.visibilityState === 'hidden' || !!document.hidden;
        var delay = startedHidden ? 1400 : 700;

        attempt = attempt || 0;

        setTimeout(function () {
            if (token !== MUSIC_EMBEDDED_IOS.playWatchToken) return;
            if (!MUSIC_EMBEDDED_IOS.active || activeMusicMediaElement() !== media) return;
            if (!media || media.paused || media.ended) return;

            var progressed = (isFinite(media.currentTime) ? media.currentTime : 0) - basePosition;
            if (progressed > 0.3) return;

            if (attempt === 0) {
                try { media.pause(); } catch (e) {}
                playEmbeddedMusicMedia(origin || 'watchdog');
                watchEmbeddedIosPlayProgress(media, origin || 'watchdog', 1);
                return;
            }

            updateEmbeddedIosPlaybackState();
        }, delay);
    }

    function scheduleEmbeddedIosVisibleUnfreeze() {
        var data = embeddedIosActiveData();
        var media = activeMusicMediaElement();

        if (!shouldUseEmbeddedIosLockscreenSupport(data) || !media || media.paused || media.ended) return;

        watchEmbeddedIosPlayProgress(media, 'visible-unfreeze', 0);
    }

    function scheduleEmbeddedIosLockKick() {
        var data = embeddedIosActiveData();
        var media = activeMusicMediaElement();

        if (!shouldUseEmbeddedIosLockscreenSupport(data) || !media || media.paused || media.ended) return;

        setTimeout(function () {
            if (!document.hidden || activeMusicMediaElement() !== media || media.paused || media.ended) return;

            try {
                media.pause();
                var promise = media.play();
                if (promise && typeof promise.catch === 'function') promise.catch(function () {});
            } catch (e) {}
        }, 400);
    }

    function bindEmbeddedIosLifecycleTrace() {
        if (MUSIC_EMBEDDED_IOS.lifecycleBound) return;
        MUSIC_EMBEDDED_IOS.lifecycleBound = true;

        ['visibilitychange', 'pagehide', 'pageshow', 'focus', 'blur'].forEach(function (eventName) {
            var target = eventName === 'visibilitychange' ? document : window;
            target.addEventListener(eventName, function () {
                var data = embeddedIosActiveData();
                if (!shouldUseEmbeddedIosLockscreenSupport(data)) return;

                traceEmbeddedIos('lifecycle-' + eventName, '', true);
                updateEmbeddedIosPlaybackState();

                if (eventName === 'visibilitychange' && !document.hidden) {
                    reviveStandaloneIosKeepAlive();
                    scheduleEmbeddedIosVisibleUnfreeze();
                }

                if (eventName === 'visibilitychange' && document.hidden) {
                    syncMusicMediaSession(data);
                    scheduleEmbeddedIosLockKick();
                }
            });
        });
    }

    function clearEmbeddedIosLockscreenSupport(origin) {
        MUSIC_EMBEDDED_IOS.active = false;
        MUSIC_EMBEDDED_IOS.lastData = null;
        MUSIC_EMBEDDED_IOS.playWatchToken++;
        MUSIC_EMBEDDED_IOS.mediaSessionArmed = false;

        if (!isStandaloneIosAudioActive())
            stopStandaloneIosKeepAlive('embedded-' + (origin || 'clear'));
    }

    // --- навигация по очереди (общая для обоих режимов) и маршрутизация Media Session ---

    function playQueueOffset(offset) {
        var tracks = queueTracks();
        var currentIndex = queueCurrentIndex();
        var nextIndex = currentIndex + offset;

        if (!tracks.length || currentIndex < 0) return false;
        if (nextIndex < 0 || nextIndex >= tracks.length) return false;

        if (canReuseActiveQueue(tracks) && selectFromActiveQueue(nextIndex)) {
            scheduleTrackPlayed(tracks[nextIndex]);
            return true;
        }

        playTrack(tracks[nextIndex], tracks, nextIndex);
        return true;
    }

    function canUseActivePlaylistNavigation() {
        var tracks = queueTracks();
        return !!(tracks.length > 1 && canReuseActiveQueue(tracks));
    }

    function playQueueOffsetViaActivePlaylist(offset) {
        var tracks = queueTracks();
        var currentIndex = queueCurrentIndex();
        var nextIndex = currentIndex + offset;

        if (!tracks.length || currentIndex < 0) return false;
        if (nextIndex < 0 || nextIndex >= tracks.length) return false;
        if (!canUseActivePlaylistNavigation()) return false;

        if (selectFromActiveQueue(nextIndex)) {
            scheduleTrackPlayed(tracks[nextIndex]);
            return true;
        }

        return false;
    }

    function syncMusicMediaSession(data) {
        var session = mediaSessionObject();
        if (!session) return;

        if (!data || !data.from_music_cluster) {
            bumpMusicHeatMetric('embeddedMediaSessionClear');
            clearEmbeddedIosLockscreenSupport('clear');
            setMediaSessionHandler('play', null);
            setMediaSessionHandler('pause', null);
            setMediaSessionHandler('seekto', null);
            setMediaSessionHandler('seekforward', null);
            setMediaSessionHandler('seekbackward', null);
            setMediaSessionHandler('nexttrack', null);
            setMediaSessionHandler('previoustrack', null);

            try {
                if ('playbackState' in session) session.playbackState = 'none';
            } catch (e) {}

            return;
        }

        bumpMusicHeatMetric('embeddedMediaSessionSync');

        if (shouldUseEmbeddedIosLockscreenSupport(data)) {
            MUSIC_EMBEDDED_IOS.active = true;
            MUSIC_EMBEDDED_IOS.lastData = data;
            armEmbeddedIosMediaSessionHandlers();
            bindEmbeddedIosLifecycleTrace();
            traceEmbeddedIos('sync', '', true);

            var embeddedMedia = activeMusicMediaElement();
            if (embeddedMedia)
                startStandaloneIosKeepAlive('embedded-sync');
        }

        var track = queueCurrentTrack();
        if ((!track || !track.id) && data.music_track_id) {
            track = {
                id: data.music_track_id,
                title: data.title || 'Track',
                artist_name: data.artist || '',
                img: data.img || data.poster || ''
            };
        }

        applyEmbeddedIosMediaSessionMetadata(data, track);

        setMediaSessionHandler('play', function () {
            var currentData = activePlayerData() || data;

            if (shouldUseEmbeddedIosLockscreenSupport(currentData)) {
                traceEmbeddedIos('media-session-play', '', true);
                playEmbeddedMusicMedia('media-session-play');
                return;
            }

            if (Lampa.PlayerVideo && typeof Lampa.PlayerVideo.play === 'function') {
                Lampa.PlayerVideo.play();
            } else {
                var media = activeMusicMediaElement();
                if (media && media.paused) media.play();
            }

            try {
                if ('playbackState' in session) session.playbackState = 'playing';
            } catch (e) {}
        });

        setMediaSessionHandler('pause', function () {
            var currentData = activePlayerData() || data;
            var media = activeMusicMediaElement();

            if (shouldUseEmbeddedIosLockscreenSupport(currentData)) {
                // С активным keep-alive iOS иногда присылает pause вместо play.
                // Если основной media уже на паузе, трактуем повторный pause как play.
                if (media && media.paused && MUSIC_IOS_AUDIO.keepAliveActive) {
                    traceEmbeddedIos('media-session-pause-as-play', '', true);
                    playEmbeddedMusicMedia('media-session-pause-as-play');
                    return;
                }

                traceEmbeddedIos('media-session-pause', '', true);
                pauseEmbeddedMusicMedia('media-session-pause');
                return;
            }

            if (Lampa.PlayerVideo && typeof Lampa.PlayerVideo.pause === 'function') {
                Lampa.PlayerVideo.pause();
            } else {
                if (media && !media.paused) media.pause();
            }

            try {
                if ('playbackState' in session) session.playbackState = 'paused';
            } catch (e) {}
        });

        if (shouldUseEmbeddedIosLockscreenSupport(activePlayerData() || data)) {
            var embeddedQueueLength = queueTracks().length;
            var embeddedIndex = queueCurrentIndex();

            setMediaSessionHandler('seekto', null);
            setMediaSessionHandler('seekforward', null);
            setMediaSessionHandler('seekbackward', null);
            setMediaSessionHandler('previoustrack', embeddedQueueLength > 1 && embeddedIndex > 0
                ? function () {
                    traceEmbeddedIos('media-session-previoustrack', '', true);
                    playEmbeddedQueueOffset(-1);
                }
                : null);
            setMediaSessionHandler('nexttrack', embeddedQueueLength > 1 && embeddedIndex >= 0 && embeddedIndex < embeddedQueueLength - 1
                ? function () {
                    traceEmbeddedIos('media-session-nexttrack', '', true);
                    playEmbeddedQueueOffset(1);
                }
                : null);
        } else if (shouldPreferTrackNavigationControls() && queueTracks().length > 1) {
            var canNavigateQueue = canUseActivePlaylistNavigation();

            setMediaSessionHandler('seekto', null);
            setMediaSessionHandler('seekforward', null);
            setMediaSessionHandler('seekbackward', null);
            setMediaSessionHandler('previoustrack', canNavigateQueue && queueCurrentIndex() > 0
                ? function () { playQueueOffsetViaActivePlaylist(-1); }
                : null);
            setMediaSessionHandler('nexttrack', canNavigateQueue && queueCurrentIndex() >= 0 && queueCurrentIndex() < queueTracks().length - 1
                ? function () { playQueueOffsetViaActivePlaylist(1); }
                : null);
        } else {
            setMediaSessionHandler('seekto', function (details) {
                var media = activeMusicMediaElement();
                if (!media || typeof details.seekTime !== 'number') return;
                media.currentTime = details.seekTime;
                updateMediaSessionPositionState();
            });
            setMediaSessionHandler('previoustrack', null);
            setMediaSessionHandler('nexttrack', null);
        }

        updateMediaSessionPositionState();

        try {
            var media = activeMusicMediaElement();
            if ('playbackState' in session) session.playbackState = media && media.paused ? 'paused' : 'playing';
        } catch (e) {}
    }

    function openBookmarksSection(sectionKey, title) {
        Lampa.Activity.push({
            title: title || 'Закладки',
            component: 'lampac_music_section',
            section_key: sectionKey,
            section_title: title || 'Закладки',
            page: 1,
            noinfo: true
        });
    }

    // ===== SEARCH DATA =====

    function normalizeSearchCacheKey(query) {
        return String(query || '').trim().toLowerCase();
    }

    function getExpandedSearchGroup(query, type) {
        var key = normalizeSearchCacheKey(query);
        var cache = key ? SEARCH_EXPANDED_CACHE[key] : null;
        if (!cache || !type)
            return null;

        if (Array.isArray(cache[type]) && cache[type].length)
            return cache[type].slice();

        if (Array.isArray(cache.sections)) {
            for (var i = 0; i < cache.sections.length; i++) {
                var section = cache.sections[i];
                if (section && section.id === type && Array.isArray(section.entries) && section.entries.length)
                    return section.entries.slice();
            }
        }

        return null;
    }

    function loadExpandedSearch(query, callback) {
        var key = normalizeSearchCacheKey(query);
        if (!key) {
            if (callback) callback(null);
            return;
        }

        if (SEARCH_EXPANDED_CACHE[key]) {
            if (callback) callback(SEARCH_EXPANDED_CACHE[key]);
            return;
        }

        if (callback) {
            if (!SEARCH_EXPANDED_WAITERS[key]) SEARCH_EXPANDED_WAITERS[key] = [];
            SEARCH_EXPANDED_WAITERS[key].push(callback);
        }

        if (SEARCH_EXPANDED_PENDING[key]) return;

        SEARCH_EXPANDED_PENDING[key] = true;
        request(MUSIC.endpoints.search + '?q=' + encodeURIComponent(query) + '&full=true', function (json) {
            delete SEARCH_EXPANDED_PENDING[key];
            SEARCH_EXPANDED_CACHE[key] = normalizeSearchResults(parseJson(json) || {});
            var waiters = SEARCH_EXPANDED_WAITERS[key] || [];
            delete SEARCH_EXPANDED_WAITERS[key];
            waiters.forEach(function (fn) {
                fn(SEARCH_EXPANDED_CACHE[key]);
            });
        }, function () {
            delete SEARCH_EXPANDED_PENDING[key];
            var waiters = SEARCH_EXPANDED_WAITERS[key] || [];
            delete SEARCH_EXPANDED_WAITERS[key];
            waiters.forEach(function (fn) {
                fn(null);
            });
        });
    }

    function warmExpandedSearch(query) {
        loadExpandedSearch(query);
    }

    function hasPendingSearchMetadata(groups) {
        return !!(groups && groups.metadata_pending);
    }

    function normalizeEntriesPagination(options) {
        var pagination = options && options.pagination ? options.pagination : null;
        if (!pagination || !pagination.next_page || !pagination.section_id) return null;

        return {
            kind: pagination.kind || 'artist_section',
            section_id: pagination.section_id,
            provider: pagination.provider || '',
            next_page: pagination.next_page,
            limit: pagination.limit || HOME_SECTION_LIMIT
        };
    }

    function openSearchSection(title, entries, options) {
        var resolvedEntries = Array.isArray(entries) ? entries.slice() : [];
        var pagination = normalizeEntriesPagination(options);

        function pushSection(list, paging) {
            Lampa.Activity.push({
                title: title || 'Результаты',
                component: 'lampac_music_entries',
                entries_title: title || 'Результаты',
                entries: list,
                entries_pagination: paging || null,
                page: 1,
                noinfo: true
            });
        }

        if (options && options.query && options.type) {
            var expanded = getExpandedSearchGroup(options.query, options.type);
            if (expanded && expanded.length > resolvedEntries.length) {
                pushSection(expanded, null);
                return;
            }

            loadExpandedSearch(options.query, function (cache) {
                var nextEntries = resolvedEntries;
                if (cache && cache[options.type] && cache[options.type].length > resolvedEntries.length)
                    nextEntries = cache[options.type].slice();

                pushSection(nextEntries, null);
            });
            return;
        }

        pushSection(resolvedEntries, pagination);
    }

    function openBookmarksScreen() {
        Lampa.Activity.push({
            title: 'Закладки',
            component: 'lampac_music_bookmarks',
            page: 1,
            noinfo: true
        });
    }

    function activePlayerData() {
        if (!Lampa.Player || typeof Lampa.Player.playdata !== 'function') return null;
        return Lampa.Player.playdata() || null;
    }

    function activePlaylist() {
        if (!Lampa.PlayerPlaylist || typeof Lampa.PlayerPlaylist.get !== 'function') return [];

        var playlist = Lampa.PlayerPlaylist.get();
        return Array.isArray(playlist) ? playlist : [];
    }

    function canReuseStandaloneIosQueue(tracks) {
        var currentTracks = Array.isArray(MUSIC_IOS_AUDIO.tracks) ? MUSIC_IOS_AUDIO.tracks : [];

        if (!isStandaloneIosAudioActive()) return false;
        if (!tracks || !tracks.length || tracks.length !== currentTracks.length) return false;

        for (var i = 0; i < tracks.length; i++) {
            if (!tracks[i] || !currentTracks[i] || tracks[i].id !== currentTracks[i].id)
                return false;
        }

        return true;
    }

    function canReuseActiveQueue(tracks) {
        var current = activePlayerData();
        var playlist = activePlaylist();

        if (!current || !current.from_music_cluster) return false;
        if (!tracks || !tracks.length || playlist.length !== tracks.length) return false;

        for (var i = 0; i < tracks.length; i++) {
            if (!playlist[i] || playlist[i].music_track_id !== tracks[i].id) return false;
        }

        return true;
    }

    function selectFromActiveQueue(index) {
        var playlist = activePlaylist();
        var item = playlist[index];

        if (!item || !Lampa.PlayerPlaylist || !Lampa.PlayerPlaylist.listener || typeof Lampa.PlayerPlaylist.listener.send !== 'function')
            return false;

        Lampa.PlayerPlaylist.listener.send('select', {
            playlist: playlist,
            position: index,
            item: item
        });

        return true;
    }

    function selectFromStandaloneIosQueue(index) {
        return standaloneIosPlayIndex(index);
    }

    function requestPlay(track, done, fail) {
        bumpMusicHeatMetric('playRequest');

        var cached = getCachedPlayResponse(track);
        if (cached) {
            bumpMusicHeatMetric('playCacheHit');
            if (done) done(cached);
            return;
        }

        var key = buildPlayCacheKey(track);
        if (PLAY_PREFETCH_PENDING[key]) {
            bumpMusicHeatMetric('playPendingJoin');
            PLAY_PREFETCH_PENDING[key].push({
                done: done,
                fail: fail
            });
            return;
        }

        bumpMusicHeatMetric('playNetwork');
        PLAY_PREFETCH_PENDING[key] = [{
            done: done,
            fail: fail
        }];

        request(buildPlayUrl(track), function (json) {
            var parsed = parseJson(json);
            var callbacks = PLAY_PREFETCH_PENDING[key] || [];
            delete PLAY_PREFETCH_PENDING[key];

            if (parsed && parsed.available && parsed.sources && parsed.sources.length) {
                saveCachedPlayResponse(track, parsed, key);
                callbacks.forEach(function (cb) {
                    if (cb.done) cb.done(parsed);
                });
                return;
            }

            callbacks.forEach(function (cb) {
                if (cb.fail) cb.fail(parsed);
            });
        }, function () {
            var callbacks = PLAY_PREFETCH_PENDING[key] || [];
            delete PLAY_PREFETCH_PENDING[key];
            callbacks.forEach(function (cb) {
                if (cb.fail) cb.fail(null);
            });
        });
    }

    function markTrackPlayed(track) {
        if (!track || !track.id) return;

        touchHomeCacheEntry('recently_played', mapTrackCard(track), RECENT_SECTION_STORAGE_LIMIT);
        updateHomeSectionMetaFromCache('recently_played');
        emitRecentChanged('recently_played', track);

        // count_play — только здесь: это честный play-путь (после задержки
        // реального воспроизведения); refreshRecentlyPlayedTrack обновляет
        // payload без прослушивания и счётчик статистики не трогает
        requestPost(MUSIC.endpoints.markHistory, buildTrackRequestParams(track) + '&count_play=true', function () {}, function () {});
    }

    function refreshRecentlyPlayedTrack(track) {
        if (!track || !track.id) return;

        touchHomeCacheEntry('recently_played', mapTrackCard(track), RECENT_SECTION_STORAGE_LIMIT);
        updateHomeSectionMetaFromCache('recently_played');
        emitRecentChanged('recently_played', track);
        requestPost(MUSIC.endpoints.markHistory, buildTrackRequestParams(track), function () {}, function () {});
    }

    function findQueueTrackById(trackId) {
        var tracks = queueTracks();

        for (var i = 0; i < tracks.length; i++) {
            if (tracks[i] && tracks[i].id === trackId)
                return tracks[i];
        }

        return null;
    }

    function findPlaylistItemByTrackId(trackId) {
        var playlist = activePlaylist();

        for (var i = 0; i < playlist.length; i++) {
            if (playlist[i] && playlist[i].music_track_id === trackId)
                return playlist[i];
        }

        return null;
    }

    function buildTrackFromPlaybackData(data) {
        if (!data || !data.music_track_id) return null;

        var image = data.img || data.poster || '';

        return {
            id: data.music_track_id,
            title: data.title || 'Track',
            artist_name: data.artist || '',
            album_title: data.music_album_title || '',
            duration_ms: data.music_duration_ms || (data.music_duration ? Math.round(Number(data.music_duration || 0) * 1000) : 0),
            image: image,
            images: image ? [{ url: image }] : []
        };
    }

    function trackFromPlaybackData(data) {
        if (!data || !data.music_track_id) return null;

        return findQueueTrackById(data.music_track_id)
            || buildTrackFromPlaybackData(findPlaylistItemByTrackId(data.music_track_id))
            || buildTrackFromPlaybackData(data);
    }

    function buildTimeline(track) {
        var duration = track && track.duration_ms ? Math.max(0, Math.round(track.duration_ms / 1000)) : 0;

        return {
            hash: 'music:' + (track && track.id ? track.id : Lampa.Utils.uid()),
            time: 0,
            percent: 0,
            duration: duration,
            stop_recording: true
        };
    }

    function buildQualityMap(json) {
        return json.sources.reduce(function (acc, item) {
            if (item && item.quality && item.url) acc[item.quality] = item.url;
            return acc;
        }, {});
    }

    // WebKit не умеет перематывать webm по range (seek-индекс в конце файла):
    // для standalone ios-пути m4a/mp4 в приоритете, webm — последний; внутри
    // групп сохраняется серверный порядок (качество/режим пользователя)
    function iosSourceRank(source) {
        var mime = String(source && source.mime_type || '').toLowerCase();
        var quality = String(source && source.quality || '').toLowerCase();

        if (mime.indexOf('mp4') >= 0 || quality.indexOf('m4a') >= 0 || quality.indexOf('mp4') >= 0) return 0;
        if (mime.indexOf('webm') >= 0 || quality.indexOf('webm') >= 0) return 2;
        return 1;
    }

    function pickPlaybackSource(sources) {
        if (!Array.isArray(sources) || !sources.length) return null;
        if (!shouldUseStandaloneIosAudio()) return sources[0];

        return sources.slice().sort(function (a, b) {
            return iosSourceRank(a) - iosSourceRank(b);
        })[0];
    }

    // у треков из discovery-лент (VK-чарт) метаданные не содержат длительность:
    // без якоря sanity-фильтр не ловит удвоенный парс fMP4, время на экране врёт,
    // а synthetic-ended не срабатывает; длительность матча — надёжный источник
    function backfillTrackDurationFromMatch(track, json) {
        if (!track || !json) return;

        var matchDuration = json.selected_match && json.selected_match.duration_ms ? Number(json.selected_match.duration_ms) : 0;
        if (!matchDuration || matchDuration <= 0) return;

        if (!track.duration_ms || track.duration_ms <= 0)
            track.duration_ms = matchDuration;
    }

    function buildResolvedPlayback(track, json) {
        backfillTrackDurationFromMatch(track, json);

        var source = pickPlaybackSource(json.sources) || json.sources[0];
        var image = trackImage(track);
        var useExternalUrl = !usesInternalPlaybackFlow() && source.external_url;

        return {
            from_music_cluster: true,
            music_track_id: track && track.id ? track.id : null,
            music_playback_mode: getPlaybackMode(),
            music_youtube_id: extractResolvedYouTubeTrackId(track, json),
            music_album_title: track && track.album_title ? track.album_title : '',
            music_duration_ms: track && track.duration_ms ? track.duration_ms : 0,
            music_duration: track && track.duration_ms ? Math.max(0, Math.round(track.duration_ms / 1000)) : 0,
            timeline: buildTimeline(track),
            title: track.title || 'Track',
            poster: image,
            img: image,
            artist: track.artist_name || '',
            url: useExternalUrl ? source.external_url : source.url,
            headers: source.headers || {},
            quality: buildQualityMap(json)
        };
    }

    function buildExternalPlayback(track) {
        var image = trackImage(track);

        return {
            from_music_cluster: true,
            music_track_id: track && track.id ? track.id : null,
            music_playback_mode: getPlaybackMode(),
            music_youtube_id: extractYouTubeTrackId(track),
            music_album_title: track && track.album_title ? track.album_title : '',
            music_duration_ms: track && track.duration_ms ? track.duration_ms : 0,
            music_duration: track && track.duration_ms ? Math.max(0, Math.round(track.duration_ms / 1000)) : 0,
            timeline: buildTimeline(track),
            title: track.title || 'Track',
            poster: image,
            img: image,
            artist: track.artist_name || '',
            url: buildStreamUrl(track)
        };
    }

    function buildPlayback(track) {
        var image = trackImage(track);
        var playback = {
            from_music_cluster: true,
            music_track_id: track && track.id ? track.id : null,
            music_playback_mode: getPlaybackMode(),
            music_youtube_id: extractYouTubeTrackId(track),
            music_album_title: track && track.album_title ? track.album_title : '',
            music_duration_ms: track && track.duration_ms ? track.duration_ms : 0,
            music_duration: track && track.duration_ms ? Math.max(0, Math.round(track.duration_ms / 1000)) : 0,
            timeline: buildTimeline(track),
            title: track.title || 'Track',
            poster: image,
            img: image,
            artist: track.artist_name || '',
            url: function (call) {
                requestPlay(track, function (json) {
                    backfillTrackDurationFromMatch(track, json);

                    var source = pickPlaybackSource(json.sources) || json.sources[0];
                    playback.url = source.url;
                    playback.headers = source.headers || {};
                    playback.quality = buildQualityMap(json);
                    playback.music_playback_mode = getPlaybackMode();
                    playback.music_youtube_id = extractResolvedYouTubeTrackId(track, json);
                    playback.music_duration = track.duration_ms ? Math.max(0, Math.round(track.duration_ms / 1000)) : playback.music_duration;
                    playback.music_duration_ms = track.duration_ms || playback.music_duration_ms;
                    call();
                }, function (json) {
                    playback.url = '';
                    Lampa.Noty.show(json && json.message ? json.message : 'Источник для трека пока не найден.');
                    call();
                });
            }
        };

        return playback;
    }

    var musicPlayerPanelMedia = null;
    var musicPlayerPanelHandler = null;
    var musicPlayerPanelRetry = 0;
    var musicPlayerVisualContainer = null;
    var musicPlayerVisualKey = '';
    var musicPlayerVisualMode = 'art';
    var musicPlayerVisualLyricsRequest = '';
    var musicPlayerVisualToggleAt = 0;
    var musicPlayerVisualAutoScrollAt = 0;
    var musicPlayerVisualCleanupToken = 0;
    var musicPlayerPanelRebindToken = 0;
    var musicPlayerPanelEndedCleanup = 0;
    var musicPlayerPanelSyntheticEndKey = '';
    var pendingInternalPlaylist = null;
    var pendingPlayedTrack = null;
    var historyPlaybackTrackId = '';

    function hasMusicPlayerVisualArtifacts() {
        return $('.lm-player-visual, .player-panel__music-lyrics, .player-video.lm-player-video--music').length > 0;
    }

    function removeMusicPlayerVisual() {
        $('.lm-player-visual').remove();
        $('.player-panel__music-lyrics').remove();
        $('.player-video.lm-player-video--music').removeClass('lm-player-video--music');
        musicPlayerVisualContainer = null;
        musicPlayerVisualKey = '';
        musicPlayerVisualMode = 'art';
        musicPlayerVisualLyricsRequest = '';
        musicPlayerVisualToggleAt = 0;
        musicPlayerVisualAutoScrollAt = 0;
    }

    function invalidateMusicPlayerVisualCleanup() {
        musicPlayerVisualCleanupToken++;
    }

    function scheduleMusicPlayerVisualCleanup(origin, force) {
        var token = ++musicPlayerVisualCleanupToken;

        [0, 80, 250, 700, 1500].forEach(function (delay) {
            setTimeout(function () {
                if (token !== musicPlayerVisualCleanupToken) return;

                var data = activePlayerData();
                if (force || !shouldRenderMusicPlayerVisual(data)) {
                    removeMusicPlayerVisual();
                    traceEmbeddedIos('visual-cleanup', (origin || '') + ' delay=' + delay, true);
                }
            }, delay);
        });
    }

    function stopMusicPlayerPanelFix() {
        if (musicPlayerPanelRetry) {
            clearTimeout(musicPlayerPanelRetry);
            musicPlayerPanelRetry = 0;
        }

        if (musicPlayerPanelEndedCleanup) {
            clearTimeout(musicPlayerPanelEndedCleanup);
            musicPlayerPanelEndedCleanup = 0;
        }

        if (musicPlayerPanelMedia && musicPlayerPanelHandler) {
            musicPlayerPanelMedia.removeEventListener('timeupdate', musicPlayerPanelHandler);
            musicPlayerPanelMedia.removeEventListener('loadedmetadata', musicPlayerPanelHandler);
            musicPlayerPanelMedia.removeEventListener('durationchange', musicPlayerPanelHandler);
            musicPlayerPanelMedia.removeEventListener('playing', musicPlayerPanelHandler);
            musicPlayerPanelMedia.removeEventListener('pause', musicPlayerPanelHandler);
            musicPlayerPanelMedia.removeEventListener('seeking', musicPlayerPanelHandler);
            musicPlayerPanelMedia.removeEventListener('seeked', musicPlayerPanelHandler);
            musicPlayerPanelMedia.removeEventListener('ended', musicPlayerPanelHandler);
            musicPlayerPanelMedia.removeEventListener('error', musicPlayerPanelHandler);
        }

        musicPlayerPanelMedia = null;
        musicPlayerPanelHandler = null;
        musicPlayerPanelSyntheticEndKey = '';
        clearEmbeddedIosLockscreenSupport('panel-stop');
        removeMusicPlayerVisual();
    }

    function resetMusicPlayerBridgeForNonMusic(origin) {
        clearPendingInternalPlaylist();
        clearPendingTrackPlayed();
        stopMusicPlayerPanelFix();
        scheduleMusicPlayerVisualCleanup(origin || 'non-music', true);
        syncMusicMediaSession(null);
        traceEmbeddedIos('non-music-reset', origin || '', true);
    }

    function scheduleMusicPlayerPanelEndCleanup(origin) {
        if (musicPlayerPanelEndedCleanup)
            clearTimeout(musicPlayerPanelEndedCleanup);

        musicPlayerPanelEndedCleanup = setTimeout(function () {
            musicPlayerPanelEndedCleanup = 0;

            var data = activePlayerData();
            var media = activeMusicMediaElement();

            if (!data || !data.from_music_cluster || !media || media.ended || media.error) {
                traceEmbeddedIos('panel-cleanup', origin || '', true);
                stopMusicPlayerPanelFix();
                syncMusicMediaSession(null);
            }
        }, 900);
    }

    function clearPendingInternalPlaylist() {
        pendingInternalPlaylist = null;
    }

    function clearPendingTrackPlayed() {
        if (pendingPlayedTrack && pendingPlayedTrack.timer) {
            clearTimeout(pendingPlayedTrack.timer);
        }

        pendingPlayedTrack = null;
    }

    function scheduleTrackPlayed(track) {
        if (!track || !track.id) {
            clearPendingTrackPlayed();
            return;
        }

        clearPendingTrackPlayed();
        historyPlaybackTrackId = track.id;

        pendingPlayedTrack = {
            track: track,
            trackId: track.id,
            timer: 0,
            armed: false
        };
    }

    function armPendingTrackPlayed(trackId) {
        if (!pendingPlayedTrack || !trackId || pendingPlayedTrack.trackId !== trackId)
            return false;

        if (pendingPlayedTrack.timer)
            return true;

        pendingPlayedTrack.armed = true;
        pendingPlayedTrack.timer = setTimeout(function () {
            if (!pendingPlayedTrack || pendingPlayedTrack.trackId !== trackId)
                return;

            var track = pendingPlayedTrack.track;
            clearPendingTrackPlayed();
            markTrackPlayed(track);
        }, MUSIC_HISTORY_MARK_DELAY);

        return true;
    }

    function flushPendingTrackPlayed(data) {
        if (!pendingPlayedTrack || !data || !data.from_music_cluster) return false;
        if (!data.music_track_id || data.music_track_id !== pendingPlayedTrack.trackId) return false;

        return armPendingTrackPlayed(data.music_track_id);
    }

    function syncActivePlaybackHistory(data) {
        if (!data || !data.from_music_cluster || !data.music_track_id) return false;

        var track = trackFromPlaybackData(data);
        if (!track || !track.id) return false;

        if (MUSIC_QUEUE.currentTrackId !== data.music_track_id)
            updateQueueCurrent(data.music_track_id);

        if (historyPlaybackTrackId !== data.music_track_id)
            scheduleTrackPlayed(track);

        return flushPendingTrackPlayed({
            from_music_cluster: true,
            music_track_id: data.music_track_id
        });
    }

    function scheduleInternalPlaylist(track, list) {
        if (!track || !track.id || !list || !list.length) {
            traceEmbeddedIos('playlist-schedule-reject', 'track=' + !!(track && track.id) + ' list=' + (list ? list.length : 0), true);
            pendingInternalPlaylist = null;
            return;
        }

        traceEmbeddedIos('playlist-schedule', 'id=' + track.id + ' len=' + list.length, true);
        pendingInternalPlaylist = {
            trackId: track.id,
            list: list
        };
    }

    function flushPendingInternalPlaylist(trackId) {
        if (!pendingInternalPlaylist || !trackId) {
            traceEmbeddedIos('playlist-flush-skip', 'pending=' + !!pendingInternalPlaylist + ' id=' + (trackId || ''), true);
            return false;
        }
        if (pendingInternalPlaylist.trackId !== trackId) {
            traceEmbeddedIos('playlist-flush-mismatch', 'want=' + pendingInternalPlaylist.trackId + ' got=' + trackId, true);
            return false;
        }

        var list = pendingInternalPlaylist.list;
        pendingInternalPlaylist = null;

        if (list && list.length) {
            Lampa.Player.playlist(list);
            traceEmbeddedIos('playlist-flush-ok', 'len=' + list.length, true);
            syncMusicMediaSession(activePlayerData());
        }
        return true;
    }

    // ===== LAMPA PLAYER BRIDGE =====

    function attachInternalPlaylistDeferred(trackId, list) {
        if (!trackId || !list || !list.length) return;

        [80, 300].forEach(function (delay) {
            setTimeout(function () {
                var work = activePlayerData();
                if (!work || !work.from_music_cluster || work.music_track_id !== trackId) {
                    traceEmbeddedIos('playlist-attach-bail', 'delay=' + delay + ' work=' + !!work
                        + ' cluster=' + !!(work && work.from_music_cluster)
                        + ' id=' + (work && work.music_track_id || ''), true);
                    return;
                }
                Lampa.Player.playlist(list);
                traceEmbeddedIos('playlist-attach-ok', 'delay=' + delay + ' len=' + list.length, true);
                syncMusicMediaSession(work);
            }, delay);
        });
    }

    function applyMusicPlayerPanelFix(lightUpdate) {
        var work = Lampa.Player.playdata();

        if (!work || !work.from_music_cluster) {
            stopMusicPlayerPanelFix();
            return;
        }

        bumpMusicHeatMetric('embeddedPanelFix');

        var visualMissing = !musicPlayerVisualContainer
            || !document.documentElement.contains(musicPlayerVisualContainer)
            || !$('.lm-player-visual').length;

        if (!lightUpdate || visualMissing)
            renderMusicPlayerVisual(work);

        var duration = work.music_duration || (work.timeline && work.timeline.duration) || 0;
        if (duration) {
            var media = musicPlayerPanelMedia || document.querySelector('.player-video video, .player-video audio');
            var current = media && isFinite(media.currentTime) ? media.currentTime : ((work.timeline && work.timeline.time) || 0);
            var displayCurrent = Math.max(0, Math.min(duration, current));
            var percent = duration > 0 ? Math.max(0, Math.min(100, displayCurrent / duration * 100)) : 0;

            // Штатный PlayerPanel.update двигает именно тот слой, по которому
            // Lampa рисует прогресс. Внутренний div нужен только для ручки.
            requestAnimationFrame(function () {
                if (Lampa.PlayerPanel && typeof Lampa.PlayerPanel.update === 'function') {
                    Lampa.PlayerPanel.update('timenow', Lampa.Utils.secondsToTime(displayCurrent));
                    Lampa.PlayerPanel.update('timeend', Lampa.Utils.secondsToTime(duration));
                    Lampa.PlayerPanel.update('position', percent + '%');
                } else {
                    $('.player-panel__timenow').text(Lampa.Utils.secondsToTime(displayCurrent));
                    $('.player-panel__timeend').text(Lampa.Utils.secondsToTime(duration));
                    $('.player-panel__position').css({ width: percent + '%' });
                }

                $('.player-panel__position > div').css({ width: '' });
            });
        }

        scheduleQueueSnapshotSave(false);
        updateMusicPlayerVisualLyricsHighlight(false);
    }

    function handleMusicPlayerPanelMouseRewind(event) {
        var work = Lampa.Player.playdata();
        if (!work || !work.from_music_cluster || !event) return;

        if (event.time && typeof event.time.addClass === 'function')
            event.time.addClass('hide').text('');

        if (typeof event.percent !== 'number') return;

        var duration = Number(work.music_duration || (work.timeline && work.timeline.duration) || 0);
        if (!duration) return;

        var media = musicPlayerPanelMedia || document.querySelector('.player-video video, .player-video audio');
        var nativeDuration = media && isFinite(media.duration) ? Number(media.duration) : 0;
        var percent = Math.max(0, Math.min(1, event.percent));
        var target = Math.max(0, Math.min(duration, duration * percent));

        if (event.method !== 'click') return;
        if (!nativeDuration || Math.abs(nativeDuration - duration) < 1) return;

        musicPlayerPanelSyntheticEndKey = '';

        if (Lampa.PlayerVideo && typeof Lampa.PlayerVideo.to === 'function')
            Lampa.PlayerVideo.to(target);
        else if (media)
            media.currentTime = target;
    }

    function handleMusicPlayerPanelEffectiveEnd(event) {
        if (!event || event.type !== 'timeupdate') return false;

        var work = activePlayerData();
        if (!work || !work.from_music_cluster) return false;

        var duration = Number(work.music_duration || (work.timeline && work.timeline.duration) || 0);
        if (!duration) return false;

        var media = musicPlayerPanelMedia || document.querySelector('.player-video video, .player-video audio');
        var current = media && isFinite(media.currentTime) ? Number(media.currentTime) : 0;
        if (!media || media.paused || media.ended || current < duration - 0.25) return false;

        var trackKey = [
            work.music_track_id || '',
            MUSIC_QUEUE.currentTrackId || '',
            queueCurrentIndex(),
            duration
        ].join('|');

        if (musicPlayerPanelSyntheticEndKey === trackKey) return true;
        musicPlayerPanelSyntheticEndKey = trackKey;

        traceEmbeddedIos('panel-effective-ended', 'time=' + (Math.round(current * 100) / 100) + '/' + duration, true);

        try {
            media.currentTime = Math.min(duration, media.duration && isFinite(media.duration) ? media.duration : duration);
        } catch (e) {}

        try {
            if (Lampa.PlayerVideo && typeof Lampa.PlayerVideo.pause === 'function')
                Lampa.PlayerVideo.pause();
            else
                media.pause();
        } catch (e) {
            try { media.pause(); } catch (_) {}
        }

        applyMusicPlayerPanelFix(false);

        if (playEmbeddedQueueOffset(1))
            return true;

        updateEmbeddedIosPlaybackState();
        return true;
    }

    function scheduleMusicPlayerPanelRebind(origin) {
        var token = ++musicPlayerPanelRebindToken;

        [0, 80, 220, 500, 1000].forEach(function (delay) {
            setTimeout(function () {
                if (token !== musicPlayerPanelRebindToken) return;

                var work = activePlayerData();
                if (!work || !work.from_music_cluster) return;

                var media = document.querySelector('.player-video video, .player-video audio');
                var container = document.querySelector('.player-video');
                var visualMissing = !container || !container.querySelector('.lm-player-visual');
                var mediaChanged = !!(media && media !== musicPlayerPanelMedia);

                if (mediaChanged) {
                    traceEmbeddedIos('panel-rebind-media', origin || '', true);
                    startMusicPlayerPanelFix(work);
                    return;
                }

                if (visualMissing) {
                    traceEmbeddedIos('panel-rebind-visual', origin || '', true);
                    renderMusicPlayerVisual(work);
                }

                applyMusicPlayerPanelFix(false);
                syncMusicMediaSession(work);
                updateMediaSessionPositionState();
            }, delay);
        });
    }

    function musicPlayerLyricsTrackKey(data) {
        if (!data) return '';

        return buildLyricsCacheKey(
            data.music_track_id || '',
            data.title || '',
            data.artist || '',
            data.music_album_title || '',
            musicPlayerLyricsDurationMs(data),
            data.music_youtube_id || ''
        );
    }

    function musicPlayerLyricsDurationMs(data) {
        if (!data) return 0;

        return data.music_duration_ms
            || (data.music_duration ? Math.max(0, Math.round(Number(data.music_duration) * 1000)) : 0);
    }

    function buildMusicPlayerLyricsUrl(data) {
        return buildLyricsUrl(
            data.title || '',
            data.artist || '',
            data.music_album_title || '',
            musicPlayerLyricsDurationMs(data),
            data.music_youtube_id || ''
        );
    }

    function renderMusicPlayerLyrics(box, data, json) {
        if (!box || !box.length) return;

        var body = box.find('.lm-player-visual__lyrics-scroll');
        if (!body.length) return;

        box.attr('data-lyrics-track', musicPlayerLyricsTrackKey(data));
        box.removeData('lyricsLineMeta');
        body.empty();
        box.removeAttr('data-lyrics-line');
        box.removeAttr('data-lyrics-manual');
        box.removeClass('lm-player-visual--lyrics-manual');

        var hasLines = json && json.available && Array.isArray(json.lines) && json.lines.length;
        var hasPlain = json && json.available && json.plain;

        if (!hasLines && !hasPlain) {
            body.append($('<div class="lm-player-visual__lyrics-message"></div>').text(json && json.retry ? 'Сервис текстов не ответил. Попробуй ещё раз.' : 'Текст не найден.'));
            return;
        }

        if (json.synced && hasLines) {
            json.lines.forEach(function (line, index) {
                var row = $('<div class="lm-player-visual__lyrics-line"></div>');

                row.attr('data-line', index);
                row.attr('data-time', line.time_ms || 0);

                if (line.text) row.text(line.text);
                else row.addClass('lm-player-visual__lyrics-line--empty').text('♪');

                body.append(row);
            });

            box.data('lyricsLineMeta', buildLyricsLineMeta(body, '.lm-player-visual__lyrics-line'));
            updateMusicPlayerVisualLyricsHighlight(true);
            return;
        }

        body.append($('<div class="lm-player-visual__lyrics-plain"></div>').text(json.plain || json.lines.map(function (line) {
            return line.text || '';
        }).join('\n')));
    }

    function loadMusicPlayerLyrics(data, box) {
        var trackKey = musicPlayerLyricsTrackKey(data);
        if (!trackKey || !box || !box.length) return;

        if (box.attr('data-lyrics-track') === trackKey && box.find('.lm-player-visual__lyrics-scroll').children().length)
            return;

        if (MUSIC_LYRICS_CACHE[trackKey]) {
            renderMusicPlayerLyrics(box, data, MUSIC_LYRICS_CACHE[trackKey]);
            return;
        }

        if (musicPlayerVisualLyricsRequest === trackKey) return;
        musicPlayerVisualLyricsRequest = trackKey;
        bumpMusicHeatMetric('lyricsRequest');

        box.find('.lm-player-visual__lyrics-scroll')
            .empty()
            .append($('<div class="lm-player-visual__lyrics-message"></div>').text('Ищу текст...'));

        request(buildMusicPlayerLyricsUrl(data), function (json) {
            json = parseJson(json);
            if (json && json.available) setCappedCacheEntry(MUSIC_LYRICS_CACHE, trackKey, json, 60);

            if (musicPlayerLyricsTrackKey(activePlayerData()) !== trackKey) return;
            musicPlayerVisualLyricsRequest = '';
            renderMusicPlayerLyrics(box, data, json);
        }, function () {
            if (musicPlayerLyricsTrackKey(activePlayerData()) !== trackKey) return;
            musicPlayerVisualLyricsRequest = '';
            renderMusicPlayerLyrics(box, data, null);
        });
    }

    function updateMusicPlayerVisualLyricsHighlight(force) {
        if (musicPlayerVisualMode !== 'lyrics') return;

        var box = $('.lm-player-visual');
        if (!box.length || !box.hasClass('lm-player-visual--lyrics')) return;

        var media = activeMusicMediaElement();
        if (!media || !isFinite(media.currentTime)) return;

        var meta = box.data('lyricsLineMeta');
        if (!meta || !meta.elements || !meta.elements.length) {
            meta = buildLyricsLineMeta(box, '.lm-player-visual__lyrics-line');
            box.data('lyricsLineMeta', meta);
        }

        if (!meta.elements.length) return;

        var timeMs = media.currentTime * 1000;
        var activeIndex = findLyricsLineIndex(meta.times, timeMs);

        if (!force && box.attr('data-lyrics-line') === String(activeIndex)) return;
        box.attr('data-lyrics-line', String(activeIndex));

        var element = activateLyricsLine(meta, activeIndex);
        if (!element) return;
        if (!force && box.attr('data-lyrics-manual') === '1') return;

        if (element && typeof element.scrollIntoView === 'function') {
            try {
                musicPlayerVisualAutoScrollAt = Date.now();
                element.scrollIntoView({ block: 'center', behavior: force ? 'auto' : 'smooth' });
            } catch (e) {
                musicPlayerVisualAutoScrollAt = Date.now();
                element.scrollIntoView();
            }
        }
    }

    function markMusicPlayerLyricsManual(box) {
        if (!box || !box.length || musicPlayerVisualMode !== 'lyrics') return;

        box.attr('data-lyrics-manual', '1');
        box.addClass('lm-player-visual--lyrics-manual');
    }

    function resumeMusicPlayerLyricsAutoScroll() {
        var box = $('.lm-player-visual');
        if (!box.length) return;

        box.removeAttr('data-lyrics-manual');
        box.removeClass('lm-player-visual--lyrics-manual');
        updateMusicPlayerVisualLyricsHighlight(true);
    }

    function toggleMusicPlayerVisualLyrics() {
        var now = Date.now();
        if (now - musicPlayerVisualToggleAt < 280) return;
        musicPlayerVisualToggleAt = now;

        var data = activePlayerData();
        if (!shouldRenderMusicPlayerVisual(data)) return;

        musicPlayerVisualMode = musicPlayerVisualMode === 'lyrics' ? 'art' : 'lyrics';
        renderMusicPlayerVisual(data);
    }

    function ensureMusicPlayerPanelLyricsButton() {
        var panel = $('.player-panel__right');
        if (!panel.length) return;

        var button = panel.find('.player-panel__music-lyrics');
        if (!button.length) {
            button = $('<div class="player-panel__music-lyrics button selector" data-controller="player_panel">Текст</div>');
            button.on('hover:enter click', function (event) {
                event.preventDefault();
                event.stopPropagation();
                toggleMusicPlayerVisualLyrics();
            });

            var playlistButton = panel.find('.player-panel__playlist');
            if (playlistButton.length) playlistButton.before(button);
            else panel.prepend(button);
        }

        button
            .toggleClass('active', musicPlayerVisualMode === 'lyrics')
            .text(musicPlayerVisualMode === 'lyrics' ? 'Обл.' : 'Текст');
    }

    function getMusicPlaybackModeFromData(data) {
        return String((data && data.music_playback_mode) || getPlaybackMode() || 'audio').toLowerCase();
    }

    // Contract: music audio owns the custom art/lyrics bridge; music video and
    // regular Lampa video must keep the stock video visible, so the bridge is
    // removed. The cleanup sweep exists because Lampa can recreate player DOM
    // shortly after Player.start, which otherwise leaves stale lyrics/art over
    // the next video.
    function shouldRenderMusicPlayerVisual(data) {
        return !!(data && data.from_music_cluster && getMusicPlaybackModeFromData(data) !== 'video');
    }

    function renderMusicPlayerVisual(data) {
        if (!shouldRenderMusicPlayerVisual(data)) {
            var hadVisualArtifacts = hasMusicPlayerVisualArtifacts();
            removeMusicPlayerVisual();
            if (data && data.from_music_cluster && hadVisualArtifacts)
                scheduleMusicPlayerVisualCleanup('music-video', true);
            return false;
        }

        invalidateMusicPlayerVisualCleanup();

        var container = document.querySelector('.player-video');
        if (!container) return false;

        if (musicPlayerVisualContainer && musicPlayerVisualContainer !== container)
            removeMusicPlayerVisual();

        var box = $('.lm-player-visual', container);
        if (!box.length) {
            box = $(
                '<div class="lm-player-visual">\
                    <div class="lm-player-visual__backdrop"></div>\
                    <div class="lm-player-visual__top">\
                        <div class="selector lm-player-visual__lyrics-toggle">Текст</div>\
                    </div>\
                    <div class="lm-player-visual__center">\
                        <div class="lm-player-visual__art-view">\
                            <div class="lm-player-visual__art"><img alt=""></div>\
                            <div class="lm-player-visual__title"></div>\
                            <div class="lm-player-visual__artist"></div>\
                        </div>\
                        <div class="lm-player-visual__lyrics-view">\
                            <div class="selector lm-player-visual__lyrics-follow">К строке</div>\
                            <div class="lm-player-visual__lyrics-scroll"></div>\
                        </div>\
                    </div>\
                </div>'
            );
            box.on('click hover:enter', '.lm-player-visual__lyrics-toggle', function (event) {
                event.preventDefault();
                event.stopPropagation();
                toggleMusicPlayerVisualLyrics();
            });
            box.on('touchmove wheel', '.lm-player-visual__lyrics-scroll', function () {
                markMusicPlayerLyricsManual(box);
            });
            box.on('scroll', '.lm-player-visual__lyrics-scroll', function () {
                if (Date.now() - musicPlayerVisualAutoScrollAt > 650)
                    markMusicPlayerLyricsManual(box);
            });
            box.on('click hover:enter', '.lm-player-visual__lyrics-follow', function (event) {
                event.preventDefault();
                event.stopPropagation();
                resumeMusicPlayerLyricsAutoScroll();
            });
            var display = $('.player-video__display', container);
            if (display.length) display.after(box);
            else $(container).append(box);
        }

        musicPlayerVisualContainer = container;
        $(container).addClass('lm-player-video--music');
        ensureMusicPlayerPanelLyricsButton();

        var image = data.img || data.poster || artwork('track', data.title || 'Музыка', data.artist || 'Music', ['#1d2128', '#46505e']);
        var title = data.title || 'Track';
        var artist = data.artist || '';
        var key = [data.music_track_id || '', image || '', title, artist].join('|');
        var trackChanged = musicPlayerVisualKey !== key;

        box.toggleClass('lm-player-visual--lyrics', musicPlayerVisualMode === 'lyrics');
        box.find('.lm-player-visual__lyrics-toggle')
            .toggleClass('active', musicPlayerVisualMode === 'lyrics')
            .text(musicPlayerVisualMode === 'lyrics' ? 'Обложка' : 'Текст');

        if (trackChanged) {
            musicPlayerVisualKey = key;
            box.removeData('lyricsLineMeta');
            box.removeAttr('data-lyrics-line');
            box.removeAttr('data-lyrics-track');
            box.removeAttr('data-lyrics-manual');
            box.removeClass('lm-player-visual--lyrics-manual');
            box.find('.lm-player-visual__lyrics-scroll').empty();
            box.find('.lm-player-visual__backdrop').css('background-image', 'url(' + JSON.stringify(image) + ')');
            box.find('.lm-player-visual__art img').attr('src', image);
            box.find('.lm-player-visual__title').text(title);
            box.find('.lm-player-visual__artist').text(artist);
        }

        if (musicPlayerVisualMode === 'lyrics')
            loadMusicPlayerLyrics(data, box);

        return true;
    }

    function startMusicPlayerPanelFix(data) {
        stopMusicPlayerPanelFix();

        if (!data || !data.from_music_cluster) return;

        function bind() {
            renderMusicPlayerVisual(activePlayerData() || data);

            var media = document.querySelector('.player-video video, .player-video audio');

            if (!media) {
                musicPlayerPanelRetry = setTimeout(bind, 100);
                return;
            }

            prepareEmbeddedIosMediaElement(media, 'panel-bind');
            musicPlayerPanelMedia = media;
            musicPlayerPanelHandler = function (event) {
                try {
                    handleMusicPlayerPanelEvent(event);
                } catch (e) {
                    traceEmbeddedIos('panel-handler-error', describeError(e), true);
                }
            };

            function handleMusicPlayerPanelEvent(event) {
                ensureLampaPlayerController(event && event.type ? ('panel-' + event.type) : 'panel-event');

                if (event && event.type === 'timeupdate')
                    bumpMusicHeatMetric('embeddedTimeupdate');

                if (event && (event.type === 'ended' || event.type === 'error')) {
                    traceEmbeddedIos('panel-' + event.type, '', true);
                    clearEmbeddedIosLockscreenSupport(event.type);
                    updateEmbeddedIosPlaybackState();
                    scheduleMusicPlayerPanelEndCleanup(event.type);
                    return;
                }

                if (event && event.type !== 'timeupdate') {
                    bumpMusicHeatMetric('embeddedPanelEvent');
                    traceEmbeddedIos('panel-' + event.type, '', true);
                }

                syncActivePlaybackHistory(activePlayerData());

                if (handleMusicPlayerPanelEffectiveEnd(event))
                    return;

                if (event && event.type === 'timeupdate' && document.hidden) {
                    bumpMusicHeatMetric('embeddedHiddenTimeupdate');
                    scheduleQueueSnapshotSave(false);
                    maybeRequestRadioAutoplay('embedded-hidden-timeupdate');
                    updateMediaSessionPositionState();
                    return;
                }

                applyMusicPlayerPanelFix(event && event.type === 'timeupdate');
                if (event && event.type === 'timeupdate')
                    maybeRequestRadioAutoplay('embedded-timeupdate');

                // timeupdate приходит часто. Полный sync пересоздаёт MediaMetadata и
                // handlers, что для iPhone заметно дороже, чем простой position update.
                if (!event || event.type !== 'timeupdate')
                    syncMusicMediaSession(activePlayerData());

                updateMediaSessionPositionState();
            }

            media.addEventListener('timeupdate', musicPlayerPanelHandler);
            media.addEventListener('loadedmetadata', musicPlayerPanelHandler);
            media.addEventListener('durationchange', musicPlayerPanelHandler);
            media.addEventListener('playing', musicPlayerPanelHandler);
            media.addEventListener('pause', musicPlayerPanelHandler);
            media.addEventListener('seeking', musicPlayerPanelHandler);
            media.addEventListener('seeked', musicPlayerPanelHandler);
            media.addEventListener('ended', musicPlayerPanelHandler);
            media.addEventListener('error', musicPlayerPanelHandler);

            applyMusicPlayerPanelFix();
            syncMusicMediaSession(activePlayerData());
            updateMediaSessionPositionState();
        }

        bind();
    }

    Lampa.Player.listener.follow('start', function (data) {
        traceEmbeddedIos('player-start-event', 'cluster=' + !!(data && data.from_music_cluster)
            + ' id=' + (data && data.music_track_id || '')
            + ' pending=' + (pendingInternalPlaylist ? pendingInternalPlaylist.trackId : ''), true);

        if (!pendingInternalPlaylist || !data || !data.from_music_cluster) return;
        if (!data.music_track_id || data.music_track_id !== pendingInternalPlaylist.trackId) return;

        setTimeout(function () {
            flushPendingInternalPlaylist(data.music_track_id);
        }, 0);
    });

    Lampa.Player.listener.follow('start', function (data) {
        setTimeout(function () {
            flushPendingTrackPlayed(data);
        }, 0);
    });

    Lampa.Player.listener.follow('external', function (data) {
        setTimeout(function () {
            flushPendingTrackPlayed(data);
        }, 0);
    });

    Lampa.Player.listener.follow('destroy', function () {
        clearPendingInternalPlaylist();
        clearPendingTrackPlayed();
    });

    function currentExternalPlayer() {
        return getMusicPlayerId();
    }

    function shouldUseManagedExternalPlaylist() {
        var player = currentExternalPlayer();

        if (Lampa.Platform.macOS())
            return ['mpv', 'iina', 'nplayer', 'infuse'].indexOf(player) >= 0;

        if (Lampa.Platform.desktop())
            return player === 'other';

        return false;
    }

    function usesInternalPlaybackFlow() {
        var player = currentExternalPlayer();

        if (!player) return true;
        if (player === 'inner' || player === 'lampa') return true;
        if (Lampa.Platform.is('apple') && player === 'ios') return true;

        return false;
    }

    function buildPlaylistTrackOrder(tracks, startIndex) {
        var ordered = [];

        for (var i = startIndex; i < tracks.length; i++) ordered.push(tracks[i]);
        for (var j = 0; j < startIndex; j++) ordered.push(tracks[j]);

        return ordered;
    }

    function buildExternalPlaylistUrl(tracks, startIndex) {
        var ordered = buildPlaylistTrackOrder(tracks, startIndex);
        var provider = '';
        var audioProviderId = getAudioProviderId();
        var playlistStrategy = '';
        var ids = ordered
            .map(function (entry) {
                return entry && entry.id ? entry.id : '';
            })
            .filter(Boolean)
            .join(',');

        if (ordered.length) {
            var firstProvider = getTrackProviderId(ordered[0]);
            if (firstProvider && ordered.every(function (entry) { return getTrackProviderId(entry) === firstProvider; }))
                provider = firstProvider;
        }

        if (audioProviderId === 'soundcloudaudio' && (Lampa.Platform.macOS() || Lampa.Platform.desktop()))
            playlistStrategy = 'resolved';

        var url = MUSIC.endpoints.playlist
            + '?ids=' + encodeURIComponent(ids)
            + '&audio_provider=' + encodeURIComponent(audioProviderId)
            + '&stream_mode=' + encodeURIComponent(getStreamMode())
            + '&playback_mode=' + encodeURIComponent(getPlaybackMode());

        if (provider)
            url += '&provider=' + encodeURIComponent(provider);

        if (playlistStrategy)
            url += '&playlist_strategy=' + encodeURIComponent(playlistStrategy);

        return withIdentity(url);
    }

    function trimPlaylistItem(playback) {
        return {
            title: playback.title,
            artist: playback.artist || '',
            img: playback.img || playback.poster || '',
            url: playback.url
        };
    }

    function buildExternalPlaylist(tracks, startIndex, currentJson, done) {
        var resolved = new Array(tracks.length);

        tracks.forEach(function (entry, entryIndex) {
            if (entryIndex === startIndex) {
                resolved[entryIndex] = trimPlaylistItem(buildResolvedPlayback(entry, currentJson));
                return;
            }

            resolved[entryIndex] = trimPlaylistItem(buildExternalPlayback(entry));
        });

        done(resolved.filter(Boolean));
    }

    function buildInternalPreparedPlaylist(tracks, startIndex, currentJson, done, providerId) {
        var resolved = new Array(tracks.length);
        var order = [];
        var pointer = 0;
        var active = 0;
        var concurrency = getPreparedPlaylistConcurrency(providerId);

        resolved[startIndex] = buildResolvedPlayback(tracks[startIndex], currentJson);

        for (var i = startIndex + 1; i < tracks.length; i++) order.push(i);
        for (var j = 0; j < startIndex; j++) order.push(j);

        function finish() {
            if (pointer >= order.length && active <= 0) {
                done(tracks.map(function (entry, index) {
                    return resolved[index] || buildPlayback(entry);
                }));
                return;
            }

            while (active < concurrency && pointer < order.length) {
                var entryIndex = order[pointer++];
                active++;

                (function (resolvedIndex) {
                    requestPlay(tracks[resolvedIndex], function (json) {
                        resolved[resolvedIndex] = buildResolvedPlayback(tracks[resolvedIndex], json);
                        active--;
                        finish();
                    }, function () {
                        resolved[resolvedIndex] = buildPlayback(tracks[resolvedIndex]);
                        active--;
                        finish();
                    });
                })(entryIndex);
            }
        }

        finish();
    }

    function resolvePreparedPlaylistEntries(tracks, indices, resolved, providerId, done, onResolved) {
        var pointer = 0;
        var active = 0;
        var concurrency = getPreparedPlaylistConcurrency(providerId);

        function finish() {
            if (pointer >= indices.length && active <= 0) {
                done();
                return;
            }

            while (active < concurrency && pointer < indices.length) {
                var resolvedIndex = indices[pointer++];
                active++;

                (function (entryIndex) {
                    requestPlay(tracks[entryIndex], function (json) {
                        resolved[entryIndex] = buildResolvedPlayback(tracks[entryIndex], json);
                        if (onResolved) onResolved(entryIndex, resolved[entryIndex]);
                        active--;
                        finish();
                    }, function () {
                        resolved[entryIndex] = buildPlayback(tracks[entryIndex]);
                        if (onResolved) onResolved(entryIndex, resolved[entryIndex]);
                        active--;
                        finish();
                    });
                })(resolvedIndex);
            }
        }

        finish();
    }

    function buildStandaloneIosPreparedPlaylist(tracks, startIndex, currentJson, done, providerId) {
        if (!shouldUseStagedStandaloneIosPreparation(providerId)) {
            buildInternalPreparedPlaylist(tracks, startIndex, currentJson, function (preparedList) {
                done(preparedList, null);
            }, providerId);
            return;
        }

        var resolved = new Array(tracks.length);
        var order = [];
        var eagerCount = Math.max(0, getStandaloneIosInitialResolveCount(providerId) - 1);

        resolved[startIndex] = buildResolvedPlayback(tracks[startIndex], currentJson);

        for (var i = startIndex + 1; i < tracks.length; i++) order.push(i);
        for (var j = 0; j < startIndex; j++) order.push(j);

        var eagerOrder = order.slice(0, eagerCount);
        var backgroundOrder = order.slice(eagerCount);

        function buildList() {
            return tracks.map(function (entry, entryIndex) {
                return resolved[entryIndex] || buildPlayback(entry);
            });
        }

        function startBackgroundWarm(token) {
            if (!backgroundOrder.length) return;

            var pointer = 0;
            var active = false;
            var waitingVisible = false;
            var warmTimer = 0;
            var warmDelay = providerId === 'youtubeaudio' ? 1200 : 900;

            function stillCurrent() {
                if (!isStandaloneIosAudioActive()) return;
                if (MUSIC_IOS_AUDIO.prepareToken !== token) return;
                return true;
            }

            function scheduleWarm(delay) {
                if (warmTimer || !stillCurrent()) return;

                warmTimer = setTimeout(function () {
                    warmTimer = 0;
                    runWarm();
                }, delay || warmDelay);
            }

            function waitForVisible() {
                if (waitingVisible) return;

                waitingVisible = true;
                bumpMusicHeatMetric('standaloneWarmPausedHidden');

                try {
                    document.addEventListener('visibilitychange', function onVisible() {
                        if (document.hidden) return;

                        waitingVisible = false;
                        document.removeEventListener('visibilitychange', onVisible);
                        scheduleWarm(450);
                    });
                } catch (e) {
                    scheduleWarm(5000);
                }
            }

            function runWarm() {
                if (!stillCurrent()) return;
                if (active || pointer >= backgroundOrder.length) return;

                if (document.hidden) {
                    waitForVisible();
                    return;
                }

                var entryIndex = backgroundOrder[pointer++];
                active = true;
                bumpMusicHeatMetric('standaloneWarmRequest');

                requestPlay(tracks[entryIndex], function (json) {
                    active = false;
                    if (stillCurrent() && MUSIC_IOS_AUDIO.playlist && entryIndex < MUSIC_IOS_AUDIO.playlist.length)
                        MUSIC_IOS_AUDIO.playlist[entryIndex] = buildResolvedPlayback(tracks[entryIndex], json);
                    scheduleWarm(warmDelay);
                }, function () {
                    active = false;
                    if (stillCurrent() && MUSIC_IOS_AUDIO.playlist && entryIndex < MUSIC_IOS_AUDIO.playlist.length)
                        MUSIC_IOS_AUDIO.playlist[entryIndex] = buildPlayback(tracks[entryIndex]);
                    scheduleWarm(warmDelay);
                });
            }

            scheduleWarm(900);
        }

        resolvePreparedPlaylistEntries(tracks, eagerOrder, resolved, providerId, function () {
            done(buildList(), startBackgroundWarm);
        });
    }

    // первый audio.play() случается в async-колбэке резолва URL — вне жеста,
    // и WebKit не даёт элементу user-gesture кредит: iOS урезает локскрин-команды
    // (pause/seekto молчат, пока пользователь не потрогает плеер в приложении).
    // Тихий play() синхронно В ЖЕСТЕ выдаёт кредит заранее; реальный src,
    // приехав из резолва, наследует права элемента
    function warmupStandaloneIosGesture() {
        if (!shouldUseStandaloneIosAudio()) return;

        var audio = standaloneIosAudioElement();
        if (!audio) return;

        // обработчики должны существовать ДО play(): iOS фиксирует
        // возможности сессии (в т.ч. seekable) в момент её создания
        armStandaloneIosMediaSessionHandlers();

        try {
            if (audio.paused) {
                audio.loop = true;
                audio.src = buildSilentWavDataUri();
                audio.setAttribute('data-source-url', 'silent-warmup');
            }

            var promise = audio.play();
            if (promise && typeof promise.catch === 'function') promise.catch(function () {});
        } catch (e) {}
    }

    function playTrack(track, playlistTracks, startIndex, options) {
        var tracks = playlistTracks && playlistTracks.length ? playlistTracks : [track];
        var index = typeof startIndex === 'number' ? startIndex : 0;
        var standaloneIos = shouldUseStandaloneIosAudio();
        var forceFresh = !!(options && options.forceFresh);
        var resumePosition = Math.max(0, Number(options && options.resumePosition || 0));

        index = Math.max(0, Math.min(index, tracks.length - 1));
        rememberQueue(tracks, index);

        if (!forceFresh && standaloneIos && tracks.length > 1 && canReuseStandaloneIosQueue(tracks) && selectFromStandaloneIosQueue(index))
            return;

        if (!forceFresh && tracks.length > 1 && canReuseActiveQueue(tracks) && selectFromActiveQueue(index)) {
            scheduleTrackPlayed(tracks[index]);
            return;
        }

        function startFreshPlayback() {
            if (!standaloneIos && isStandaloneIosAudioActive())
                stopStandaloneIosAudioPlayback();

            if (standaloneIos)
                warmupStandaloneIosGesture();
            else if (shouldUseEmbeddedIosLockscreenMode())
                warmupEmbeddedIosGesture(tracks[index]);

            if (tracks.length > 1 && shouldUseManagedExternalPlaylist()) {
                var externalFirst = {
                    title: tracks[index].title || 'Track',
                    artist: tracks[index].artist_name || '',
                    poster: trackImage(tracks[index]),
                    img: trackImage(tracks[index]),
                    url: buildExternalPlaylistUrl(tracks, index)
                };

                scheduleTrackPlayed(tracks[index]);
                launchMusicPlayback(externalFirst);
                return;
            }

            requestPlay(tracks[index], function (json) {
                var providerId = getAudioProviderId();

                function buildLazyList() {
                    return tracks.map(function (entry, entryIndex) {
                        return entryIndex === index
                            ? buildResolvedPlayback(entry, json)
                            : buildPlayback(entry);
                    });
                }

                function launchStandalone(preparedList, afterLaunch) {
                    var list = preparedList && preparedList.length
                        ? preparedList
                        : [buildResolvedPlayback(tracks[index], json)];

                    var token = startStandaloneIosAudioPlayback(tracks, list, index, resumePosition);
                    if (!token) {
                        Lampa.Noty.show('Не удалось воспроизвести трек.');
                        return;
                    }

                    if (afterLaunch)
                        afterLaunch(token);
                }

                function launch(externalPlaylist, list) {
                    list = list && list.length ? list : buildLazyList();
                    var first = list[index];

                    traceEmbeddedIos('launch-internal', 'len=' + list.length
                        + ' mode=' + getPlaybackMode()
                        + ' player=' + currentExternalPlayer()
                        + ' launch=' + getLaunchMusicPlayerId()
                        + ' internal=' + usesInternalPlaybackFlow(), true);

                    if (externalPlaylist && externalPlaylist.length > 1) {
                        first.playlist = externalPlaylist;
                    }

                    if (tracks.length > 1 && usesInternalPlaybackFlow()) {
                        scheduleInternalPlaylist(tracks[index], list);
                    } else {
                        clearPendingInternalPlaylist();
                    }

                    scheduleTrackPlayed(tracks[index]);
                    if (shouldUseEmbeddedIosLockscreenMode())
                        primeEmbeddedIosMediaSession(first, tracks[index], 'launch');

                    launchMusicPlayback(first);

                    if (!(tracks.length > 1 && usesInternalPlaybackFlow())) {
                        Lampa.Player.playlist(list);
                    } else {
                        attachInternalPlaylistDeferred(tracks[index].id, list);
                        setTimeout(function () {
                            var work = activePlayerData();
                            if (work && work.from_music_cluster && work.music_track_id) {
                                flushPendingInternalPlaylist(work.music_track_id);
                            }
                        }, 150);
                    }
                }

                if (standaloneIos) {
                    if (tracks.length > 1) {
                        startMusicPlaybackLoading('Подготавливаю плейлист');
                        buildStandaloneIosPreparedPlaylist(tracks, index, json, function (preparedList, afterLaunch) {
                            stopMusicPlaybackLoading();
                            launchStandalone(preparedList, afterLaunch);
                        }, providerId);
                        return;
                    }

                    launchStandalone(null);
                    return;
                }

                if (tracks.length > 1 && !usesInternalPlaybackFlow()) {
                    startMusicPlaybackLoading('Подготавливаю альбом');
                    buildExternalPlaylist(tracks, index, json, function (externalPlaylist) {
                        stopMusicPlaybackLoading();
                        launch(externalPlaylist, null);
                    });
                    return;
                }

                if (tracks.length > 1 && usesInternalPlaybackFlow() && requiresPreparedInternalPlaylist(providerId)) {
                    startMusicPlaybackLoading('Подготавливаю плейлист');
                    buildInternalPreparedPlaylist(tracks, index, json, function (preparedList) {
                        stopMusicPlaybackLoading();
                        launch(null, preparedList);
                    }, providerId);
                    return;
                }

                launch(null, null);
            }, function (json) {
                Lampa.Noty.show(json && json.message ? json.message : 'Источник для трека пока не найден.');
            });
        }

        var current = activePlayerData();
        if (current && current.from_music_cluster) {
            stopMusicPlayerPanelFix();
            Lampa.Player.close();

            setTimeout(function () {
                startFreshPlayback();
            }, 0);
            return;
        }

        startFreshPlayback();
    }

    function loadTrackMatches(track, callback, query) {
        request(MUSIC.endpoints.matches + '?'
            + buildTrackRequestParams(track)
            + '&audio_provider=' + encodeURIComponent(getAudioProviderId())
            + '&playback_mode=' + encodeURIComponent(getPlaybackMode())
            + (query ? '&query=' + encodeURIComponent(query) : ''), function (json) {
            callback(parseJson(json) || null);
        }, function () {
            callback(null);
        });
    }

    function saveTrackMatch(track, match, done, query) {
        var payload = buildTrackRequestParams(track)
            + '&audio_provider=' + encodeURIComponent(match.provider_id)
            + '&playback_mode=' + encodeURIComponent(getPlaybackMode())
            + '&match_id=' + encodeURIComponent(match.id)
            + (query ? '&query=' + encodeURIComponent(query) : '');

        requestPost(MUSIC.endpoints.selectMatch, payload, function (json) {
            if (json && json.saved) {
                applySelectedMatchMetadata(track, match);
                invalidateTrackPlayCache(track);
                refreshRecentlyPlayedTrack(track);
            }
            done(!!(json && json.saved));
        }, function () {
            done(false);
        });
    }

    function resetTrackMatch(track, done) {
        var payload = buildTrackRequestParams(track)
            + '&playback_mode=' + encodeURIComponent(getPlaybackMode());

        requestPost(MUSIC.endpoints.resetMatch, payload, function (json) {
            if (json && json.reset)
                invalidateTrackPlayCache(track);
            done(!!(json && json.reset));
        }, function () {
            done(false);
        });
    }

    function formatMatchTitle(match) {
        var parts = [];
        if (match.title) parts.push(match.title);
        if (match.artists && match.artists.length) parts.push(match.artists.join(', '));
        if (match.duration_ms) parts.push(formatDuration(match.duration_ms));
        if (match.provider_id) parts.push(match.provider_id);
        return parts.join(' · ');
    }

    function getTrackArtistSearchQuery(track) {
        if (!track) return '';

        var artistName = String(track.artist_name || '').trim();
        if (artistName) return artistName;

        if (Array.isArray(track.artists) && track.artists.length)
            return track.artists.filter(Boolean).join(' x ');

        return '';
    }

    function buildTrackArtistSearchItems(track) {
        var query = getTrackArtistSearchQuery(track);
        if (!query) return [];

        return [{ title: 'Поиск по артисту', action: 'artist_search', artist_query: query }];
    }

    function buildTrackAlbumNavigation(track) {
        if (!track) return null;

        var albumTitle = String(track.album_title || '').trim();
        var albumId = String(track.album_id || '').trim();
        if (!albumTitle && !albumId) return null;

        var artistName = getTrackArtistSearchQuery(track);
        var album = {
            title: albumTitle || 'Альбом',
            artist_name: artistName || '',
            images: Array.isArray(track.images) ? track.images : []
        };

        if (albumId) album.id = albumId;

        // Для импортированных Spotify/Apple/SoundCloud треков album_id часто
        // отсутствует, поэтому открытие альбома идёт через уже существующий
        // resolveAlbum: поиск по артисту и названию с retry и срезом edition noise.
        // Если есть только прямой album_id без артиста, безопаснее открыть по id.
        if (albumTitle && (artistName || !albumId))
            album.lookup_query = [artistName, albumTitle].filter(Boolean).join(' ');

        return album;
    }

    function openTrackMenu(track, playlistTracks, startIndex, extraItems, onAction, onBookmarkChanged, menuOptions) {
        var restoreContext = captureMusicFocusContext('content');
        var isBookmarked = isBookmarkedEntity(MUSIC.storage.bookmarked_tracks, track && track.id);
        var items = [{ title: 'Играть', action: 'play' }];
        var trackAlbum = buildTrackAlbumNavigation(track);

        if (playlistTracks && playlistTracks.length > 1)
            items.push({ title: 'Играть альбом отсюда', action: 'play_from_here' });

        items.push({ title: 'Радио от трека', action: 'radio_from_track' });

        // на экране самого альбома пункт скрыт (hideOpenAlbum) — он открыл бы
        // тот же экран заново. Без album-меты (SoundCloud-аплоады) — фоллбек
        // через поиск самого трека
        if (!(menuOptions && menuOptions.hideOpenAlbum)) {
            if (trackAlbum)
                items.push({ title: 'Открыть альбом', action: 'open_album', album: trackAlbum });
            else if (track && track.title)
                items.push({ title: 'Открыть альбом', action: 'open_album_lookup' });
        }

        // пункты очереди видны только когда standalone-очередь реально играет
        // (или восстановлена) — вне этого им некуда вставлять. Полное
        // управление очередью из любого списка, плеер открывать не нужно
        if (canEnqueueToStandaloneQueue(track)) {
            var queuePos = standaloneQueueIndexOfTrack(track.id);
            var inQueue = queuePos > -1 && queuePos !== standaloneQueueCurrentIndexValue();

            items.push({ title: 'Играть следующим', action: 'queue_next' });
            items.push({ title: inQueue ? 'В конец очереди' : 'Добавить в очередь', action: 'queue_end' });
            if (inQueue) items.push({ title: 'Убрать из очереди', action: 'queue_remove' });
        }

        items.push({
            title: isBookmarked ? 'Убрать из закладок' : 'Добавить в закладки',
            action: 'toggle_bookmark'
        });
        items.push({ title: 'В плейлист…', action: 'add_to_playlist' });
        items = items.concat(buildTrackArtistSearchItems(track));
        items.push({ title: 'Источники', action: 'sources' });
        if (Array.isArray(extraItems) && extraItems.length)
            items = items.concat(extraItems);

        Lampa.Select.show({
            title: track.title || 'Track',
            items: items,
            onBack: function () {
                restoreMusicFocusContext(restoreContext);
            },
            onSelect: function (selected) {
                if (!selected) {
                    restoreMusicFocusContext(restoreContext);
                    return;
                }

                if (selected.action === 'play') {
                    playTrack(track);
                    restoreMusicFocusContext(restoreContext, 120);
                    return;
                }

                if (selected.action === 'play_from_here') {
                    playTrack(track, playlistTracks, startIndex || 0);
                    restoreMusicFocusContext(restoreContext, 120);
                    return;
                }

                if (selected.action === 'radio_from_track') {
                    startRadioFromTrack(track, restoreContext);
                    return;
                }

                if (selected.action === 'open_album') {
                    openAlbum(selected.album, {
                        onFail: function () {
                            restoreMusicFocusContext(restoreContext);
                        }
                    });
                    return;
                }

                if (selected.action === 'open_album_lookup') {
                    openAlbumFromTrackSearch(track, restoreContext);
                    return;
                }

                if (selected.action === 'queue_next' || selected.action === 'queue_end') {
                    var wasQueued = standaloneQueueIndexOfTrack(track && track.id) > -1;
                    var queued = enqueueTrackToStandaloneQueue(track, selected.action === 'queue_next' ? 'next' : 'end');

                    Lampa.Noty.show(queued
                        ? (selected.action === 'queue_next'
                            ? 'Сыграет следующим.'
                            : (wasQueued ? 'Перемещено в конец очереди.' : 'Добавлено в очередь.'))
                        : 'Не удалось добавить в очередь.');
                    restoreMusicFocusContext(restoreContext, 120);
                    return;
                }

                if (selected.action === 'queue_remove') {
                    var removeIndex = standaloneQueueIndexOfTrack(track && track.id);
                    var removed = removeIndex > -1 && mutateStandaloneIosQueue('remove', removeIndex);

                    Lampa.Noty.show(removed ? 'Убрано из очереди.' : 'Не удалось убрать из очереди.');
                    restoreMusicFocusContext(restoreContext, 120);
                    return;
                }

                if (selected.action === 'toggle_bookmark') {
                    var added = toggleBookmarkedEntity(MUSIC.storage.bookmarked_tracks, track);
                    Lampa.Noty.show(added ? 'Трек добавлен в закладки.' : 'Трек удалён из закладок.');
                    var focusHandled = onBookmarkChanged ? onBookmarkChanged(added, restoreContext) : false;
                    if (!focusHandled)
                        restoreMusicFocusContext(restoreContext, 120);
                    return;
                }

                if (selected.action === 'add_to_playlist') {
                    openAddToPlaylistMenu(track, restoreContext);
                    return;
                }

                if (selected.action === 'artist_search') {
                    openSearchQuery(selected.artist_query || track.artist_name || '');
                    return;
                }

                if (selected.action === 'sources') {
                    loadTrackMatches(track, function (json) {
                        var matches = json && json.matches ? json.matches : [];
                        var selectedMatch = json && json.selected_match ? json.selected_match : null;

                        if (!matches.length) {
                            Lampa.Noty.show('Автоматически не найдено — введи запрос вручную.');
                            openManualSourceSearchMenu(track, null, restoreContext);
                            return;
                        }

                        var items = matches.map(function (match) {
                            var isSelected = selectedMatch
                                && selectedMatch.id === match.id
                                && selectedMatch.provider_id === match.provider_id;

                            return {
                                title: (isSelected ? '✓ ' : '') + formatMatchTitle(match),
                                match: match
                            };
                        });

                        items.push({ title: 'Найти вручную…', manual_search: true });

                        Lampa.Select.show({
                            title: 'Источники',
                            items: items,
                            onBack: function () {
                                restoreMusicFocusContext(restoreContext);
                            },
                            onSelect: function (entry) {
                                if (!entry) {
                                    restoreMusicFocusContext(restoreContext);
                                    return;
                                }

                                if (entry.manual_search) {
                                    openManualSourceSearchMenu(track, null, restoreContext);
                                    return;
                                }

                                if (!entry.match) return;

                                saveTrackMatch(track, entry.match, function (saved) {
                                    if (!saved) {
                                        Lampa.Noty.show('Не удалось выбрать источник.');
                                        restoreMusicFocusContext(restoreContext);
                                        return;
                                    }

                                    Lampa.Noty.show('Источник сохранён.');
                                    restoreMusicFocusContext(restoreContext, 180);
                                });
                            }
                        });
                    });
                    return;
                }

                if (onAction)
                    onAction(selected, restoreContext);
            }
        });
    }

    // меню-версия «Найти вручную» (для контекстного меню карточек):
    // тот же серверный путь query-поиска, что и в шите фулл-плеера
    function openManualSourceSearchMenu(track, initialValue, restoreContext) {
        restoreContext = restoreContext || captureMusicFocusContext('content');
        var prefill = typeof initialValue === 'string' && initialValue
            ? initialValue
            : (((track && track.artist_name) || '') + ' ' + ((track && track.title) || '')).trim();

        Lampa.Input.edit({
            value: prefill,
            title: 'Поиск источника',
            free: true,
            nosave: true,
            nomic: true
        }, function (queryValue) {
            queryValue = String(queryValue || '').trim();
            if (!queryValue) {
                restoreMusicFocusContext(restoreContext);
                return;
            }

            loadTrackMatches(track, function (json) {
                var matches = json && Array.isArray(json.matches) ? json.matches : [];
                var selectedMatch = json && json.selected_match ? json.selected_match : null;
                var items = [{ title: 'Изменить запрос («' + queryValue + '»)', retry_query: true }];

                matches.forEach(function (match) {
                    var isSelected = selectedMatch
                        && selectedMatch.id === match.id
                        && selectedMatch.provider_id === match.provider_id;

                    items.push({
                        title: (isSelected ? '✓ ' : '') + formatMatchTitle(match),
                        match: match
                    });
                });

                if (!matches.length)
                    items.push({ title: 'Ничего не найдено', noop: true });

                Lampa.Select.show({
                    title: 'Поиск источника',
                    items: items,
                    onBack: function () {
                        restoreMusicFocusContext(restoreContext);
                    },
                    onSelect: function (entry) {
                        if (!entry || entry.noop) {
                            restoreMusicFocusContext(restoreContext);
                            return;
                        }

                        if (entry.retry_query) {
                            openManualSourceSearchMenu(track, queryValue, restoreContext);
                            return;
                        }

                        if (!entry.match) return;

                        saveTrackMatch(track, entry.match, function (saved) {
                            if (!saved) {
                                Lampa.Noty.show('Не удалось выбрать источник.');
                                restoreMusicFocusContext(restoreContext);
                                return;
                            }

                            Lampa.Noty.show('Источник сохранён.');
                            restoreMusicFocusContext(restoreContext, 180);
                        }, queryValue);
                    }
                });
            }, queryValue);
        });
    }

    function mapPlaylistCard(summary) {
        var image = summary && summary.images && summary.images.length && summary.images[summary.images.length - 1].url
            ? summary.images[summary.images.length - 1].url
            : artwork('playlist', summary && summary.title ? summary.title : 'Плейлист', 'PLAYLIST', ['#232838', '#4a5a82']);
        var sourceType = summary && summary.source && summary.source.type ? String(summary.source.type) : '';

        return {
            type: 'playlist',
            id: summary.id,
            title: summary.title || 'Плейлист',
            subtitle: formatTrackCountLabel(summary.track_count || 0),
            badge: sourceType.indexOf('soundcloud_') === 0 ? 'SOUNDCLOUD' : 'PLAYLIST',
            image: image,
            background: image,
            raw: summary
        };
    }

    function mapPlaylistAddCard() {
        var image = artwork('playlist_add', 'Добавить', 'PLAYLIST', ['#202a34', '#54726c']);
        return {
            type: 'playlist_action',
            id: 'playlist:add',
            title: 'Добавить',
            subtitle: 'Создать или импорт',
            badge: '+',
            image: image,
            background: image,
            raw: { action: 'add_playlist' }
        };
    }

    function buildUserPlaylistCards(playlists) {
        return [mapPlaylistAddCard()].concat((playlists || []).map(mapPlaylistCard));
    }

    function buildUserPlaylistCardsWithStats(playlists, stats) {
        var cards = buildUserPlaylistCards(playlists);

        // «Твой топ» живёт в полке плейлистов как виртуальный альбом.
        // Ставим сразу после «Добавить», но только после серверного unlock.
        if (stats && stats.unlocked)
            cards.splice(1, 0, mapStatsTopCard(stats));

        return cards;
    }

    function updateRenderedUserPlaylistCard(entry) {
        if (!entry || !entry.id) return;

        var focusKey = buildMusicFocusKey(entry);
        $('.lm-card.selector[data-music-focus-key]').filter(function () {
            return this.getAttribute('data-music-focus-key') === focusKey;
        }).each(function () {
            var card = $(this);
            var image = card.find('.lm-card__img');

            setTextIfChanged(card.find('.lm-card__title'), entry.title || 'Плейлист');
            setTextIfChanged(card.find('.lm-card__subtitle'), entry.subtitle || '');
            setTextIfChanged(card.find('.lm-card__badge'), entry.badge || '');

            if (entry.image && image.attr('src') !== entry.image) {
                card.removeClass('loaded');
                image.attr('src', entry.image);
            }

            if (card.hasClass('focus'))
                setBackground(entry.background || IMG_BG);
        });
    }

    function updateUserPlaylistHomeCard(summary) {
        if (!summary || !summary.id) return false;

        var entry = applySectionKey([mapPlaylistCard(summary)], 'user_playlists')[0];
        var list = Array.isArray(MUSIC_HOME_CACHE.user_playlists) ? MUSIC_HOME_CACHE.user_playlists.slice() : [];
        var addCard = list.filter(function (item) { return item && item.type === 'playlist_action'; })[0]
            || applySectionKey([mapPlaylistAddCard()], 'user_playlists')[0];
        var rest = list.filter(function (item) {
            return item && item.type !== 'playlist_action' && item.id !== entry.id;
        });

        MUSIC_HOME_CACHE.user_playlists = [addCard, entry].concat(rest);
        updateHomeSectionMetaFromCache('user_playlists');
        updateRenderedUserPlaylistCard(entry);

        return true;
    }

    // название сервиса-источника импортированного плейлиста ('SoundCloud'/'Spotify')
    // или null для обычных плейлистов — по нему решаем, показывать ли «Обновить»
    function playlistSourceService(entry) {
        var sourceType = entry && entry.raw && entry.raw.source && entry.raw.source.type
            ? String(entry.raw.source.type)
            : '';
        if (sourceType.indexOf('soundcloud_') === 0) return 'SoundCloud';
        if (sourceType.indexOf('spotify_') === 0) return 'Spotify';
        if (sourceType.indexOf('applemusic_') === 0) return 'Apple Music';
        return null;
    }

    function formatTotalDurationLabel(totalMs) {
        var minutes = Math.round((Number(totalMs) || 0) / 60000);
        if (!minutes) return '';
        if (minutes < 60) return minutes + ' мин';

        var hours = Math.floor(minutes / 60);
        var rest = minutes % 60;
        return rest ? hours + ' ч ' + rest + ' мин' : hours + ' ч';
    }

    function playTracksShuffled(tracks) {
        if (!tracks || !tracks.length) return;

        Lampa.Storage.set(MUSIC.storage.shuffle, true);
        var start = Math.floor(Math.random() * tracks.length);
        playTrack(tracks[start], tracks, start);
    }

    // ===== USER PLAYLISTS =====

    function formatTrackCountLabel(count) {
        count = Number(count) || 0;
        var mod10 = count % 10;
        var mod100 = count % 100;

        if (mod10 === 1 && mod100 !== 11) return count + ' трек';
        if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return count + ' трека';
        return count + ' треков';
    }

    function loadUserPlaylists(callback) {
        request(MUSIC.endpoints.playlists, function (json) {
            json = parseJson(json);
            callback(json && Array.isArray(json.playlists) ? json.playlists : []);
        }, function () {
            callback([]);
        });
    }

    // сводка статистики для карточки «Твой топ»; кэш 60s, чтобы частые
    // перерисовки полки (recently_played-события) не спамили эндпоинт
    function loadStatsTopSummary(callback) {
        var now = Date.now();

        if (MUSIC_STATS_TOP.checkedAt && now - MUSIC_STATS_TOP.checkedAt < 60000) {
            callback(MUSIC_STATS_TOP.summary);
            return;
        }

        request(MUSIC.endpoints.statsTop + '?limit=1', function (json) {
            MUSIC_STATS_TOP.checkedAt = Date.now();
            MUSIC_STATS_TOP.summary = parseJson(json) || null;
            callback(MUSIC_STATS_TOP.summary);
        }, function () {
            // сбой сети не кэшируем — следующая перерисовка попробует снова
            callback(MUSIC_STATS_TOP.summary);
        });
    }

    function mapStatsTopCard(stats) {
        var image = artwork('playlist', 'Твой топ', 'STATS', ['#2b1e3d', '#7b4bbd']);

        return {
            type: 'stats_top',
            id: 'stats:top',
            title: 'Твой топ',
            subtitle: formatPlaysLabel(stats && stats.total_plays),
            badge: '♪',
            image: image,
            background: image,
            raw: stats
        };
    }

    function formatPlaysLabel(count) {
        count = Math.max(0, Number(count) || 0);
        var mod10 = count % 10;
        var mod100 = count % 100;
        var word = 'прослушиваний';

        if (mod10 === 1 && mod100 !== 11) word = 'прослушивание';
        else if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) word = 'прослушивания';

        return count + ' ' + word;
    }

    // бытовой эквивалент заработка — деадпан-пасхалка, подаётся как факт
    // каталога; интерфейс никогда не признаётся, что пошутил
    function payoutEquivalentLabel(payout) {
        if (payout < 0.5) return 'почти жвачка';
        if (payout < 3) return 'почти кофе';
        if (payout < 10) return 'шаурма близко';
        if (payout < 30) return 'уже заметили бухгалтеры';
        return 'индустрия держится';
    }

    // мета hero «Твоего топа»: цифры + шутка про заработок артистов одной
    // строкой (ставка ~$0.004 за стрим), сухо, как факт каталога
    function buildStatsTopMetaLabel(summary, trackCount) {
        if (!summary) return formatTrackCountLabel(trackCount || 0);

        var parts = [formatPlaysLabel(summary.total_plays)];

        if (summary.total_ms > 0)
            parts.push('примерно ' + formatTotalDurationLabel(summary.total_ms));

        var payout = (Number(summary.total_plays) || 0) * 0.004;
        if (payout >= 0.01) {
            parts.push('артисты заработали ≈ $' + payout.toFixed(2));
            parts.push(payoutEquivalentLabel(payout));
        }

        return parts.join(' · ');
    }

    // личные «сертификации» треков по числу прослушиваний — тихая пометка
    // в мете строки, без иконок и объяснений
    function trackCertificationLabel(playCount) {
        if (playCount >= 250) return 'архивная ценность';
        if (playCount >= 100) return 'платина';
        if (playCount >= 50) return 'золото';
        return '';
    }

    function openCreatePlaylistDialog(onCreated, restoreContext) {
        Lampa.Input.edit({
            value: '',
            title: 'Название плейлиста',
            free: true,
            nosave: true,
            nomic: true
        }, function (titleValue) {
            titleValue = String(titleValue || '').trim();
            if (!titleValue) {
                restoreMusicFocusContext(restoreContext);
                return;
            }

            requestPost(MUSIC.endpoints.playlistCreate, 'title=' + encodeURIComponent(titleValue), function (json) {
                if (!json || !json.created || !json.playlist_id) {
                    Lampa.Noty.show('Не удалось создать плейлист.');
                    restoreMusicFocusContext(restoreContext);
                    return;
                }

                if (onCreated) {
                    notifyPlaylistChanged(json.playlist_id, {
                        skipHomeSection: true,
                        skipPlaylistSection: true
                    });
                    onCreated(json);
                    return;
                }

                notifyPlaylistChanged(json.playlist_id, {
                    restoreState: { sectionKey: 'user_playlists', entryIndex: 1 }
                });
                Lampa.Noty.show('Плейлист создан.');
            }, function () {
                Lampa.Noty.show('Не удалось создать плейлист.');
                restoreMusicFocusContext(restoreContext);
            });
        });
    }

    function openImportLinkDialog(restoreContext) {
        Lampa.Input.edit({
            value: 'https://',
            title: 'Ссылка SoundCloud, Spotify или Apple Music',
            free: true,
            nosave: true,
            nomic: true
        }, function (url) {
            url = String(url || '').trim();
            if (!url || url === 'https://' || url === 'http://') {
                restoreMusicFocusContext(restoreContext);
                return;
            }

            Lampa.Noty.show('Импортирую плейлист...');
            requestPost(MUSIC.endpoints.playlistImport, 'url=' + encodeURIComponent(url), function (json) {
                if (!json || !json.available || !json.playlist_id) {
                    Lampa.Noty.show((json && json.message) || 'Не удалось импортировать плейлист.');
                    restoreMusicFocusContext(restoreContext);
                    return;
                }

                notifyPlaylistChanged(json.playlist_id, {
                    restoreState: { sectionKey: 'user_playlists', entryIndex: 1 }
                });
                var countLabel = 'Импортировано: ' + formatTrackCountLabel(json.track_count || 0) + (json.truncated ? ' (не всё — список слишком большой)' : '') + '.';
                Lampa.Noty.show(json.message || countLabel);
            }, function () {
                Lampa.Noty.show('Не удалось импортировать плейлист.');
                restoreMusicFocusContext(restoreContext);
            });
        });
    }

    function openPlaylistAddMenu() {
        var restoreContext = captureMusicFocusContext('content');

        showContextMenu('Добавить', [
            { title: 'Создать плейлист', action: 'create' },
            { title: 'Импорт по ссылке', action: 'import' }
        ], function (selected) {
            if (!selected || !selected.action) return;

            if (selected.action === 'create') {
                openCreatePlaylistDialog(null, restoreContext);
                return;
            }

            if (selected.action === 'import')
                openImportLinkDialog(restoreContext);
        });
    }

    function syncImportedPlaylist(entry, refresh, confirmed, restoreContext, index) {
        if (!entry || !entry.id) return;
        restoreContext = restoreContext || captureMusicFocusContext('content');

        var serviceName = playlistSourceService(entry) || 'источник';
        if (!confirmed) {
            // sync — слияние: порядок и ручные правки сохраняются,
            // новые треки источника добавляются в начало
            showContextMenu('Новые треки добавятся в начало', [
                { title: 'Обновить из ' + serviceName, action: 'confirm_sync' },
                { title: 'Отмена', action: 'cancel' }
            ], function (selected) {
                if (selected && selected.action === 'confirm_sync') {
                    syncImportedPlaylist(entry, refresh, true, restoreContext, index);
                    return;
                }

                restoreMusicFocusContext(restoreContext);
            }, restoreContext);
            return;
        }

        Lampa.Noty.show('Обновляю из ' + serviceName + '...');
        requestPost(MUSIC.endpoints.playlistSync, 'id=' + encodeURIComponent(entry.id), function (json) {
            if (!json || !json.available) {
                Lampa.Noty.show((json && json.message) || 'Не удалось обновить плейлист.');
                restoreMusicFocusContext(restoreContext);
                return;
            }

            notifyPlaylistChanged(entry.id, {
                skipPlaylistSection: !!refresh,
                skipHomeSection: !!(refresh && refresh.length > 0),
                skipActiveSectionRefresh: !!(refresh && refresh.length === 0)
            });
            Lampa.Noty.show(json.message || ('Обновлено: ' + formatTrackCountLabel(json.track_count || 0) + '.'));
            refreshListWithFocus(refresh, buildEntryRestoreState(entry, index), restoreContext);
        }, function () {
            Lampa.Noty.show('Не удалось обновить плейлист.');
            restoreMusicFocusContext(restoreContext);
        });
    }

    // «В плейлист…»: выбор существующего или создание нового, трек уходит
    // на сервер целиком (durable, per-profile как история)
    function openAddToPlaylistMenu(track, restoreContext) {
        if (!track || !track.id) {
            Lampa.Noty.show('Трек не выбран.');
            restoreMusicFocusContext(restoreContext);
            return;
        }

        restoreContext = restoreContext || captureMusicFocusContext('content');

        loadUserPlaylists(function (playlists) {
            var items = [{ title: 'Создать новый…', create_new: true }];

            playlists.forEach(function (playlist) {
                items.push({
                    title: playlist.title + ' (' + formatTrackCountLabel(playlist.track_count || 0) + ')',
                    playlist: playlist
                });
            });

            Lampa.Select.show({
                title: 'В плейлист',
                items: items,
                onBack: function () {
                    restoreMusicFocusContext(restoreContext);
                },
                onSelect: function (entry) {
                    if (!entry) {
                        restoreMusicFocusContext(restoreContext);
                        return;
                    }

                    if (entry.create_new) {
                        openCreatePlaylistDialog(function (json) {
                            addTrackToPlaylist(track, json.playlist_id, json.title || 'плейлист', restoreContext);
                        }, restoreContext);
                        return;
                    }

                    if (entry.playlist)
                        addTrackToPlaylist(track, entry.playlist.id, entry.playlist.title, restoreContext);
                }
            });
        });
    }

    function addTrackToPlaylist(track, playlistId, playlistTitle, restoreContext) {
        var payload = 'id=' + encodeURIComponent(playlistId)
            + '&track=' + encodeURIComponent(JSON.stringify(track));

        requestPost(MUSIC.endpoints.playlistTrackAdd, payload, function (json) {
            if (json && json.saved) {
                updateUserPlaylistHomeCard(json.playlist);
                notifyPlaylistChanged(playlistId, {
                    skipHomeSection: true,
                    skipPlaylistSection: true,
                    preserveHomeCache: true
                });
                Lampa.Noty.show('Добавлено в «' + (playlistTitle || 'плейлист') + '».');
                restoreMusicFocusContext(restoreContext, 120);
                return;
            }

            Lampa.Noty.show('Не удалось добавить в плейлист.');
            restoreMusicFocusContext(restoreContext);
        }, function () {
            Lampa.Noty.show('Не удалось добавить в плейлист.');
            restoreMusicFocusContext(restoreContext);
        });
    }

    function mapSearchArtistEntry(artist, sectionKey) {
        return {
            type: 'artist',
            id: artist.id,
            title: artist.name || 'Unknown Artist',
            subtitle: artist.sort_name || artist.country || 'Artist',
            badge: artist.country || 'ARTIST',
            image: artistImage(artist, 250),
            background: artistImage(artist, 500),
            raw: artist,
            section_key: sectionKey || ''
        };
    }

    function mapSearchAlbumEntry(album, sectionKey) {
        return {
            type: 'album',
            id: album.id,
            title: album.title || 'Untitled',
            subtitle: album.artist_name || formatDate(album.date),
            badge: formatYear(album.date) || 'ALBUM',
            image: albumImage(album, 250),
            background: albumImage(album, 500),
            raw: album,
            section_key: sectionKey || ''
        };
    }

    function mapSearchTrackEntry(track, sectionKey) {
        return {
            type: 'track',
            id: track.id,
            title: track.title || 'Untitled',
            subtitle: track.artist_name || track.album_title || 'Track',
            badge: formatDuration(track.duration_ms) || 'TRACK',
            image: trackImage(track),
            background: trackImage(track),
            raw: track,
            section_key: sectionKey || ''
        };
    }

    function mapSearchSection(section) {
        if (!section) return null;

        var sectionKey = 'search_section:' + (section.id || Lampa.Utils.uid());
        var artists = section && Array.isArray(section.artists) ? section.artists : [];
        var albums = section && Array.isArray(section.albums) ? section.albums : [];
        var tracks = section && Array.isArray(section.tracks) ? section.tracks : [];
        var entries = [];

        if (tracks.length) entries = tracks.map(function (track) { return mapSearchTrackEntry(track, sectionKey); });
        else if (albums.length) entries = albums.map(function (album) { return mapSearchAlbumEntry(album, sectionKey); });
        else if (artists.length) entries = artists.map(function (artist) { return mapSearchArtistEntry(artist, sectionKey); });

        if (!entries.length) return null;

        return {
            id: section.id || sectionKey,
            title: section.title || 'Результаты',
            source_provider: section.source_provider || '',
            type: section.type || (tracks.length ? 'tracks' : albums.length ? 'albums' : 'artists'),
            has_more: !!section.has_more,
            next_page: section.next_page || '',
            entries: entries
        };
    }

    function normalizeSearchResults(json) {
        var artists = json && Array.isArray(json.artists) ? json.artists : [];
        var albums = json && Array.isArray(json.albums) ? json.albums : [];
        var tracks = json && Array.isArray(json.tracks) ? json.tracks : [];
        var sections = json && Array.isArray(json.search_sections) ? json.search_sections : [];

        return {
            metadata_pending: !!(json && json.metadata_pending),
            artists: artists.map(function (artist) { return mapSearchArtistEntry(artist, 'search_artists'); }),
            albums: albums.map(function (album) { return mapSearchAlbumEntry(album, 'search_albums'); }),
            tracks: tracks.map(function (track) { return mapSearchTrackEntry(track, 'search_tracks'); }),
            sections: sections.map(mapSearchSection).filter(Boolean)
        };
    }

    function getSearchSectionGroupTitle(section) {
        if (!section) return 'Результаты';
        if ((section.source_provider === 'youtubeaudio' || section.source_provider === 'soundcloudcharts') && section.title) return section.title;
        if (section.type === 'artists') return 'Артисты';
        if (section.type === 'albums') return 'Альбомы';
        if (section.type === 'tracks') return 'Треки';
        return 'Результаты';
    }

    function buildRelatedArtistEntries(groups, query) {
        var tracks = groups && Array.isArray(groups.tracks) ? groups.tracks : [];
        var artists = groups && Array.isArray(groups.artists) ? groups.artists : [];
        var queryNorm = normalizeText(query);
        var primary = {};
        var related = {};

        function markPrimary(name) {
            var normalized = normalizeText(name);
            if (normalized) primary[normalized] = true;
        }

        function isPrimary(name) {
            var normalized = normalizeText(name);
            if (!normalized) return false;
            if (primary[normalized]) return true;

            return !!(queryNorm && (
                normalized === queryNorm
                || normalized.indexOf(queryNorm) >= 0
                || queryNorm.indexOf(normalized) >= 0
            ));
        }

        function addRelated(name, track, index) {
            var normalized = normalizeText(name);
            if (!normalized || isPrimary(name)) return;

            var state = related[normalized];
            if (!state) {
                state = related[normalized] = {
                    name: String(name || '').trim(),
                    score: 0,
                    tracks: [],
                    raw: {
                        id: 'related:' + normalized,
                        name: String(name || '').trim(),
                        sort_name: '',
                        country: '',
                        description: 'Из совместных треков',
                        images: []
            }
        };
    }

            state.score += Math.max(1, 24 - index);
            if (track && track.title && state.tracks.indexOf(track.title) < 0)
                state.tracks.push(track.title);
        }

        if (queryNorm) markPrimary(query);

        artists.slice(0, 4).forEach(function (entry) {
            if (entry && entry.raw && entry.raw.name) markPrimary(entry.raw.name);
        });

        tracks.slice(0, 40).forEach(function (entry, index) {
            var track = entry && entry.raw ? entry.raw : null;
            var trackArtists = track && Array.isArray(track.artists) && track.artists.length
                ? track.artists
                : track && track.artist_name
                    ? [track.artist_name]
                    : [];

            if (trackArtists.length < 2) return;
            if (!trackArtists.some(isPrimary)) return;

            trackArtists.forEach(function (name) {
                addRelated(name, track, index);
            });
        });

        return Object.keys(related)
            .map(function (key) { return related[key]; })
            .sort(function (a, b) { return b.score - a.score; })
            .slice(0, HOME_SECTION_LIMIT)
            .map(function (item) {
                return {
                    type: 'artist',
                    id: item.raw.id,
                    title: item.raw.name,
                    subtitle: '',
                    badge: 'RELATED',
                    image: artistImage(item.raw, 250),
                    background: artistImage(item.raw, 250),
                    raw: item.raw
                };
            });
    }

    function buildSearchSources(groups, query) {
        var sourcesMap = {};
        var catalogGroups = [];
        var relatedArtists = buildRelatedArtistEntries(groups, query);

        function ensureSource(id, title, sourceProvider) {
            if (!sourcesMap[id]) {
                sourcesMap[id] = {
                    id: id,
                    title: title,
                        source_provider: sourceProvider || '',
                        count: 0,
                        pending: false,
                        groups: []
                    };
                }

            return sourcesMap[id];
        }

        function getSearchSourceId(section) {
            var id = section && section.id ? section.id : '';
            var provider = section && section.source_provider ? section.source_provider : '';

            if (provider === 'youtubeaudio' || id.indexOf('search:youtubemusic') === 0)
                return 'search:youtubemusic';

            if (provider === 'soundcloudcharts' || id.indexOf('search:soundcloud') === 0)
                return 'search:soundcloud';

            return id || ('source:' + ((section && section.title) || Lampa.Utils.uid()));
        }

        function getSearchSourceTitle(section, sourceId) {
            if (sourceId === 'search:youtubemusic') return 'YouTube Music';
            if (sourceId === 'search:soundcloud') return 'SoundCloud';
            return (section && section.title) || 'Источник';
        }

        if (groups.artists.length) {
            catalogGroups.push({
                id: 'artists',
                title: 'Артисты',
                entries: groups.artists
            });
        }

        if (groups.albums.length) {
            catalogGroups.push({
                id: 'albums',
                title: 'Альбомы',
                entries: groups.albums
            });
        }

        if (groups.tracks.length) {
            catalogGroups.push({
                id: 'tracks',
                title: 'Треки',
                entries: groups.tracks
            });
        }

        if (relatedArtists.length) {
            catalogGroups.push({
                id: 'related_artists',
                title: 'Также может понравиться',
                entries: relatedArtists
            });
        }

        var catalogSource = ensureSource('catalog', 'MusicBrainz', 'musicbrainz');
        catalogSource.groups = catalogGroups;
        catalogSource.count = catalogGroups.reduce(function (sum, group) {
            return sum + (Array.isArray(group.entries) ? group.entries.length : 0);
        }, 0);
        catalogSource.pending = hasPendingSearchMetadata(groups);

        ensureSource('search:youtubemusic', 'YouTube Music', 'youtubeaudio');
        ensureSource('search:soundcloud', 'SoundCloud', 'soundcloudcharts');
        ensureSource('search:sefon', 'Sefon', 'sefonaudio');

        groups.sections.forEach(function (section) {
            if (!section) return;

            var entries = Array.isArray(section.entries) ? section.entries : [];
            if (!entries.length) return;

            var sourceId = getSearchSourceId(section);
            var source = ensureSource(sourceId, getSearchSourceTitle(section, sourceId), section.source_provider || '');

            source.groups.push({
                id: section.id,
                title: getSearchSectionGroupTitle(section),
                entries: entries,
                has_more: !!section.has_more
            });
            source.count += entries.length;
        });

        return ['catalog', 'search:youtubemusic', 'search:soundcloud', 'search:sefon']
            .map(function (id) { return sourcesMap[id]; })
            .filter(Boolean);
    }

    function mapArtistCard(artist) {
        return {
            type: 'artist',
            id: artist.id,
            title: artist.name || 'Unknown Artist',
            subtitle: artist.sort_name || artist.country || 'Artist',
            badge: artist.country || 'ARTIST',
            image: artistImage(artist, 250),
            background: artistImage(artist, 250),
            raw: artist
        };
    }

    function mapAlbumCard(album) {
        return {
            type: 'album',
            id: album.id,
            title: album.title || 'Untitled',
            subtitle: album.artist_name || formatDate(album.date),
            badge: formatYear(album.date) || 'ALBUM',
            image: albumImage(album, 250),
            background: albumImage(album, 500),
            raw: album
        };
    }

    function mapQueryCard(queryItem) {
        var query = typeof queryItem === 'string' ? queryItem : (queryItem && queryItem.query ? queryItem.query : '');
        var artist = queryItem && typeof queryItem === 'object' ? queryItem.artist : null;
        var image = artist ? artistImage(artist, 250) : artwork('album', query, 'Search', ['#1a2531', '#41627e']);

        return {
            type: 'query',
            id: query,
            title: query,
            subtitle: artist && artist.name ? artist.name : 'Открыть поиск',
            badge: 'SEARCH',
            image: image,
            background: image,
            artist: artist,
            raw: query
        };
    }

    function mapTrackCard(track) {
        var badge = track && track.duration_ms
            ? formatDuration(track.duration_ms)
            : track && track.track_number
                ? '#' + track.track_number
                : 'TRACK';

        return {
            type: 'track',
            id: track.id,
            title: track.title || 'Untitled',
            subtitle: track.artist_name || track.album_title || 'Track',
            badge: badge,
            image: trackImage(track),
            background: trackImage(track),
            raw: track
        };
    }

    function cloneHomeEntry(entry) {
        if (!entry) return null;

        var copy = {};
        Object.keys(entry).forEach(function (key) {
            if (key !== 'ready') copy[key] = entry[key];
        });

        return copy;
    }

    function applySectionKey(entries, sectionKey) {
        return (entries || []).map(function (entry) {
            if (entry) entry.section_key = sectionKey;
            return entry;
        }).filter(Boolean);
    }

    function cloneHomeEntries(entries) {
        return (entries || []).map(cloneHomeEntry).filter(Boolean);
    }

    function mergeHomeEntriesById(primary, secondary, limit) {
        var result = [];
        var seen = {};

        function add(entry) {
            if (!entry) return;

            var key = entry.id || ((entry.type || '') + ':' + (entry.title || ''));
            if (!key || seen[key]) return;

            seen[key] = true;
            result.push(entry);
        }

        (primary || []).forEach(add);
        (secondary || []).forEach(add);

        return result.slice(0, limit || RECENT_SECTION_STORAGE_LIMIT);
    }

    function getHomeSectionEntries(sectionKey, home) {
        if (sectionKey === 'recently_played') {
            var serverEntries = (home && home.recently_played ? home.recently_played : [])
                .map(function (item) { return item && item.track ? mapTrackCard(item.track) : null; })
                .filter(Boolean);
            var cachedEntries = Array.isArray(MUSIC_HOME_CACHE.recently_played)
                ? MUSIC_HOME_CACHE.recently_played.slice()
                : [];

            return applySectionKey(
                cachedEntries.length
                    ? mergeHomeEntriesById(cachedEntries, serverEntries, RECENT_SECTION_STORAGE_LIMIT)
                    : serverEntries,
                sectionKey
            );
        }

        if (sectionKey === 'recent_albums')
            return applySectionKey(getRecentEntities(MUSIC.storage.recent_albums).map(mapAlbumCard), sectionKey);

        if (sectionKey === 'recent_artists')
            return applySectionKey(getRecentEntities(MUSIC.storage.recent_artists).map(mapArtistCard), sectionKey);

        if (sectionKey === 'recent_queries')
            return applySectionKey(getRecentQueries().map(mapQueryCard), sectionKey);

        var dynamicSection = home && Array.isArray(home.browse_sections)
            ? home.browse_sections.filter(function (section) { return section && section.id === sectionKey; })[0]
            : null;

        if (dynamicSection) {
            if (dynamicSection.type === 'album')
                return applySectionKey((dynamicSection.albums || []).map(mapAlbumCard), sectionKey);

            if (dynamicSection.type === 'artist')
                return applySectionKey((dynamicSection.artists || []).map(mapArtistCard), sectionKey);

            if (dynamicSection.type === 'track')
                return applySectionKey((dynamicSection.tracks || []).map(mapTrackCard), sectionKey);
        }

        return [];
    }

    function snapshotHomeSections(home) {
        var recentlyPlayed = getHomeSectionEntries('recently_played', home);

        MUSIC_HOME_SECTION_TITLES = {
            recently_played: 'Недавно слушали',
            recent_albums: 'Недавние альбомы',
            recent_artists: 'Недавние артисты',
            recent_queries: 'Недавние поиски',
            user_playlists: 'Мои плейлисты'
        };
        MUSIC_HOME_SECTION_META = {
            recently_played: { has_more: recentlyPlayed.length > HOME_SECTION_LIMIT || !!(home && home.recently_played && home.recently_played.length > HOME_SECTION_LIMIT) },
            recent_albums: { has_more: getRecentEntities(MUSIC.storage.recent_albums).length > HOME_SECTION_LIMIT },
            recent_artists: { has_more: getRecentEntities(MUSIC.storage.recent_artists).length > HOME_SECTION_LIMIT },
            recent_queries: { has_more: getRecentQueries().length > HOME_SECTION_LIMIT }
        };

        MUSIC_HOME_CACHE = {
            recently_played: recentlyPlayed,
            recent_albums: getHomeSectionEntries('recent_albums'),
            recent_artists: getHomeSectionEntries('recent_artists'),
            recent_queries: getHomeSectionEntries('recent_queries'),
            user_playlists: applySectionKey(buildUserPlaylistCards(home && Array.isArray(home.user_playlists) ? home.user_playlists : []), 'user_playlists')
        };
        MUSIC_HOME_SECTION_META.user_playlists = { has_more: (MUSIC_HOME_CACHE.user_playlists || []).length > HOME_SECTION_LIMIT };

        if (home && Array.isArray(home.browse_sections)) {
            home.browse_sections.forEach(function (section) {
                if (!section || !section.id) return;
                MUSIC_HOME_SECTION_TITLES[section.id] = section.title || 'Секция';
                MUSIC_HOME_SECTION_META[section.id] = {
                    has_more: section.has_more === true
                };
                MUSIC_HOME_CACHE[section.id] = getHomeSectionEntries(section.id, home);
            });
        }

        return MUSIC_HOME_CACHE;
    }

    function resolveHomeSectionEntries(sectionKey, done) {
        if (sectionKey === 'user_playlists') {
            loadUserPlaylists(function (playlists) {
                loadStatsTopSummary(function (stats) {
                    var cards = buildUserPlaylistCardsWithStats(playlists, stats);
                    done(applySectionKey(cards, 'user_playlists'));
                });
            });
            return;
        }

        if (sectionKey === 'stats:top') {
            request(MUSIC.endpoints.statsTop + '?limit=30', function (json) {
                json = parseJson(json);
                MUSIC_STATS_TOP.summary = json || null;

                var items = json && Array.isArray(json.tracks) ? json.tracks : [];
                var tracks = items.map(function (item) {
                    var track = item && item.track ? item.track : null;
                    if (track) track.stats_play_count = item.play_count || 0;
                    return track;
                }).filter(Boolean);

                done(applySectionKey(tracks.map(mapTrackCard), sectionKey));
            }, function () {
                done([]);
            });
            return;
        }

        if (sectionKey.indexOf('playlist:') === 0) {
            var playlistId = sectionKey.slice('playlist:'.length);

            request(MUSIC.endpoints.playlistTracks + '?id=' + encodeURIComponent(playlistId), function (json) {
                json = parseJson(json);
                var tracks = json && Array.isArray(json.tracks) ? json.tracks : [];
                done(applySectionKey(tracks.map(mapTrackCard), sectionKey));
            }, function () {
                done([]);
            });
            return;
        }

        if (sectionKey.indexOf('browse:') === 0) {
            request(MUSIC.endpoints.section + '?id=' + encodeURIComponent(sectionKey), function (json) {
                var parsed = parseJson(json) || null;
                if (parsed && parsed.available === false) {
                    done([]);
                    return;
                }

                if (parsed && parsed.id) {
                    var fakeHome = { browse_sections: [parsed] };
                    if (parsed.title) MUSIC_HOME_SECTION_TITLES[sectionKey] = parsed.title;
                    MUSIC_HOME_SECTION_META[sectionKey] = {
                        has_more: parsed.has_more === true
                    };
                    MUSIC_HOME_CACHE[sectionKey] = getHomeSectionEntries(sectionKey, fakeHome);
                    done((MUSIC_HOME_CACHE[sectionKey] || []).slice());
                    return;
                }

                done([]);
            }, function () {
                done([]);
            });
            return;
        }

        if (MUSIC_HOME_CACHE[sectionKey] && MUSIC_HOME_CACHE[sectionKey].length) {
            done(MUSIC_HOME_CACHE[sectionKey].slice());
            return;
        }

        if (sectionKey === 'recently_played') {
            request(MUSIC.endpoints.home, function (json) {
                var parsed = parseJson(json) || null;
                snapshotHomeSections(parsed);
                done((MUSIC_HOME_CACHE[sectionKey] || []).slice());
            }, function () {
                done([]);
            });
            return;
        }

        if (sectionKey === 'bookmarked_tracks') {
            done(applySectionKey(getBookmarkedEntities(MUSIC.storage.bookmarked_tracks).map(mapTrackCard), sectionKey));
            return;
        }

        if (sectionKey === 'bookmarked_albums') {
            done(applySectionKey(getBookmarkedEntities(MUSIC.storage.bookmarked_albums).map(mapAlbumCard), sectionKey));
            return;
        }

        if (sectionKey === 'bookmarked_artists') {
            done(applySectionKey(getBookmarkedEntities(MUSIC.storage.bookmarked_artists).map(mapArtistCard), sectionKey));
            return;
        }

        snapshotHomeSections(null);
        done((MUSIC_HOME_CACHE[sectionKey] || []).slice());
    }

    function openHomeSection(sectionKey, title) {
        Lampa.Activity.push({
            title: title || 'Секция',
            component: 'lampac_music_section',
            section_key: sectionKey,
            section_title: title || 'Секция',
            page: 1,
            noinfo: true
        });
    }

    function activateEntry(entry, entries, index) {
        if (!entry) return;

        if (entry.type === 'playlist_action') {
            openPlaylistAddMenu();
            return;
        }

        if (entry.type === 'playlist') {
            openHomeSection('playlist:' + entry.id, entry.title || 'Плейлист');
            return;
        }

        if (entry.type === 'stats_top') {
            openHomeSection('stats:top', 'Твой топ');
            return;
        }

        if (entry.type === 'artist') {
            openArtist(entry.raw);
            return;
        }

        if (entry.type === 'album') {
            openAlbum(entry.raw);
            return;
        }

        if (entry.type === 'track') {
            playTrack(entry.raw, entries.map(function (item) { return item.raw; }), index);
            return;
        }

        if (entry.type === 'query') {
            openSearchQuery(entry.raw);
        }
    }

    // durable-перестановка трека внутри сохранённого плейлиста: порядок
    // хранится на сервере (user_playlists.payload), воспроизведение потом
    // идёт в новом порядке. Не путать с операциями над играющей очередью
    function openPlaylistTrackMoveMenu(entry, entries, index, refresh, restoreContext) {
        var playlistId = String(entry.section_key || '').slice('playlist:'.length);
        var trackId = (entry.raw && entry.raw.id) || '';
        var lastIndex = entries.length - 1;
        var items = [];

        if (!playlistId || !trackId) return;

        if (index > 0) {
            items.push({ title: 'В начало', position: 0 });
            if (index > 1) items.push({ title: 'На позицию выше', position: index - 1 });
        }

        if (index < lastIndex) {
            if (index < lastIndex - 1) items.push({ title: 'На позицию ниже', position: index + 1 });
            items.push({ title: 'В конец', position: lastIndex });
        }

        if (!items.length) return;

        showContextMenu('Переставить: ' + ((entry.raw && entry.raw.title) || ''), items, function (selected, moveRestoreContext) {
            if (!selected || typeof selected.position !== 'number') return;

            var payload = 'id=' + encodeURIComponent(playlistId)
                + '&track_id=' + encodeURIComponent(trackId)
                + '&position=' + selected.position;
            var focusContext = captureMusicNeighborFocusContext(moveRestoreContext || restoreContext, 'content');

            requestPost(MUSIC.endpoints.playlistTrackMove, payload, function (json) {
                if (!json || !json.moved) {
                    Lampa.Noty.show('Не удалось переставить трек.');
                    restoreMusicFocusContext(moveRestoreContext || restoreContext);
                    return;
                }

                updateUserPlaylistHomeCard(json.playlist);
                notifyPlaylistChanged(playlistId, {
                    skipPlaylistSection: !!refresh,
                    skipHomeSection: !!(refresh && refresh.length > 0),
                    skipActiveSectionRefresh: !!(refresh && refresh.length === 0),
                    preserveHomeCache: !!json.playlist
                });
                // фокус ведём за треком на его новую позицию
                refreshListWithFocus(refresh, buildEntryRestoreState(entry, selected.position), focusContext);
            }, function () {
                Lampa.Noty.show('Не удалось переставить трек.');
                restoreMusicFocusContext(moveRestoreContext || restoreContext);
            });
        }, restoreContext);
    }

    function activateEntryMenu(entry, entries, index, refresh) {
        if (!entry) return;

        if (entry.type === 'playlist_action') {
            openPlaylistAddMenu();
            return;
        }

        if (entry.type === 'track') {
            var extraTrackItems = [];

            if (entry.raw && String(entry.raw.id || '').indexOf('vkchart:') === 0) {
                extraTrackItems.push({ title: 'Открыть поиск', action: 'search' });
            }

            if (entry.section_key === 'recently_played') {
                extraTrackItems.push({
                    title: 'Удалить из недавно слушали',
                    action: 'remove_recently_played'
                });
            }

            if (String(entry.section_key || '').indexOf('playlist:') === 0) {
                if (entries.length > 1) {
                    extraTrackItems.push({
                        title: 'Переставить в плейлисте…',
                        action: 'move_in_playlist'
                    });
                }

                extraTrackItems.push({
                    title: 'Убрать из плейлиста',
                    action: 'remove_from_playlist'
                });
            }

            openTrackMenu(entry.raw, entries.map(function (item) { return item.raw; }), index, extraTrackItems, function (selected, restoreContext) {
                if (!selected || !selected.action) return;

                if (selected.action === 'artist_search') {
                    openSearchQuery(entry.raw.artist_name || '');
                    return;
                }

                if (selected.action === 'search') {
                    openSearchQuery([entry.raw.artist_name, entry.raw.title].filter(Boolean).join(' '));
                    return;
                }

                if (selected.action === 'move_in_playlist') {
                    openPlaylistTrackMoveMenu(entry, entries, index, refresh, restoreContext);
                    return;
                }

                if (selected.action === 'remove_from_playlist') {
                    var playlistId = String(entry.section_key || '').slice('playlist:'.length);
                    var removePayload = 'id=' + encodeURIComponent(playlistId)
                        + '&track_id=' + encodeURIComponent((entry.raw && entry.raw.id) || '');
                    var removeRestoreContext = captureMusicNeighborFocusContext(restoreContext, 'content');

                    requestPost(MUSIC.endpoints.playlistTrackRemove, removePayload, function (json) {
                        if (!json || !json.removed) {
                            Lampa.Noty.show('Не удалось убрать из плейлиста.');
                            restoreMusicFocusContext(restoreContext);
                            return;
                        }

                        Lampa.Noty.show('Убрано из плейлиста.');
                        updateUserPlaylistHomeCard(json.playlist);
                        notifyPlaylistChanged(playlistId, {
                            skipPlaylistSection: !!refresh,
                            skipHomeSection: !!(refresh && refresh.length > 0),
                            skipActiveSectionRefresh: !!(refresh && refresh.length === 0),
                            preserveHomeCache: !!json.playlist
                        });
                        refreshListWithFocus(refresh, buildEntryRestoreState(entry, index), removeRestoreContext);
                    }, function () {
                        Lampa.Noty.show('Не удалось убрать из плейлиста.');
                        restoreMusicFocusContext(restoreContext);
                    });
                    return;
                }

                if (selected.action !== 'remove_recently_played') return;

                var historyRestoreContext = captureMusicNeighborFocusContext(restoreContext, 'content');
                var historyHomeRefresh = !!(refresh && refresh.length > 0);

                removeHistoryTrack(entry.raw && entry.raw.id, function (removed) {
                    if (!removed) {
                        Lampa.Noty.show('Не удалось удалить из истории.');
                        restoreMusicFocusContext(restoreContext);
                        return;
                    }

                    Lampa.Noty.show('Удалено из недавно слушали.');
                    if (refresh) {
                        if (historyHomeRefresh)
                            refreshListWithFocus(refresh, buildEntryRestoreState(entry, index), historyRestoreContext);
                        return;
                    }

                    restoreMusicFocusContext(historyRestoreContext, 120);
                }, { skipEvent: historyHomeRefresh });
            }, function (added, restoreContext) {
                if (entry.section_key === 'bookmarked_tracks' && !added && refresh) {
                    var trackRestoreContext = captureMusicNeighborFocusContext(restoreContext, 'content');
                    refreshListWithFocus(refresh, buildEntryRestoreState(entry, index), trackRestoreContext);
                    return true;
                }

                return false;
            });
            return;
        }

        if (entry.type === 'playlist') {
            var playlistItems = [
                { title: 'Открыть', action: 'open' },
                { title: 'Удалить плейлист', action: 'delete' }
            ];

            var sourceService = playlistSourceService(entry);
            if (sourceService)
                playlistItems.splice(1, 0, { title: 'Обновить из ' + sourceService, action: 'sync_source' });

            showContextMenu(entry.title || 'Плейлист', playlistItems, function (selected, restoreContext) {
                if (!selected) return;

                if (selected.action === 'open') {
                    activateEntry(entry, entries, index);
                    return;
                }

                if (selected.action === 'sync_source') {
                    syncImportedPlaylist(entry, refresh, false, restoreContext, index);
                    return;
                }

                if (selected.action !== 'delete') return;

                var deleteRestoreContext = captureMusicNeighborFocusContext(restoreContext, 'content');

                requestPost(MUSIC.endpoints.playlistDelete, 'id=' + encodeURIComponent(entry.id || ''), function (json) {
                    if (!json || !json.removed) {
                        Lampa.Noty.show('Не удалось удалить плейлист.');
                        restoreMusicFocusContext(restoreContext);
                        return;
                    }

                    Lampa.Noty.show('Плейлист удалён.');
                    notifyPlaylistChanged(entry.id, {
                        skipPlaylistSection: true,
                        skipHomeSection: !!(refresh && refresh.length > 0),
                        skipActiveSectionRefresh: !!(refresh && refresh.length === 0)
                    });
                    refreshListWithFocus(refresh, buildEntryRestoreState(entry, index), deleteRestoreContext);
                }, function () {
                    Lampa.Noty.show('Не удалось удалить плейлист.');
                    restoreMusicFocusContext(restoreContext);
                });
            });
            return;
        }

        if (entry.type === 'artist') {
            var artistBookmarked = isBookmarkedEntity(MUSIC.storage.bookmarked_artists, entry.raw && entry.raw.id);
            var artistItems = [
                { title: 'Открыть поиск', action: 'search' },
                { title: 'Открыть дискографию', action: 'catalog' },
                { title: artistBookmarked ? 'Убрать из закладок' : 'Добавить в закладки', action: 'toggle_bookmark' }
            ];

            if (entry.section_key === 'recent_artists') {
                artistItems.push({
                    title: 'Удалить из недавних артистов',
                    action: 'remove_recent_artist'
                });
            }

            showContextMenu(entry.title || 'Артист', artistItems, function (selected, restoreContext) {
                if (selected.action === 'search')
                    openSearchQuery(entry.raw && entry.raw.name ? entry.raw.name : entry.title);
                else if (selected.action === 'catalog')
                    openArtistCatalog(entry.raw);
                else if (selected.action === 'toggle_bookmark') {
                    var artistRestoreContext = captureMusicNeighborFocusContext(restoreContext, 'content');
                    var added = toggleBookmarkedEntity(MUSIC.storage.bookmarked_artists, entry.raw);
                    Lampa.Noty.show(added ? 'Артист добавлен в закладки.' : 'Артист удалён из закладок.');
                    if (entry.section_key === 'bookmarked_artists' && refresh) {
                        refreshListWithFocus(refresh, buildEntryRestoreState(entry, index), artistRestoreContext);
                    } else {
                        restoreMusicFocusContext(restoreContext, 120);
                    }
                }
                else if (selected.action === 'remove_recent_artist') {
                    var recentArtistRestoreContext = captureMusicNeighborFocusContext(restoreContext, 'content');
                    var recentArtistHomeRefresh = !!(refresh && refresh.length > 0);

                    if (!removeRecentEntity(MUSIC.storage.recent_artists, entry.raw || entry, { skipEvent: recentArtistHomeRefresh })) {
                        Lampa.Noty.show('Не удалось удалить артиста.');
                        restoreMusicFocusContext(restoreContext);
                        return;
                    }

                    Lampa.Noty.show('Артист удалён из недавних.');
                    if (refresh) {
                        if (recentArtistHomeRefresh)
                            refreshListWithFocus(refresh, buildEntryRestoreState(entry, index), recentArtistRestoreContext);
                        return;
                    }

                    restoreMusicFocusContext(recentArtistRestoreContext, 120);
                }
                else
                    openSearchQuery(entry.raw && entry.raw.name ? entry.raw.name : entry.title);
            });
            return;
        }

        if (entry.type === 'album') {
            var albumBookmarked = isBookmarkedEntity(MUSIC.storage.bookmarked_albums, entry.raw && entry.raw.id);
            var albumItems = [
                { title: 'Открыть альбом', action: 'open' },
                { title: albumBookmarked ? 'Убрать из закладок' : 'Добавить в закладки', action: 'toggle_bookmark' },
                { title: 'Открыть поиск', action: 'search' }
            ];

            if (entry.section_key === 'recent_albums') {
                albumItems.push({
                    title: 'Удалить из недавних альбомов',
                    action: 'remove_recent_album'
                });
            }

            showContextMenu(entry.title || 'Альбом', albumItems, function (selected, restoreContext) {
                if (selected.action === 'search')
                    openSearchQuery([entry.raw && entry.raw.artist_name, entry.raw && entry.raw.title].filter(Boolean).join(' '));
                else if (selected.action === 'toggle_bookmark') {
                    var albumRestoreContext = captureMusicNeighborFocusContext(restoreContext, 'content');
                    var added = toggleBookmarkedEntity(MUSIC.storage.bookmarked_albums, entry.raw);
                    Lampa.Noty.show(added ? 'Альбом добавлен в закладки.' : 'Альбом удалён из закладок.');
                    if (entry.section_key === 'bookmarked_albums' && refresh) {
                        refreshListWithFocus(refresh, buildEntryRestoreState(entry, index), albumRestoreContext);
                    } else {
                        restoreMusicFocusContext(restoreContext, 120);
                    }
                }
                else if (selected.action === 'remove_recent_album') {
                    var recentAlbumRestoreContext = captureMusicNeighborFocusContext(restoreContext, 'content');
                    var recentAlbumHomeRefresh = !!(refresh && refresh.length > 0);

                    if (!removeRecentEntity(MUSIC.storage.recent_albums, entry.raw || entry, { skipEvent: recentAlbumHomeRefresh })) {
                        Lampa.Noty.show('Не удалось удалить альбом.');
                        restoreMusicFocusContext(restoreContext);
                        return;
                    }

                    Lampa.Noty.show('Альбом удалён из недавних.');
                    if (refresh) {
                        if (recentAlbumHomeRefresh)
                            refreshListWithFocus(refresh, buildEntryRestoreState(entry, index), recentAlbumRestoreContext);
                        return;
                    }

                    restoreMusicFocusContext(recentAlbumRestoreContext, 120);
                }
                else
                    openAlbum(entry.raw);
            });
            return;
        }

        if (entry.type === 'query') {
            var queryItems = [
                { title: 'Открыть поиск', action: 'search' }
            ];

            if (entry.section_key === 'recent_queries') {
                queryItems.push({
                    title: 'Удалить из недавних поисков',
                    action: 'remove_recent_query'
                });
            }

            showContextMenu(entry.title || 'Поиск', queryItems, function (selected, restoreContext) {
                if (!selected || !selected.action) return;

                if (selected.action === 'remove_recent_query') {
                    var recentQueryRestoreContext = captureMusicNeighborFocusContext(restoreContext, 'content');
                    var recentQueryHomeRefresh = !!(refresh && refresh.length > 0);

                    if (!removeRecentQuery(entry.raw, { skipEvent: recentQueryHomeRefresh })) {
                        Lampa.Noty.show('Не удалось удалить запрос.');
                        restoreMusicFocusContext(restoreContext);
                        return;
                    }

                    Lampa.Noty.show('Запрос удалён из недавних.');
                    if (refresh) {
                        if (recentQueryHomeRefresh)
                            refreshListWithFocus(refresh, buildEntryRestoreState(entry, index), recentQueryRestoreContext);
                        return;
                    }

                    restoreMusicFocusContext(recentQueryRestoreContext, 120);
                    return;
                }

                openSearchQuery(entry.raw);
            });
        }
    }

    function MusicCardItem(item) {
        var _this = this;
        var alive = true;
        var html = $('<div class="selector lm-card"></div>');
        var view = $('<div class="lm-card__view"></div>');
        var img = $('<img class="lm-card__img" src="" alt="" />');
        var badge = $('<div class="lm-card__badge"></div>').text(item.badge || '');
        var info = $('<div class="lm-card__info"></div>');
        var title = $('<div class="lm-card__title"></div>').text(item.title || 'Untitled');
        var subtitle = $('<div class="lm-card__subtitle"></div>').text(item.subtitle || '');
        var element = html[0];
        var onVisible = function () {
            if (_this.onVisible) _this.onVisible(element, item);
        };
        var onUpdate = function () {
            if (_this.onUpdate) _this.onUpdate(element, item);
        };
        var focusKey = buildMusicFocusKey(item);

        if (focusKey)
            html.attr('data-music-focus-key', focusKey);

        function artistImageTarget() {
            if (item.type === 'artist' && item.raw) return item.raw;
            return null;
        }

        function refreshArtistImage() {
            var target = artistImageTarget();
            if (!target || !alive) return;

            item.image = artistImage(target, 250);
            item.background = artistImage(target, 250);
            if (img.attr('src') !== item.image) {
                html.removeClass('loaded');
                img.attr('src', item.image || IMG_BG);
            }

            if (html.hasClass('focus'))
                setBackground(item.background || IMG_BG);
        }

        function ensureArtistImage() {
            var target = artistImageTarget();
            if (!target || !alive) return;
            if (artistHasRemoteImage(target) || item._artistImageRequested) return;

            item._artistImageRequested = true;

            requestArtistImage(target, function (images) {
                if (!alive || !images || !images.length) return;

                target.images = images;
                if (item.type === 'query' && item.raw)
                    updateRecentQueryArtist(item.raw, target, false);
                refreshArtistImage();
            });
        }

        img.on('load', function () {
            html.addClass('loaded');
        });
        img.on('error', function () {
            img.attr('src', item.image || IMG_BG);
            html.addClass('loaded');
        });
        img.attr('src', item.image || IMG_BG);

        html.on('hover:focus', function () {
            ensureArtistImage();
            if (item.type === 'track' && item.raw)
                schedulePlayPrefetch(item.raw);
            if (_this.onFocus) _this.onFocus(html[0], item);
        });

        html.on('hover:enter', function () {
            if (_this.onEnter) _this.onEnter(html[0], item);
        });

        html.on('hover:touch', function () {
            if (_this.onHover) _this.onHover(html[0], item);
        });

        html.on('hover:long', function () {
            if (_this.onMenu) _this.onMenu(html[0], item);
        });

        view.append(img);
        if (item.badge) view.append(badge);
        info.append(title);
        info.append(subtitle);
        html.append(view);
        html.append(info);

        this.create = function () {
            element.addEventListener('visible', onVisible);
            element.addEventListener('update', onUpdate);
        };

        this.visible = function () {
            ensureArtistImage();
            onVisible();
        };

        this.update = function () {
            onUpdate();
        };

        this.render = function (js) {
            return js ? html[0] : html;
        };

        this.ensureArtistImage = ensureArtistImage;

        this.destroy = function () {
            alive = false;
            element.removeEventListener('visible', onVisible);
            element.removeEventListener('update', onUpdate);
            img.off();
            html.off();
            html.remove();
        };
    }

    function buildTrackRow(track, index, options) {
        options = options || {};

        var isCurrent = !!(track && track.id && MUSIC_QUEUE && MUSIC_QUEUE.currentTrackId === track.id);
        var row = $('<div class="selector lm-track"></div>');
        var number = $('<div class="lm-track__num"></div>');
        // в плейлисте номер = позиция в списке (positionNumber), иначе после
        // перестановки трек показывал бы свой track_number из источника;
        // в альбоме наоборот — track_number и есть правильный номер дорожки
        var numText = $('<span class="lm-track__num-text"></span>').text(isCurrent
            ? '♪'
            : (options.positionNumber ? (index + 1) : (track.track_number || track.number || (index + 1))));
        var numPlay = $('<span class="lm-track__num-play">▶</span>');
        var body = $('<div class="lm-track__body"></div>');
        var name = $('<div class="lm-track__name"></div>').text(track.title || 'Untitled');
        var meta = [];
        var metaText = $('<div class="lm-track__meta"></div>');
        var time = $('<div class="lm-track__time"></div>').text(formatDuration(track.duration_ms));

        if (track.artist_name) meta.push(track.artist_name);
        if (track.album_title && !options.hideAlbum) meta.push(track.album_title);
        if (track.disc_number) meta.push('Disc ' + track.disc_number);
        // экран «Твой топ»: счётчик и личная «сертификация» (×52 · золото)
        if (track.stats_play_count) {
            meta.push('×' + track.stats_play_count);

            var certification = trackCertificationLabel(track.stats_play_count);
            if (certification) meta.push(certification);
        }
        metaText.text(meta.join(' · '));

        number.append(numText);
        number.append(numPlay);

        body.append(name);
        if (meta.length) body.append(metaText);

        row.append(number);

        if (options.thumb) {
            var thumb = $('<div class="lm-track__thumb"></div>');
            var thumbImg = $('<img alt="" />');

            thumbImg.attr('loading', 'lazy');
            thumbImg.attr('decoding', 'async');
            thumbImg.attr('src', options.thumb);
            thumb.append(thumbImg);
            row.append(thumb);
        }

        row.append(body);
        row.append(time);

        if (isCurrent) row.addClass('lm-track--current');

        return row;
    }

    function bindMusicLineMoreFocus(line, onFocusMore) {
        if (!line || !line.render || typeof onFocusMore !== 'function') return;

        function attach() {
            var more = $(line.render(true)).find('.card-more.selector')[0];
            if (!more || more._musicMoreBound) return;

            more._musicMoreBound = true;
            $(more).on('hover:focus hover:touch', function () {
                onFocusMore();
            });
        }

        requestAnimationFrame(attach);

        if (line.scroll && typeof line.scroll.onScroll === 'function') {
            var originalOnScroll = line.scroll.onScroll;
            line.scroll.onScroll = function () {
                originalOnScroll.apply(this, arguments);
                attach();
            };
        }
    }

    // ===== HOME / CARD UI =====

    function createMusicLine(config) {
        var title = config.title || '';
        var entries = Array.isArray(config.entries) ? config.entries : [];
        var hasMore = !!config.hasMore;

        entries.forEach(function (entry, index) {
            entry.params = entry.params || {};
            entry.params.createInstance = function (item) {
                return new MusicCardItem(item);
            };
            entry.params.on = entry.params.on || {};
            entry.params.on['hover:enter'] = function (item, data) {
                if (config.onSelect)
                    config.onSelect(data, index);
            };
            entry.params.on['hover:long'] = function (item, data) {
                if (config.onMenu)
                    config.onMenu(data, index);
            };
        });

        var line = Lampa.Maker.make('Line', {
            title: title,
            results: entries,
            total_pages: hasMore ? 2 : 1,
            params: {
                items: {
                    mapping: 'line',
                    align_left: false,
                    view: entries.length || HOME_SECTION_LIMIT
                }
            }
        }, function (module) {
            return module.only('Items', 'Create', 'More');
        });

        line.use({
            onActive: function (item, entry) {
                if (config.onFocus)
                    config.onFocus(entry, item);
            },
            onMore: function () {
                if (config.onMore)
                    config.onMore();
            },
            onDown: function () {
                if (config.onDown)
                    config.onDown();
            },
            onUp: function () {
                if (config.onUp)
                    config.onUp();
            },
            onBack: function () {
                if (config.onBack)
                    config.onBack();
            }
        });

        line.create();
        bindMusicLineMoreFocus(line, config.onFocusMore);

        return line;
    }

    function bindBase(instance, scroll, getLast) {
        instance.back = function () {
            Lampa.Activity.backward();
        };

        instance.start = function () {
            if (Lampa.Activity.active().activity !== instance.activity) return;

            Lampa.Controller.add('content', {
                toggle: function () {
                    if (instance.onControllerToggle && instance.onControllerToggle() === true) return;
                    var last = getLast();
                    if (instance.resolveControllerFocusTarget)
                        last = instance.resolveControllerFocusTarget(last);

                    var container = instance.getControllerFocusContainer
                        ? instance.getControllerFocusContainer(last)
                        : scroll.render();

                    Lampa.Controller.collectionSet(container || scroll.render());
                    Lampa.Controller.collectionFocus(last || false, container || scroll.render());
                    if (last) scroll.update($(last), true);
                },
                left: function () {
                    if (instance.onControllerLeft && instance.onControllerLeft() === true) return;
                    if (Navigator.canmove('left')) Navigator.move('left');
                    else Lampa.Controller.toggle('menu');
                },
                right: function () {
                    if (instance.onControllerRight && instance.onControllerRight() === true) return;
                    Navigator.move('right');
                },
                up: function () {
                    if (instance.onControllerUp && instance.onControllerUp() === true) return;
                    if (Navigator.canmove('up')) Navigator.move('up');
                    else Lampa.Controller.toggle('head');
                },
                down: function () {
                    if (instance.onControllerDown && instance.onControllerDown() === true) return;
                    if (Navigator.canmove('down')) Navigator.move('down');
                },
                back: instance.back
            });

            Lampa.Controller.toggle('content');
        };

        instance.pause = function () {};
        instance.stop = function () {};
    }

    function MusicComponent(object) {
        var scroll = new Lampa.Scroll({
            mask: false,
            over: true,
            step: 250
        });
        var items = [];
        var html = $('<div></div>');
        var body = $('<div class="category-full lm-grid"></div>');
        var head = $('<div class="lm-search-bar category-full"></div>');
        var actions = $('<div class="lm-search-actions"></div>');
        var searchScreenMode = !!object.search_mode;
        var searchBtn = $('<div class="selector lm-search-btn lm-search-btn--icon lm-search-btn--search" aria-label="Поиск музыки">' + SEARCH_ICON + '</div>');
        var filterBtn = $('<div class="selector lm-search-btn lm-search-btn--icon lm-search-btn--filter" aria-label="Фильтр">' + FILTER_ICON + '</div>');
        var bookmarksBtn = $('<div class="selector lm-search-btn lm-search-btn--icon lm-search-btn--bookmark" aria-label="Закладки">' + BOOKMARK_ICON + '</div>');
        var authBtn = $('<div class="selector lm-auth-btn">🔐 Авторизация</div>');
        var homeBtn = $('<div class="selector lm-search-btn lm-search-btn--icon lm-search-btn--home" aria-label="Главная">' + HOME_ICON + '</div>');
        var status = $('<div class="lm-search-status"></div>');
        var authStatus = $('<div class="lm-auth-status"></div>');
        var searchInputWrap = $('<div class="lm-search-input-wrap"></div>');
        var searchInputField = $('<input class="lm-search-input-field" type="text" autocomplete="off" autocorrect="off" autocapitalize="off" spellcheck="false" />');
        var searchHistoryWrap = $('<div class="lm-search-history"></div>');
        var last;
        var destroyed = false;
        var currentQuery = object.query || '';
        var searchRequestToken = 0;
        var searchPreserveInputFocus = false;
        var searchInputFocusTime = 0;
        var homeLines = [];
        var homeHeroCard = null;
        var homeMode = false;
        var homeActiveLine = -1;
        var searchLines = [];
        var searchSources = [];
        var searchSourceTabs = [];
        var searchSourceTabsWrap = $('<div class="lm-search-sources"></div>');
        var searchActiveSourceId = '';
        var searchMode = false;
        var searchActiveLine = -1;
        var searchFocusZone = searchScreenMode ? 'buttons' : '';
        var searchHistoryTargetIndex = 0;
        var searchSourceTargetIndex = 0;
        var searchMetadataRefreshTimer = null;
        var headerFocusTarget = null;
        var recentDirty = false;
        var unbindRecentListener = function () {};
        var _this = this;

        bindBase(this, scroll, function () { return last || searchBtn[0]; });

        this.resolveControllerFocusTarget = function (target) {
            if (!target || !document.documentElement.contains(target) || !$(target).hasClass('selector'))
                target = headerFocusTarget || searchBtn[0];

            if (homeMode && isHomeHeaderTarget(target)) {
                homeActiveLine = -1;
                headerFocusTarget = target;
            }

            if (searchMode && (isHomeHeaderTarget(target) || isSearchScreenButton(target)))
                searchActiveLine = -1;

            return target || searchBtn[0];
        };

        this.getControllerFocusContainer = function (target) {
            if (!target) return scroll.render();

            return $(target).closest(
                '.lm-home-line,.lm-search-line,.lm-search-actions,.lm-search-sources,.lm-search-history,.lm-search-bar,.lm-search-screen'
            )[0] || scroll.render();
        };

        var baseStart = this.start;
        this.start = function () {
            baseStart();

            if (homeMode && (recentDirty || MUSIC_DEFERRED_HOME_REFRESH)) {
                recentDirty = false;
                MUSIC_DEFERRED_HOME_REFRESH = false;
                _this.loadHome(buildHomeRestoreFromLast());
            }
        };

        function handleRecentChanged(e) {
            var detail = e && e.detail ? e.detail : {};

            if (!homeMode) return;
            if (!detail.section_key) return;
            if (String(detail.section_key).indexOf('playlist:') === 0) return;

            recentDirty = true;

            if (isLampaPlayerOverlayOpen()) {
                traceEmbeddedIos('recent-refresh-deferred', detail.section_key || '', true);
                return;
            }

            if (Lampa.Activity.active().activity === _this.activity) {
                var restoreState = detail.payload && detail.payload.restoreState
                    ? detail.payload.restoreState
                    : null;

                recentDirty = false;
                MUSIC_HOME_REFRESH_RESTORE_BLOCK_UNTIL = Date.now() + 1500;
                _this.loadHome(restoreState || buildHomeRestoreFromLast() || {
                    sectionKey: detail.section_key,
                    entryIndex: 0
                });
            }
        }

        function restoreAfterSearchInput(target) {
            setTimeout(function () {
                if (destroyed) return;
                if (Lampa.Activity.active().activity !== _this.activity) return;

                last = target || last || searchBtn[0];
                Lampa.Controller.toggle('content');

                if (last) scroll.update($(last), true);
            }, 0);
        }

        function updateSearchButtonDisplay() {
            if (!searchScreenMode) return;

            var title = currentQuery || 'Введите текст...';
            if (searchInputField && searchInputField.length) {
                if (searchInputField.val() !== currentQuery)
                    searchInputField.val(currentQuery);
                searchInputField.attr('placeholder', 'Введите текст...');
            } else {
                searchBtn.text(title);
            }

            searchBtn.toggleClass('lm-search-input-btn--placeholder', !currentQuery);
        }

        function submitInlineSearch(value, options) {
            options = options || {};

            var previousQuery = currentQuery;
            var nextQuery = String(value || '').trim();

            currentQuery = nextQuery;
            object.query = nextQuery;
            updateSearchButtonDisplay();

            if (!nextQuery) {
                searchRequestToken++;
                searchPreserveInputFocus = !!options.keepInputFocus;
                _this.loadSearchLanding(!!options.keepInputFocus);
                return;
            }

            if (options.saveHistory) {
                saveLastQuery(nextQuery);
                renderSearchHistory();
            }

            if (!options.force && nextQuery === previousQuery)
                return;

            searchPreserveInputFocus = !!options.keepInputFocus;

            if (!searchPreserveInputFocus && searchInputField && searchInputField.length && document.activeElement === searchInputField[0]) {
                try {
                    searchInputField[0].blur();
                } catch (e) {}
            }

            _this.activity.loader(true);
            _this.loadSearch();
        }

        function searchScreenButtons() {
            if (!searchScreenMode) return [];

            var buttons = [filterBtn[0], bookmarksBtn[0], homeBtn[0]];
            if (MUSIC.features.auth) buttons.push(authBtn[0]);
            return buttons.filter(function (button) {
                return !!(button && button.parentNode);
            });
        }

        function setSearchFocusZone(zone) {
            if (!searchScreenMode) return;
            searchFocusZone = zone || '';
        }

        function registerSearchFocusControllers() {
            if (!searchScreenMode) return;

            Lampa.Controller.add('music_search_history', {
                toggle: function () {
                    var items = searchHistoryWrap.find('.selector');
                    var target = items.eq(Math.max(0, Math.min(searchHistoryTargetIndex, Math.max(0, items.length - 1)))).get(0) || items.get(0);
                    if (!target) return;

                    last = target;
                    Lampa.Controller.collectionSet(searchHistoryWrap[0]);
                    Lampa.Controller.collectionFocus(target, searchHistoryWrap[0]);
                    scroll.update($(target), true);
                },
                left: function () {
                    Navigator.move('left');
                },
                right: function () {
                    Navigator.move('right');
                },
                up: function () {
                    focusSearchInput();
                },
                down: function () {
                    if (searchSourceTabs.length)
                        focusSearchSourceTab(Math.max(0, getSearchSourceIndexById(searchActiveSourceId)));
                    else if (searchLines.length)
                        openSearchLine(0);
                },
                back: _this.back
            });

            Lampa.Controller.add('music_search_sources', {
                toggle: function () {
                    var target = searchSourceTabs[Math.max(0, Math.min(searchSourceTargetIndex, Math.max(0, searchSourceTabs.length - 1)))] || searchSourceTabs[0];
                    if (!target) return;

                    last = target;
                    Lampa.Controller.collectionSet(searchSourceTabsWrap[0]);
                    Lampa.Controller.collectionFocus(target, searchSourceTabsWrap[0]);
                    scroll.update($(target), true);
                },
                left: function () {
                    Navigator.move('left');
                },
                right: function () {
                    Navigator.move('right');
                },
                up: function () {
                    if (searchHistoryWrap.find('.selector').length)
                        focusSearchHistoryItem(0);
                    else
                        focusSearchInput();
                },
                down: function () {
                    if (ensureFocusedSearchSourceActive()) return;
                    if (searchLines.length) openSearchLine(0);
                },
                back: _this.back
            });
        }

        function applySearchFocus(container, target) {
            if (!target) return false;

            var useContainer = container || scroll.render();
            last = target;
            scroll.update($(target), true);
            Lampa.Controller.collectionSet(useContainer);
            Lampa.Controller.collectionFocus(target || false, useContainer);
            return true;
        }

        function focusSearchInput() {
            searchActiveLine = -1;
            setSearchFocusZone('input');
            headerFocusTarget = searchBtn[0];
            Lampa.Controller.toggle('content');
            return applySearchFocus(searchInputWrap[0], searchBtn[0]);
        }

        function isSearchScreenButton(element) {
            return searchScreenButtons().indexOf(element) >= 0;
        }

        function focusSearchScreenButton() {
            var buttons = searchScreenButtons();
            var target = buttons.indexOf(headerFocusTarget) >= 0 ? headerFocusTarget : buttons[0];
            if (!target) return false;

            searchActiveLine = -1;
            setSearchFocusZone('buttons');
            focusSearchHeader(target);
            return applySearchFocus(actions[0], target);
        }

        function findSearchLineFocusTarget(lineElement) {
            if (!lineElement) return null;

            var cards = $(lineElement).find('.lm-card.selector');
            if (cards.length) return cards.get(0);

            var nonMore = $(lineElement).find('.selector').not('.card-more');
            if (nonMore.length) return nonMore.get(0);

            return $(lineElement).find('.card-more.selector').get(0) || null;
        }

        function renderSearchHistory() {
            if (!searchScreenMode) return;

            searchHistoryWrap.empty();

            var recentQueries = getRecentQueries().slice(0, RECENT_QUERY_QUICK_LIMIT);
            recentQueries.forEach(function (item) {
                var entry = mapQueryCard(item);
                var chip = $('<div class="selector lm-search-history-item"></div>');
                var icon = $('<span class="lm-search-history-item__icon">◷</span>');
                var text = $('<span class="lm-search-history-item__title"></span>').text(entry.title);

                chip.append(icon);
                chip.append(text);

                chip.on('hover:focus', function () {
                    setSearchFocusZone('history');
                    last = chip[0];
                    scroll.update(chip, true);
                    setBackground(entry.background || IMG_BG);
                });

                chip.on('hover:enter hover:long', function () {
                    openSearchQuery(entry.title);
                });

                searchHistoryWrap.append(chip);
            });

            searchHistoryWrap.toggleClass('hide', !recentQueries.length);
        }

        function destroyHomeItems() {
            Lampa.Arrays.destroy(items);
            items = [];
        }

        function buildHomeRestoreFromLast() {
            if (!homeMode) return null;

            var lineIndex = detectHomeLineIndex(last);
            if (lineIndex < 0 || lineIndex >= homeLines.length || !homeLines[lineIndex])
                return null;

            var lineElement = homeLines[lineIndex].render(true);
            var cards = $(lineElement).find('.lm-card');
            var cardElement = $(last).closest('.lm-card')[0];
            var cardIndex = cardElement ? cards.index(cardElement) : 0;

            return {
                sectionKey: homeLines[lineIndex].section_key,
                entryIndex: Math.max(0, cardIndex)
            };
        }

        function restoreHomeAfterRender(restoreState) {
            if (!restoreState || !restoreState.sectionKey) return;

            requestAnimationFrame(function () {
                if (destroyed || !homeMode || Lampa.Activity.active().activity !== _this.activity) return;

                var lineIndex = -1;
                for (var i = 0; i < homeLines.length; i++) {
                    if (homeLines[i] && homeLines[i].section_key === restoreState.sectionKey) {
                        lineIndex = i;
                        break;
                    }
                }

                if (lineIndex < 0 || !homeLines[lineIndex]) {
                    focusHomeHero();
                    return;
                }

                function restoreHomeLine() {
                    if (destroyed || !homeMode || Lampa.Activity.active().activity !== _this.activity) return;
                    openHomeLine(lineIndex);
                }

                requestAnimationFrame(restoreHomeLine);
                setTimeout(restoreHomeLine, 120);
            });
        }

        this.create = function () {
            this.activity.loader(true);
            unbindRecentListener = bindRecentListener(handleRecentChanged);
            registerSearchFocusControllers();

            if (searchScreenMode) {
                head.removeClass('lm-search-bar').addClass('lm-search-screen category-full');
                updateSearchButtonDisplay();
                searchBtn.removeClass('lm-search-btn lm-search-btn--icon lm-search-btn--search').addClass('lm-search-input-btn');
                searchBtn.empty().append(searchInputField);
                actions.append(filterBtn);
                actions.append(bookmarksBtn);
                actions.append(homeBtn);
                if (MUSIC.features.auth) actions.append(authBtn);
                head.append(actions);
                searchInputWrap.append(searchBtn);
                head.append(searchInputWrap);
                head.append(searchHistoryWrap);
                head.append(searchSourceTabsWrap);
            } else {
                actions.append(searchBtn);
                actions.append(filterBtn);
                actions.append(bookmarksBtn);
                if (MUSIC.features.auth) actions.append(authBtn);
                if (object.query) actions.append(homeBtn);
                head.append(actions);
                head.append(status);
                if (MUSIC.features.auth) head.append(authStatus);
            }
            scroll.append(head);
            scroll.append(body);
            html.append(scroll.render());

            last = searchBtn[0];
            headerFocusTarget = searchBtn[0];
            updateFilterButton(filterBtn);

            searchBtn.on('hover:focus', function () {
                if (homeMode) homeActiveLine = -1;
                last = searchBtn[0];
                if (!searchScreenMode)
                    headerFocusTarget = searchBtn[0];
                else
                    setSearchFocusZone('input');
                scroll.update(searchBtn, true);
                setBackground(artwork('album', currentQuery || 'Музыка', searchScreenMode ? 'Поиск' : 'Поиск музыки', ['#172434', '#375d7f']));
            });

            searchBtn.on('hover:enter', function () {
                if (searchScreenMode) {
                    if (searchInputField && searchInputField.length) {
                        searchInputField[0].focus();
                        searchInputField[0].select();
                    }
                    return;
                }

                if (homeMode) {
                    openSearchScreen('');
                    return;
                }

                openSearchInput(currentQuery, {
                    ignoreSameValue: true,
                    onCancel: function () {
                        restoreAfterSearchInput(searchBtn[0]);
                    }
                });
            });

            if (!searchScreenMode) {
                searchBtn.on('hover:long', function () {
                    openSearchActionsMenu();
                });
            }

            if (searchScreenMode) {
                searchInputField.on('focus', function () {
                    searchInputFocusTime = Date.now();
                    setSearchFocusZone('input');
                    last = searchBtn[0];
                    scroll.update(searchBtn, true);
                });

                searchInputField.on('click mousedown mouseup input keypress paste', function (event) {
                    event.stopPropagation();
                });

                searchInputField.on('keydown', function (event) {
                    if (event.key === 'Enter' || event.key === 'ArrowDown' || event.key === 'ArrowUp' || event.key === 'Escape') {
                        event.preventDefault();
                        event.stopPropagation();
                    }

                    event.stopPropagation();
                });

                searchInputField.on('keyup', function (event) {
                    event.stopPropagation();

                    if (searchInputFocusTime + 150 > Date.now()) return;

                    if (event.key === 'Enter') {
                        event.preventDefault();
                        this.blur();
                        submitInlineSearch($(this).val(), { force: true, saveHistory: true });
                        return;
                    }

                    if (event.key === 'ArrowDown') {
                        event.preventDefault();
                        this.blur();

                        if (searchHistoryWrap.find('.selector').length)
                            focusSearchHistoryItem(0);
                        else if (searchSourceTabs.length)
                            focusSearchSourceTab(Math.max(0, getSearchSourceIndexById(searchActiveSourceId)));
                        else if (searchLines.length)
                            openSearchLine(0);

                        return;
                    }

                    if (event.key === 'ArrowUp' || event.key === 'Escape') {
                        event.preventDefault();
                        this.blur();
                        focusSearchScreenButton();
                    }
                });
            }

            filterBtn.on('hover:focus', function () {
                if (homeMode) homeActiveLine = -1;
                if (searchScreenMode) setSearchFocusZone('buttons');
                last = filterBtn[0];
                headerFocusTarget = filterBtn[0];
                scroll.update(filterBtn, true);
                setBackground(artwork('track', 'Фильтр', getPlaybackModeTitle() + ' · ' + getQualityModeTitle(), ['#1f2734', '#3f5f7d']));
            });

            filterBtn.on('hover:enter', function () {
                openFilterMenu(filterBtn);
            });

            filterBtn.on('hover:long', function () {
                openFilterActionsMenu();
            });

            bookmarksBtn.on('hover:focus', function () {
                if (homeMode) homeActiveLine = -1;
                if (searchScreenMode) setSearchFocusZone('buttons');
                last = bookmarksBtn[0];
                headerFocusTarget = bookmarksBtn[0];
                scroll.update(bookmarksBtn, true);
                setBackground(artwork('album', 'Закладки', 'Артисты · Альбомы · Треки', ['#1d2632', '#486077']));
            });

            bookmarksBtn.on('hover:enter', function () {
                openBookmarksScreen();
            });

            bookmarksBtn.on('hover:long', function () {
                openBookmarksScreen();
            });

            homeBtn.on('hover:focus', function () {
                if (homeMode) homeActiveLine = -1;
                if (searchScreenMode) setSearchFocusZone('buttons');
                last = homeBtn[0];
                headerFocusTarget = homeBtn[0];
                scroll.update(homeBtn, true);
                setBackground(artwork('album', 'Музыка', 'Главная', ['#15202c', '#35556c']));
            });

            homeBtn.on('hover:enter', function () {
                Lampa.Activity.push({
                    title: MUSIC.title,
                    component: 'lampac_music_home',
                    page: 1,
                    noinfo: true
                });
            });

            homeBtn.on('hover:long', function () {
                openHomeActionsMenu();
            });

            if (MUSIC.features.auth) {
                authBtn.on('hover:focus', function () {
                    if (homeMode) homeActiveLine = -1;
                    if (searchScreenMode) setSearchFocusZone('buttons');
                    last = authBtn[0];
                    headerFocusTarget = authBtn[0];
                    scroll.update(authBtn, true);
                    setBackground(artwork('album', 'Yandex Music', 'Авторизация', ['#2b173b', '#8a2be2']));
                });

                authBtn.on('hover:enter', function () {
                    openAuthMenu(authStatus);
                });

                refreshAuthStatus(authStatus);
            }

            if (object.query)
                this.loadSearch();
            else if (object.search_mode)
                this.loadSearchLanding();
            else
                this.loadHome();

            return this.render();
        };

        this.clean = function () {
            clearSearchMetadataRefresh();
            body.empty();
            homeLines = [];
            homeHeroCard = null;
            homeMode = false;
            homeActiveLine = -1;
            searchLines = [];
            searchSources = [];
            searchSourceTabs = [];
            searchActiveSourceId = '';
            searchSourceTabsWrap.empty().detach();
            searchMode = false;
            searchActiveLine = -1;
            destroyHomeItems();
            this.activity.loader(true);
        };

        function openHomeLine(index) {
            if (!homeMode) return false;
            if (index < 0 || index >= homeLines.length || !homeLines[index]) return false;

            homeActiveLine = index;
            scroll.update($(homeLines[index].render(true)), true);

            requestAnimationFrame(function () {
                if (homeLines[index]) homeLines[index].toggle();
            });

            return true;
        }

        function detectHomeLineIndex(element) {
            if (!element) return -1;

            var lineElement = $(element).closest('.lm-home-line')[0];
            if (!lineElement) return -1;

            for (var i = 0; i < homeLines.length; i++) {
                if (homeLines[i] && homeLines[i].render && homeLines[i].render(true) === lineElement)
                    return i;
            }

            return -1;
        }

        function focusHomeHero() {
            homeActiveLine = -1;
            last = searchBtn[0];
            headerFocusTarget = searchBtn[0];
            if (last) scroll.update($(last), true);
            setBackground(artwork('album', 'Музыка', 'Главная', ['#15202c', '#35556c']));
            Lampa.Controller.toggle('content');
        }

        function focusSearchHeader(target) {
            if (homeMode) homeActiveLine = -1;
            searchActiveLine = -1;
            last = target || headerFocusTarget || searchBtn[0];
            headerFocusTarget = last || searchBtn[0];

            if (last) scroll.update($(last), true);

            if (last === filterBtn[0])
                setBackground(artwork('track', 'Фильтр', getPlaybackModeTitle() + ' · ' + getQualityModeTitle(), ['#1f2734', '#3f5f7d']));
            else if (last === bookmarksBtn[0])
                setBackground(artwork('album', 'Закладки', 'Артисты · Альбомы · Треки', ['#1d2632', '#486077']));
            else if (last === homeBtn[0])
                setBackground(artwork('album', 'Музыка', 'Главная', ['#15202c', '#35556c']));
            else if (last === authBtn[0])
                setBackground(artwork('album', 'Yandex Music', 'Авторизация', ['#2b173b', '#8a2be2']));
            else
                setBackground(artwork('album', currentQuery || 'Музыка', 'Поиск музыки', ['#172434', '#375d7f']));

            Lampa.Controller.toggle('content');
        }

        function isHomeHeaderTarget(target) {
            return !!target && (
                target === searchBtn[0] ||
                target === filterBtn[0] ||
                target === bookmarksBtn[0] ||
                target === homeBtn[0] ||
                (MUSIC.features.auth && target === authBtn[0])
            );
        }

        function openSearchLine(index) {
            if (!searchMode) return false;
            if (index < 0 || index >= searchLines.length || !searchLines[index]) return false;

            searchActiveLine = index;
            setSearchFocusZone('results');
            var line = searchLines[index];
            scroll.update($(line.render(true)), true);

            requestAnimationFrame(function () {
                if (!searchLines[index]) return;
                line.toggle();

                requestAnimationFrame(function () {
                    if (!searchLines[index]) return;

                    var lineElement = line.render(true);
                    var target = findSearchLineFocusTarget(lineElement);
                    if (!target) return;

                    last = target;
                    Lampa.Controller.collectionSet(lineElement);
                    Lampa.Controller.collectionFocus(target, lineElement);
                    scroll.update($(target), true);
                });
            });

            return true;
        }

        function detectSearchLineIndex(element) {
            if (!element) return -1;

            var lineElement = $(element).closest('.lm-search-line')[0];
            if (!lineElement) return -1;

            for (var i = 0; i < searchLines.length; i++) {
                if (searchLines[i] && searchLines[i].render && searchLines[i].render(true) === lineElement)
                    return i;
            }

            return -1;
        }

        function detectSearchSourceTabIndex(element) {
            if (!element) return -1;

            var tabElement = $(element).closest('.lm-search-source-tab')[0];
            if (!tabElement) return -1;

            return searchSourceTabs.indexOf(tabElement);
        }

        function ensureFocusedSearchSourceActive() {
            var tabIndex = detectSearchSourceTabIndex(last);
            if (tabIndex < 0 || tabIndex >= searchSources.length) return false;

            var source = searchSources[tabIndex];
            if (!source || source.id === searchActiveSourceId) return false;

            activateSearchSource(source.id, false);
            return true;
        }

        function focusSearchHistoryItem(index) {
            var items = searchHistoryWrap.find('.selector');
            if (!items.length) return false;

            var target = items.eq(Math.max(0, Math.min(index, items.length - 1)));
            if (!target.length) return false;

            searchActiveLine = -1;
            setSearchFocusZone('history');
            searchHistoryTargetIndex = items.index(target[0]);
            Lampa.Controller.toggle('music_search_history');
            return true;
        }

        function getSearchSourceIndexById(sourceId) {
            for (var i = 0; i < searchSources.length; i++) {
                if (searchSources[i] && searchSources[i].id === sourceId)
                    return i;
            }

            return -1;
        }

        function getActiveSearchSource() {
            var index = getSearchSourceIndexById(searchActiveSourceId);
            if (index >= 0) return searchSources[index];
            return searchSources.length ? searchSources[0] : null;
        }

        function getFirstSearchSourceWithResults() {
            for (var i = 0; i < searchSources.length; i++) {
                if (searchSources[i] && searchSources[i].count > 0)
                    return searchSources[i];
            }

            return searchSources.length ? searchSources[0] : null;
        }

        function hasPendingSearchSource() {
            return searchSources.some(function (source) {
                return !!(source && source.pending);
            });
        }

        function clearSearchMetadataRefresh() {
            if (!searchMetadataRefreshTimer) return;
            clearTimeout(searchMetadataRefreshTimer);
            searchMetadataRefreshTimer = null;
        }

        function scheduleSearchMetadataRefresh(query, requestToken, attempt) {
            clearSearchMetadataRefresh();

            if (!query || attempt > 2) return;

            searchMetadataRefreshTimer = setTimeout(function () {
                searchMetadataRefreshTimer = null;

                if (destroyed || requestToken !== searchRequestToken || currentQuery !== query) return;

                request(MUSIC.endpoints.search + '?q=' + encodeURIComponent(query), function (json) {
                    if (destroyed || requestToken !== searchRequestToken || currentQuery !== query) return;

                    var groups = normalizeSearchResults(json);
                    var sources = buildSearchSources(groups, query);
                    var wasCatalogActive = searchActiveSourceId === 'catalog';
                    var previousSourceId = searchActiveSourceId;

                    searchSources = sources;
                    if (!searchActiveSourceId || getSearchSourceIndexById(searchActiveSourceId) < 0)
                        searchActiveSourceId = previousSourceId && getSearchSourceIndexById(previousSourceId) >= 0
                            ? previousSourceId
                            : (getFirstSearchSourceWithResults() || {}).id || '';

                    if (wasCatalogActive)
                        renderSearchLinesForActiveSource();
                    else {
                        renderSearchSourceTabs();
                        if (searchFocusZone === 'sources') {
                            var activeTabIndex = getSearchSourceIndexById(searchActiveSourceId);
                            if (activeTabIndex >= 0 && searchSourceTabs[activeTabIndex]) {
                                last = searchSourceTabs[activeTabIndex];
                                scroll.update($(last), true);
                            }
                        }
                    }

                    if (hasPendingSearchMetadata(groups))
                        scheduleSearchMetadataRefresh(query, requestToken, attempt + 1);
                }, function () {});
            }, attempt ? 5000 : 3000);
        }

        function focusSearchSourceTab(index) {
            if (!searchMode) return false;
            if (index < 0 || index >= searchSourceTabs.length || !searchSourceTabs[index]) return false;

            searchActiveLine = -1;
            setSearchFocusZone('sources');
            searchSourceTargetIndex = index;
            Lampa.Controller.toggle('music_search_sources');
            return true;
        }

        function renderSearchSourceTabs() {
            searchSourceTabs = [];
            searchSourceTabsWrap.empty();

            if (searchScreenMode && !searchSourceTabsWrap.parent().length)
                head.append(searchSourceTabsWrap);

            searchSources.forEach(function (source, index) {
                var tab = $('<div class="selector lm-search-source-tab"><span class="lm-search-source-tab__title"></span><span class="lm-search-source-tab__count"></span></div>');
                tab.find('.lm-search-source-tab__title').text(source.title || 'Источник');
                tab.find('.lm-search-source-tab__count')
                    .text(source.pending ? '...' : (source.count || 0))
                    .toggleClass('hide', !source.pending && !(source.count > 0));
                tab.toggleClass('active', source.id === searchActiveSourceId);
                tab.toggleClass('lm-search-source-tab--pending', !!source.pending);

                tab.on('hover:focus', function () {
                    setSearchFocusZone('sources');
                    searchSourceTargetIndex = index;
                    last = tab[0];
                    scroll.update(tab, true);
                    setBackground(artwork('album', source.title || 'Источник', currentQuery || 'Поиск', ['#1a2432', '#4b6785']));
                });

                tab.on('hover:enter hover:long', function () {
                    if (searchActiveSourceId !== source.id) {
                        activateSearchSource(source.id, true);
                        return;
                    }

                    focusSearchSourceTab(index);
                });

                searchSourceTabs.push(tab[0]);
                searchSourceTabsWrap.append(tab);
            });

            searchSourceTabsWrap.toggleClass('hide', !searchSourceTabs.length);
        }

        function renderSearchLinesForActiveSource() {
            var source = getActiveSearchSource();

            Lampa.Arrays.destroy(items);
            items = [];
            searchLines = [];
            body.empty();

            renderSearchSourceTabs();

            if (!source || !Array.isArray(source.groups) || !source.groups.length) {
                if (currentQuery) {
                    body.append($('<div class="lm-search-empty"></div>').text(source && source.pending ? 'MusicBrainz загружается...' : 'В выбранном источнике ничего не найдено.'));
                }
                return;
            }

            source.groups.forEach(function (group) {
                _this.appendSearchGroup(group.id, group.title, group.entries, group.has_more);
            });
        }

        function activateSearchSource(sourceId, focusTab) {
            searchActiveSourceId = sourceId;
            renderSearchLinesForActiveSource();
            searchActiveLine = -1;

            requestAnimationFrame(function () {
                if (destroyed || !searchMode) return;

                if (focusTab && searchSourceTabs.length) {
                    focusSearchSourceTab(Math.max(0, getSearchSourceIndexById(searchActiveSourceId)));
                    return;
                }

                if (searchLines.length) {
                    openSearchLine(0);
                    return;
                }

                focusSearchHeader(headerFocusTarget);
            });
        }

        function openSearchActionsMenu() {
            var recentQueries = getRecentQueries().slice(0, RECENT_QUERY_QUICK_LIMIT);
            var items = [
                { title: 'Открыть поиск', action: 'search' },
                { title: 'Открыть главную', action: 'home' }
            ];

            if (recentQueries.length) {
                items.push({
                    title: 'Недавние поиски',
                    separator: true
                });

                items.push({
                    title: 'Очистить недавние поиски',
                    action: 'clear_recent_queries'
                });

                recentQueries.forEach(function (query) {
                    items.push({
                        title: query.query,
                        action: 'query',
                        query: query.query
                    });
                });
            }

            showContextMenu(translate('title_action', 'Действия'), items, function (selected, restoreContext) {
                if (!selected || !selected.action) return;

                if (selected.action === 'search') {
                    if (homeMode) {
                        openSearchScreen('');
                    } else {
                        openSearchInput(currentQuery, {
                            ignoreSameValue: true,
                            onCancel: function () {
                                restoreAfterSearchInput(searchBtn[0]);
                            }
                        });
                    }
                    return;
                }

                if (selected.action === 'home') {
                    Lampa.Activity.push({
                        title: MUSIC.title,
                        component: 'lampac_music_home',
                        page: 1,
                        noinfo: true
                    });
                    return;
                }

                if (selected.action === 'clear_recent_queries') {
                    clearRecentQueries();
                    Lampa.Noty.show('Недавние поиски очищены.');
                    restoreMusicFocusContext(restoreContext, 120);
                    return;
                }

                if (selected.action === 'query')
                    openSearchQuery(selected.query);
            });
        }

        function openFilterActionsMenu() {
            openFilterMenu(filterBtn);
        }

        function buildHomeActionItems() {
            var sectionKeys = Object.keys(MUSIC_HOME_CACHE || {});
            var items = [{
                title: 'Открыть главную',
                action: 'home'
            }];

            if (sectionKeys.length) {
                items.push({
                    title: 'Разделы',
                    separator: true
                });

                sectionKeys.forEach(function (sectionKey) {
                    items.push({
                        title: MUSIC_HOME_SECTION_TITLES[sectionKey] || 'Секция',
                        action: 'section',
                        sectionKey: sectionKey
                    });
                });
            }

            return items;
        }

        function openHomeActionsMenu() {
            showContextMenu(translate('title_action', 'Действия'), buildHomeActionItems(), function (selected, restoreContext) {
                if (!selected || !selected.action) return;

                if (selected.action === 'home') {
                    Lampa.Activity.push({
                        title: MUSIC.title,
                        component: 'lampac_music_home',
                        page: 1,
                        noinfo: true
                    });
                    return;
                }

                if (selected.action === 'section')
                    openHomeSection(selected.sectionKey, MUSIC_HOME_SECTION_TITLES[selected.sectionKey] || 'Секция');
                else
                    restoreMusicFocusContext(restoreContext);
            });
        }

        this.onControllerToggle = function () {
            if (homeMode && homeLines.length) {
                var homeIndex = homeActiveLine >= 0 ? homeActiveLine : detectHomeLineIndex(last);
                if (homeIndex >= 0)
                    return openHomeLine(homeIndex);
            }

            if (searchMode && searchLines.length) {
                var searchIndex = searchActiveLine >= 0 ? searchActiveLine : detectSearchLineIndex(last);
                if (searchIndex >= 0)
                    return openSearchLine(searchIndex);
            }

            return false;
        };

        this.onControllerDown = function () {
            if (homeMode && homeActiveLine < 0 && homeLines.length && isHomeHeaderTarget(last))
                return openHomeLine(0);

            if (searchMode) {
                if (searchScreenMode) {
                    if (searchFocusZone === 'buttons')
                        return focusSearchInput();

                    if (searchFocusZone === 'input') {
                        if (searchHistoryWrap.find('.selector').length)
                            return focusSearchHistoryItem(0);

                        if (searchSourceTabs.length)
                            return focusSearchSourceTab(Math.max(0, getSearchSourceIndexById(searchActiveSourceId)));

                        if (searchLines.length)
                            return openSearchLine(0);
                    }

                    if (searchFocusZone === 'history') {
                        if (searchSourceTabs.length)
                            return focusSearchSourceTab(Math.max(0, getSearchSourceIndexById(searchActiveSourceId)));

                        if (searchLines.length)
                            return openSearchLine(0);
                    }

                    if (searchFocusZone === 'sources') {
                        if (ensureFocusedSearchSourceActive())
                            return true;

                        if (searchLines.length)
                            return openSearchLine(0);
                    }
                }

                var tabIndex = detectSearchSourceTabIndex(last);
                var historyIndex = searchHistoryWrap.find('.selector').index(last);

                if (searchScreenMode && isSearchScreenButton(last)) {
                    return focusSearchInput();
                }

                if (tabIndex >= 0) {
                    if (ensureFocusedSearchSourceActive())
                        return true;

                    if (searchLines.length)
                        return openSearchLine(0);
                }

                if (searchScreenMode && historyIndex >= 0 && searchSourceTabs.length)
                    return focusSearchSourceTab(Math.max(0, getSearchSourceIndexById(searchActiveSourceId)));

                if (searchScreenMode && searchActiveLine < 0 && last === searchBtn[0]) {
                    if (searchHistoryWrap.find('.selector').length)
                        return focusSearchHistoryItem(0);

                    if (searchSourceTabs.length)
                        return focusSearchSourceTab(Math.max(0, getSearchSourceIndexById(searchActiveSourceId)));
                }

                if (searchActiveLine < 0 && searchSourceTabs.length && isHomeHeaderTarget(last))
                    return focusSearchSourceTab(Math.max(0, getSearchSourceIndexById(searchActiveSourceId)));

                if (searchActiveLine < 0 && searchLines.length)
                    return openSearchLine(0);
            }

            return false;
        };

        this.onControllerUp = function () {
            if (homeMode && homeActiveLine < 0 && isHomeHeaderTarget(last)) {
                headerFocusTarget = last;
                Lampa.Controller.toggle('head');
                return true;
            }

            if (!searchMode || !searchScreenMode || searchActiveLine >= 0)
                return false;

            if (searchFocusZone === 'input')
                return focusSearchScreenButton();

            if (searchFocusZone === 'history')
                return focusSearchInput();

            if (searchFocusZone === 'sources') {
                if (searchHistoryWrap.find('.selector').length)
                    return focusSearchHistoryItem(0);

                return focusSearchInput() || focusSearchScreenButton();
            }

            var tabIndex = detectSearchSourceTabIndex(last);
            var historyItems = searchHistoryWrap.find('.selector');
            var historyIndex = historyItems.index(last);

            if (last === searchBtn[0])
                return focusSearchScreenButton();

            if (tabIndex >= 0) {
                if (historyItems.length)
                    return focusSearchHistoryItem(0);

                return focusSearchScreenButton() || focusSearchInput();
            }

            if (historyIndex >= 0) {
                return focusSearchInput();
            }

            return false;
        };

        this.loadHome = function (restoreState) {
            status.text('');
            destroyHomeItems();
            body.empty();
            homeLines = [];
            homeHeroCard = null;
            homeMode = true;
            homeActiveLine = -1;
            _this.activity.loader(true);

            function appendHomeSection(title, sectionKey, entries) {
                if (!entries.length) return;

                scroll.minus();
                var visibleEntries = cloneHomeEntries(entries.slice(0, HOME_SECTION_LIMIT));
                var canOpenMore = sectionKey.indexOf('browse:') === 0
                    ? !!(MUSIC_HOME_SECTION_META[sectionKey] && MUSIC_HOME_SECTION_META[sectionKey].has_more)
                    : entries.length > HOME_SECTION_LIMIT;
                var line = createMusicLine({
                    title: title,
                    entries: visibleEntries,
                    hasMore: canOpenMore,
                    onSelect: function (entry, index) {
                        activateEntry(index >= 0 ? entries[index] : entry, entries, index);
                    },
                    onMenu: function (entry, index) {
                        activateEntryMenu(index >= 0 ? entries[index] : entry, entries, index, function (nextRestoreState) {
                            _this.loadHome(nextRestoreState || {
                                sectionKey: sectionKey,
                                entryIndex: Math.max(0, index)
                            });
                        });
                    },
                    onFocus: function (entry) {
                        setBackground((entry && entry.background) || IMG_BG);
                        requestAnimationFrame(function () {
                            var current = line.render(true).querySelector('.focus');
                            if (current) last = current;
                        });
                    },
                    onFocusMore: function () {
                        setBackground(artwork('album', title, 'Ещё', ['#1d2837', '#496786']));
                        requestAnimationFrame(function () {
                            var current = line.render(true).querySelector('.focus');
                            if (current) last = current;
                        });
                    },
                    onMore: function () {
                        openHomeSection(sectionKey, title);
                    }
                });
                line.section_key = sectionKey;
                $(line.render(true)).addClass('lm-home-line');

                var lineIndex = homeLines.length;

                line.use({
                    onDown: function () {
                    if (lineIndex < homeLines.length - 1)
                        openHomeLine(lineIndex + 1);
                    },
                    onUp: function () {
                        if (lineIndex > 0) {
                            openHomeLine(lineIndex - 1);
                            return;
                        }

                        focusHomeHero();
                    },
                    onBack: _this.back
                });

                body.append(line.render(true));
                homeLines.push(line);
                items.push(line);
            }

            function renderHome(home) {
                body.empty();

                var sections = snapshotHomeSections(home);
                homeHeroCard = null;

                loadStatsTopSummary(function (stats) {
                    if (destroyed) return;

                    sections.user_playlists = applySectionKey(buildUserPlaylistCardsWithStats(
                        home && Array.isArray(home.user_playlists) ? home.user_playlists : [],
                        stats
                    ), 'user_playlists');
                    MUSIC_HOME_CACHE.user_playlists = sections.user_playlists;
                    MUSIC_HOME_SECTION_META.user_playlists = { has_more: (sections.user_playlists || []).length > HOME_SECTION_LIMIT };

                    appendHomeSection('Недавно слушали', 'recently_played', sections.recently_played || []);
                    appendHomeSection('Недавние альбомы', 'recent_albums', sections.recent_albums || []);
                    appendHomeSection('Недавние артисты', 'recent_artists', sections.recent_artists || []);
                    appendHomeSection('Недавние поиски', 'recent_queries', sections.recent_queries || []);
                    appendHomeSection('Мои плейлисты', 'user_playlists', sections.user_playlists || []);

                    if (home && Array.isArray(home.browse_sections)) {
                        home.browse_sections.forEach(function (section) {
                            if (!section || !section.id) return;
                            appendHomeSection(section.title || 'Секция', section.id, sections[section.id] || []);
                        });
                    }

                    _this.activity.loader(false);
                    _this.activity.toggle();
                    restoreHomeAfterRender(restoreState);
                });
            }

            request(MUSIC.endpoints.home, function (json) {
                if (destroyed) return;
                renderHome(parseJson(json) || null);
            }, function () {
                if (destroyed) return;
                renderHome(null);
            });
        };

        this.loadSearchLanding = function (keepInputFocus) {
            searchRequestToken++;
            clearSearchMetadataRefresh();
            homeMode = false;
            homeActiveLine = -1;
            searchMode = true;
            searchActiveLine = -1;
            searchLines = [];
            status.text('Введите запрос или выберите недавний.');

            _this.clean();
            searchMode = true;
            body.empty();
            searchSourceTabsWrap.empty().detach();
            searchSources = buildSearchSources({
                artists: [],
                albums: [],
                tracks: [],
                sections: []
            }, currentQuery);
            searchSourceTabs = [];
            searchActiveSourceId = searchSources.length ? searchSources[0].id : '';
            updateSearchButtonDisplay();
            renderSearchHistory();
            renderSearchLinesForActiveSource();

            searchActiveLine = -1;
            _this.activity.loader(false);
            _this.activity.toggle();

            if (keepInputFocus && searchScreenMode && searchInputField && searchInputField.length) {
                setSearchFocusZone('input');
                last = searchBtn[0];
                scroll.update(searchBtn, true);
                requestAnimationFrame(function () {
                    if (destroyed || !searchInputField[0]) return;
                    searchInputField[0].focus();
                });
                return;
            }

            last = headerFocusTarget || searchBtn[0];
            if (last) scroll.update($(last), true);
        };

        this.loadSearch = function () {
            var requestToken = ++searchRequestToken;
            var keepInputFocus = !!(searchPreserveInputFocus && searchScreenMode && searchInputField && searchInputField.length);
            var caretPosition = keepInputFocus && document.activeElement === searchInputField[0] && typeof searchInputField[0].selectionStart === 'number'
                ? searchInputField[0].selectionStart
                : null;
            searchPreserveInputFocus = false;
            homeMode = false;
            homeActiveLine = -1;
            searchMode = true;
            searchActiveLine = -1;
            searchLines = [];
            status.text('');
            clearSearchMetadataRefresh();

            request(MUSIC.endpoints.search + '?q=' + encodeURIComponent(currentQuery), function (json) {
                if (destroyed || requestToken !== searchRequestToken) return;

                var groups = normalizeSearchResults(json);
                var sources = buildSearchSources(groups, currentQuery);
                updateSearchButtonDisplay();
                renderSearchHistory();
                if (groups.artists.length && groups.artists[0] && groups.artists[0].raw) {
                    updateRecentQueryArtist(currentQuery, groups.artists[0].raw);
                    requestArtistImage(groups.artists[0].raw, function (images) {
                        if (!images || !images.length) return;

                        var artist = groups.artists[0].raw;
                        artist.images = images;
                        updateRecentQueryArtist(currentQuery, artist, false);
                    });
                }
                var total = sources.reduce(function (sum, source) {
                    return sum + ((source && typeof source.count === 'number') ? source.count : 0);
                }, 0);

                _this.clean();

                searchMode = true;
                searchActiveLine = -1;
                searchLines = [];
                searchSources = sources;

                if (!total && !hasPendingSearchSource()) {
                    _this.empty('По запросу ничего не найдено.');
                    _this.activity.loader(false);
                    return;
                }

                if (!searchActiveSourceId || getSearchSourceIndexById(searchActiveSourceId) < 0)
                    searchActiveSourceId = (getFirstSearchSourceWithResults() || {}).id || '';

                renderSearchLinesForActiveSource();

                if (hasPendingSearchMetadata(groups))
                    scheduleSearchMetadataRefresh(currentQuery, requestToken, 0);

                warmExpandedSearch(currentQuery);

                searchActiveLine = -1;
                _this.activity.loader(false);
                _this.activity.toggle();

                if (keepInputFocus && searchInputField && searchInputField.length) {
                    setSearchFocusZone('input');
                    last = searchBtn[0];
                    scroll.update(searchBtn, true);
                    requestAnimationFrame(function () {
                        if (destroyed || !searchInputField[0]) return;
                        searchInputField[0].focus();
                        if (caretPosition !== null) {
                            try {
                                searchInputField[0].setSelectionRange(caretPosition, caretPosition);
                            } catch (e) {}
                        }
                    });
                    return;
                }

                last = searchSourceTabs[getSearchSourceIndexById(searchActiveSourceId)]
                    || headerFocusTarget
                    || searchBtn[0];
                if (last) scroll.update($(last), true);
            }, function () {
                if (destroyed || requestToken !== searchRequestToken) return;
                updateSearchButtonDisplay();
                renderSearchHistory();
                _this.clean();
                _this.empty('Не удалось загрузить результаты поиска.');
                _this.activity.loader(false);
            });
        };

        this.appendSearchGroup = function (groupType, title, group, hasMoreOverride) {
            if (!group.length) return;

            scroll.minus();
            var visibleEntries = cloneHomeEntries(group.slice(0, HOME_SECTION_LIMIT));
            var line = createMusicLine({
                title: title,
                entries: visibleEntries,
                hasMore: typeof hasMoreOverride === 'boolean' ? hasMoreOverride : group.length > HOME_SECTION_LIMIT,
                onSelect: function (entry, index) {
                    activateEntry(index >= 0 ? group[index] : entry, group, index);
                },
                onMenu: function (entry, index) {
                    activateEntryMenu(index >= 0 ? group[index] : entry, group, index);
                },
                onFocus: function (entry) {
                    setSearchFocusZone('results');
                    setBackground((entry && entry.background) || IMG_BG);
                    requestAnimationFrame(function () {
                        var current = line.render(true).querySelector('.focus');
                        if (current) last = current;
                    });
                },
                onFocusMore: function () {
                    setSearchFocusZone('results');
                    setBackground(artwork('album', title, 'Ещё', ['#1d2632', '#486077']));
                    requestAnimationFrame(function () {
                        var current = line.render(true).querySelector('.focus');
                        if (current) last = current;
                    });
                },
                onMore: function () {
                    openSearchSection(title, group, {
                        query: currentQuery,
                        type: groupType
                    });
                }
            });
            $(line.render(true)).addClass('lm-home-line lm-search-line');

            var lineIndex = searchLines.length;

            line.use({
                onDown: function () {
                    if (lineIndex < searchLines.length - 1)
                        openSearchLine(lineIndex + 1);
                },
                onUp: function () {
                    if (lineIndex > 0) {
                        openSearchLine(lineIndex - 1);
                        return;
                    }

                    if (searchSourceTabs.length) {
                        focusSearchSourceTab(Math.max(0, getSearchSourceIndexById(searchActiveSourceId)));
                        return;
                    }

                    focusSearchHeader(headerFocusTarget);
                },
                onBack: _this.back
            });

            body.append(line.render(true));
            searchLines.push(line);
            items.push(line);
        };

        this.empty = function (message) {
            var empty = new Lampa.Empty();
            body.append(empty.render());
            if (message) Lampa.Noty.show(message);
        };

        this.render = function () {
            return html;
        };

        this.destroy = function () {
            destroyed = true;
            clearSearchMetadataRefresh();
            unbindRecentListener();
            Lampa.Arrays.destroy(items);
            scroll.destroy();
            html.remove();
        };
    }

    // ===== FULL SCREEN COMPONENTS =====

    function MusicSectionFull(object) {
        var scroll = new Lampa.Scroll({ mask: false, over: true });
        var html = $('<div></div>');
        var body = $('<div class="category-full lm-full"></div>');
        var last;
        var destroyed = false;
        var items = [];
        var sectionKey = object.section_key;
        var sectionTitle = object.section_title || object.title || 'Секция';
        var sectionDirty = false;
        var unbindRecentListener = function () {};
        var _this = this;

        bindBase(this, scroll, function () { return last; });

        var baseStart = this.start;
        this.start = function () {
            baseStart();

            if (sectionDirty) {
                sectionDirty = false;
                renderSection();
            }
        };

        function handleRecentChanged(e) {
            var detail = e && e.detail ? e.detail : {};
            if (!detail.section_key || detail.section_key !== sectionKey) return;
            if (detail.payload && detail.payload.skipActiveSectionRefresh && Lampa.Activity.active().activity === _this.activity) return;

            sectionDirty = true;

            if (isLampaPlayerOverlayOpen()) {
                traceEmbeddedIos('section-refresh-deferred', detail.section_key || '', true);
                return;
            }

            if (Lampa.Activity.active().activity === _this.activity) {
                sectionDirty = false;
                renderSection();
            }
        }

        function clearRenderedItems() {
            Lampa.Arrays.destroy(items);
            items = [];
            body.empty();
        }

        function renderSection() {
            _this.activity.loader(true);
            clearRenderedItems();

            resolveHomeSectionEntries(sectionKey, function (entries) {
                if (destroyed) return;

                var isStatsTop = sectionKey === 'stats:top';
                var isPlaylist = String(sectionKey || '').indexOf('playlist:') === 0 || isStatsTop;
                var wrapper = $('<div class="lm-full__wrapper"></div>');
                var header = $('<div class="lm-full__header selector"></div>');
                var info = $('<div class="lm-full__info"></div>');
                var title = $('<div class="lm-full__title"></div>').text(sectionTitle);

                // плейлист и «Твой топ» — hero-шапка и трек-лист,
                // остальные секции — сетка карточек
                if (isPlaylist) {
                    var rawTracks = entries.map(function (entry) { return entry && entry.raw ? entry.raw : null; }).filter(Boolean);
                    var heroImg = entries.length && entries[0].image
                        ? entries[0].image
                        : artwork('album', sectionTitle, 'PLAYLIST', ['#232838', '#4a5a82']);
                    var totalMs = rawTracks.reduce(function (sum, item) { return sum + (item.duration_ms || 0); }, 0);

                    var poster = $('<div class="lm-full__poster"></div>');
                    var posterImg = $('<img class="lm-full__poster-img" src="" alt="" />');
                    var metaElem = $('<div class="lm-full__meta"></div>').text(isStatsTop
                        ? buildStatsTopMetaLabel(MUSIC_STATS_TOP.summary, rawTracks.length)
                        : [formatTrackCountLabel(rawTracks.length), formatTotalDurationLabel(totalMs)].filter(Boolean).join(' · ')
                    );
                    var actions = $('<div class="lm-full__actions"></div>');
                    var playBtn = $('<div class="selector lm-small-btn lm-small-btn--primary">▶ Слушать</div>');
                    var shuffleBtn = $('<div class="selector lm-small-btn">Перемешать</div>');
                    var tracksBox = $('<div class="lm-full__tracks"></div>');

                    posterImg.on('load error', function () { posterImg.css('opacity', 1); });
                    posterImg.attr('src', heroImg);
                    poster.append(posterImg);

                    info.append(title);
                    info.append(metaElem);

                    if (rawTracks.length) {
                        actions.append(playBtn);
                        actions.append(shuffleBtn);
                        info.append(actions);
                    }

                    header.append(poster);
                    header.append(info);
                    wrapper.append(header);
                    wrapper.append(tracksBox);
                    body.append(wrapper);
                    scroll.append(body);
                    html.append(scroll.render());

                    computeStandaloneIosArtTint(heroImg, function (tint) {
                        if (destroyed || !tint) return;

                        try {
                            wrapper.get(0).style.setProperty('--lm-hero-tint', tint);
                        } catch (e) {}
                    });

                    last = header[0];

                    header.on('hover:focus', function () {
                        last = this;
                        scroll.update($(this), true);
                        setBackground(heroImg);
                    });

                    playBtn.on('hover:focus', function () {
                        last = playBtn[0];
                        scroll.update(playBtn, true);
                        setBackground(heroImg);
                    });

                    playBtn.on('hover:enter', function () {
                        if (rawTracks.length) playTrack(rawTracks[0], rawTracks, 0);
                    });

                    shuffleBtn.on('hover:focus', function () {
                        last = shuffleBtn[0];
                        scroll.update(shuffleBtn, true);
                        setBackground(heroImg);
                    });

                    shuffleBtn.on('hover:enter', function () {
                        playTracksShuffled(rawTracks);
                    });

                    if (!entries.length) {
                        tracksBox.append($('<div class="lm-full__empty"></div>')
                            .text(isStatsTop
                                ? 'Статистика ещё копится — слушай музыку, топ соберётся сам.'
                                : 'Плейлист пуст. Долгое нажатие на любом треке → «В плейлист…»'));
                    }

                    entries.forEach(function (entry, index) {
                        var row = buildTrackRow(entry.raw, index, { thumb: entry.image, positionNumber: true });

                        row.on('hover:focus', function () {
                            last = row[0];
                            scroll.update(row, true);
                            setBackground(entry.background || heroImg);
                        });

                        row.on('hover:enter', function () {
                            playTrack(entry.raw, rawTracks, index);
                        });

                        row.on('hover:long', function () {
                            activateEntryMenu(entry, entries, index, renderSection);
                        });

                        tracksBox.append(row);
                    });

                    _this.activity.loader(false);
                    _this.activity.toggle();
                    return;
                }

                var statusElem = $('<div class="lm-full__status"></div>').text('Карточек: ' + entries.length);
                var grid = $('<div class="lm-grid"></div>');

                info.append(title);
                info.append(statusElem);
                header.append(info);
                wrapper.append(header);
                wrapper.append(grid);
                body.append(wrapper);
                scroll.append(body);
                html.append(scroll.render());

                last = header[0];

                header.on('hover:focus', function () {
                    last = this;
                    scroll.update($(this), true);
                    setBackground(artwork('album', sectionTitle, 'Секция', ['#15202c', '#35556c']));
                });

                entries.forEach(function (entry, index) {
                    var cardObj = new MusicCardItem(entry);
                    var render = cardObj.render();

                    render.on('hover:focus', function () {
                        last = render[0];
                        scroll.update(render, true);
                        setBackground(entry.background || IMG_BG);
                    });

                    render.on('hover:enter', function () {
                        activateEntry(entry, entries, index);
                    });

                    render.on('hover:long', function () {
                        activateEntryMenu(entry, entries, index, renderSection);
                    });

                    grid.append(render);
                    items.push(cardObj);
                });

                _this.activity.loader(false);
                _this.activity.toggle();
            });
        }

        this.create = function () {
            scroll.minus();
            unbindRecentListener = bindRecentListener(handleRecentChanged);
            renderSection();

            return this.render();
        };

        this.render = function () { return html; };
        this.destroy = function () {
            destroyed = true;
            unbindRecentListener();
            Lampa.Arrays.destroy(items);
            scroll.destroy();
            html.remove();
        };
    }

    function MusicEntriesFull(object) {
        var scroll = new Lampa.Scroll({ mask: false, over: true });
        var html = $('<div></div>');
        var body = $('<div class="category-full lm-full"></div>');
        var last;
        var items = [];
        var destroyed = false;
        var entriesTitle = object.entries_title || object.title || 'Результаты';
        var entries = Array.isArray(object.entries) ? object.entries.slice() : [];
        var pagination = object.entries_pagination || null;
        var nextPage = pagination && pagination.next_page ? pagination.next_page : '';
        var waitLoad = false;
        var statusElem = null;
        var grid = null;
        var _this = this;

        bindBase(this, scroll, function () { return last; });

        function statusText(loading) {
            if (!entries.length) return 'Пока пусто';

            var text = 'Карточек: ' + entries.length;
            if (loading) text += ' · загрузка...';
            else if (nextPage) text += ' · ещё есть';

            return text;
        }

        function updateStatus(loading) {
            if (statusElem)
                statusElem.text(statusText(loading));
        }

        function buildPaginationUrl() {
            if (!pagination || pagination.kind !== 'artist_section' || !pagination.section_id || !nextPage)
                return '';

            var url = MUSIC.endpoints.artistSection
                + '?id=' + encodeURIComponent(pagination.section_id)
                + '&limit=' + encodeURIComponent(pagination.limit || HOME_SECTION_LIMIT);

            if (pagination.provider)
                url += '&provider=' + encodeURIComponent(pagination.provider);

            url += '&page=' + encodeURIComponent(nextPage);

            return url;
        }

        function appendEntry(entry, index, appendToController) {
            var cardObj = new MusicCardItem(entry);
            var render = cardObj.render();

            render.on('hover:focus', function () {
                last = render[0];
                scroll.update(render, true);
                setBackground(entry.background || IMG_BG);
            });

            render.on('hover:enter', function () {
                activateEntry(entry, entries, index);
            });

            render.on('hover:long', function () {
                activateEntryMenu(entry, entries, index);
            });

            grid.append(render);
            items.push(cardObj);
            items.push({
                destroy: function () {
                    render.off();
                }
            });

            if (appendToController && Lampa.Controller && typeof Lampa.Controller.collectionAppend === 'function')
                Lampa.Controller.collectionAppend(render[0]);
        }

        function appendEntries(nextEntries, appendToController) {
            (Array.isArray(nextEntries) ? nextEntries : []).forEach(function (entry) {
                entries.push(entry);
                appendEntry(entry, entries.length - 1, appendToController);
            });
        }

        function loadNextPage() {
            if (!nextPage || waitLoad) return;

            var url = buildPaginationUrl();
            if (!url) return;

            waitLoad = true;
            updateStatus(true);

            request(url, function (response) {
                if (destroyed || waitLoad === false) return;

                var nextSection = mapSearchSection(parseJson(response) || response);
                var nextEntries = nextSection && Array.isArray(nextSection.entries) ? nextSection.entries : [];

                nextPage = nextSection && nextSection.next_page ? nextSection.next_page : '';
                if (pagination) pagination.next_page = nextPage;

                if (nextEntries.length)
                    appendEntries(nextEntries, true);
                else if (!nextPage)
                    Lampa.Noty.show('Больше нет данных.');

                waitLoad = false;
                updateStatus(false);

                if (last) scroll.update($(last), true);
            }, function () {
                if (destroyed) return;

                waitLoad = false;
                updateStatus(false);
                Lampa.Noty.show('Не удалось догрузить раздел.');
            });
        }

        this.create = function () {
            this.activity.loader(true);
            scroll.minus();
            scroll.onEnd = loadNextPage;

            var wrapper = $('<div class="lm-full__wrapper"></div>');
            var header = $('<div class="lm-full__header selector"></div>');
            var info = $('<div class="lm-full__info"></div>');
            var title = $('<div class="lm-full__title"></div>').text(entriesTitle);
            statusElem = $('<div class="lm-full__status"></div>').text(statusText(false));
            grid = $('<div class="lm-grid"></div>');

            info.append(title);
            info.append(statusElem);
            header.append(info);
            wrapper.append(header);
            wrapper.append(grid);
            body.append(wrapper);
            scroll.append(body);
            html.append(scroll.render());

            last = header[0];

            header.on('hover:focus', function () {
                last = this;
                scroll.update($(this), true);
                setBackground(artwork('album', entriesTitle, 'Полный список', ['#1d2632', '#486077']));
            });

            items.push({
                destroy: function () {
                    header.off();
                }
            });

            var initialEntries = entries.slice();
            entries = [];
            appendEntries(initialEntries, false);

            if (!entries.length) {
                var empty = $('<div class="lm-home-info"><div class="lm-home-info__row">Список пока пуст.</div></div>');
                body.append(empty);
                items.push({
                    destroy: function () {
                        empty.remove();
                    }
                });
            }

            this.activity.loader(false);
            this.activity.toggle();
            return this.render();
        };

        this.render = function () { return html; };
        this.destroy = function () {
            destroyed = true;
            Lampa.Arrays.destroy(items);
            scroll.destroy();
            html.remove();
        };
    }

    function MusicBookmarksFull() {
        var scroll = new Lampa.Scroll({ mask: false, over: true });
        var html = $('<div></div>');
        var body = $('<div class="category-full lm-full"></div>');
        var last;
        var destroyed = false;
        var items = [];
        var lines = [];
        var activeLine = -1;
        var header;
        var _this = this;

        bindBase(this, scroll, function () { return last; });

        function bookmarkSections() {
            return [
                {
                    key: 'bookmarked_artists',
                    title: 'Артисты',
                    fullTitle: 'Закладки: артисты',
                    entries: applySectionKey(getBookmarkedEntities(MUSIC.storage.bookmarked_artists).map(mapArtistCard), 'bookmarked_artists')
                },
                {
                    key: 'bookmarked_albums',
                    title: 'Альбомы',
                    fullTitle: 'Закладки: альбомы',
                    entries: applySectionKey(getBookmarkedEntities(MUSIC.storage.bookmarked_albums).map(mapAlbumCard), 'bookmarked_albums')
                },
                {
                    key: 'bookmarked_tracks',
                    title: 'Треки',
                    fullTitle: 'Закладки: треки',
                    entries: applySectionKey(getBookmarkedEntities(MUSIC.storage.bookmarked_tracks).map(mapTrackCard), 'bookmarked_tracks')
                }
            ];
        }

        function destroyRenderedItems() {
            Lampa.Arrays.destroy(items);
            items = [];
            lines = [];
            activeLine = -1;
            body.empty();
        }

        function focusHeader() {
            activeLine = -1;
            last = header ? header[0] : false;
            if (last) scroll.update($(last), true);
            setBackground(artwork('album', 'Закладки', 'Артисты · Альбомы · Треки', ['#1d2632', '#486077']));
            Lampa.Controller.toggle('content');
        }

        function openLine(index) {
            if (index < 0 || index >= lines.length || !lines[index]) return false;

            activeLine = index;
            scroll.update($(lines[index].render(true)), true);

            requestAnimationFrame(function () {
                if (lines[index]) lines[index].toggle();
            });

            return true;
        }

        function restoreAfterRender(sectionKey) {
            if (!sectionKey) {
                focusHeader();
                return;
            }

            for (var i = 0; i < lines.length; i++) {
                if (lines[i] && lines[i].section_key === sectionKey) {
                    openLine(i);
                    return;
                }
            }

            focusHeader();
        }

        function renderScreen(restoreState) {
            if (destroyed) return;

            destroyRenderedItems();

            var sections = bookmarkSections();
            var total = 0;
            sections.forEach(function (section) {
                total += section.entries.length;
            });

            header = $('<div class="lm-full__header selector"></div>');
            var info = $('<div class="lm-full__info"></div>');
            var title = $('<div class="lm-full__title"></div>').text('Закладки');
            var status = $('<div class="lm-full__status"></div>').text(
                total ? ('Карточек: ' + total) : 'Пока пусто'
            );

            info.append(title);
            info.append(status);
            header.append(info);
            body.append(header);

            header.on('hover:focus', function () {
                last = this;
                scroll.update($(this), true);
                setBackground(artwork('album', 'Закладки', 'Артисты · Альбомы · Треки', ['#1d2632', '#486077']));
            });

            items.push({
                destroy: function () {
                    header.off();
                }
            });

            sections.forEach(function (section) {
                if (!section.entries.length) return;

                scroll.minus();

                var visibleEntries = cloneHomeEntries(section.entries.slice(0, HOME_SECTION_LIMIT));
                var line = createMusicLine({
                    title: section.title,
                    entries: visibleEntries,
                    hasMore: section.entries.length > HOME_SECTION_LIMIT,
                    onSelect: function (entry, index) {
                        activateEntry(index >= 0 ? section.entries[index] : entry, section.entries, index);
                    },
                    onMenu: function (entry, index) {
                        activateEntryMenu(index >= 0 ? section.entries[index] : entry, section.entries, index, function () {
                            renderScreen({
                                sectionKey: section.key
                            });
                        });
                    },
                    onFocus: function (entry) {
                        setBackground((entry && entry.background) || IMG_BG);
                        requestAnimationFrame(function () {
                            var current = line.render(true).querySelector('.focus');
                            if (current) last = current;
                        });
                    },
                    onFocusMore: function () {
                        setBackground(artwork('album', section.fullTitle, 'Ещё', ['#1d2632', '#486077']));
                        requestAnimationFrame(function () {
                            var current = line.render(true).querySelector('.focus');
                            if (current) last = current;
                        });
                    },
                    onMore: function () {
                        openBookmarksSection(section.key, section.fullTitle);
                    }
                });

                line.section_key = section.key;
                $(line.render(true)).addClass('lm-home-line lm-bookmarks-line');

                var lineIndex = lines.length;

                line.use({
                    onDown: function () {
                        if (lineIndex < lines.length - 1)
                            openLine(lineIndex + 1);
                    },
                    onUp: function () {
                        if (lineIndex > 0) {
                            openLine(lineIndex - 1);
                            return;
                        }

                        focusHeader();
                    },
                    onBack: _this.back
                });

                body.append(line.render(true));
                lines.push(line);
                items.push(line);
            });

            if (!total) {
                var empty = $('<div class="lm-home-info"><div class="lm-home-info__row">Закладки пока пусты. Добавляй артистов, альбомы и треки через контекстное меню.</div></div>');
                body.append(empty);
                items.push({
                    destroy: function () {
                        empty.remove();
                    }
                });
            }

            _this.activity.loader(false);
            _this.activity.toggle();
            restoreAfterRender(restoreState && restoreState.sectionKey);
        }

        this.onControllerToggle = function () {
            if (lines.length && activeLine >= 0)
                return openLine(activeLine);

            return false;
        };

        this.onControllerDown = function () {
            if (activeLine < 0 && lines.length && last === header[0])
                return openLine(0);

            return false;
        };

        this.create = function () {
            scroll.minus();
            scroll.append(body);
            html.append(scroll.render());
            this.activity.loader(true);
            renderScreen();

            return this.render();
        };

        this.render = function () { return html; };
        this.destroy = function () {
            destroyed = true;
            Lampa.Arrays.destroy(items);
            scroll.destroy();
            html.remove();
        };
    }

    function MusicArtistFull(object) {
        var scroll = new Lampa.Scroll({ mask: false, over: true });
        var html = $('<div></div>');
        var body = $('<div class="category-full lm-full"></div>');
        var last;
        var destroyed = false;
        var items = [];
        var artistLines = [];
        var artistActiveLine = -1;
        var artistHeader = null;
        var artistHeaderImage = IMG_BG;
        var artistData = null;
        var artistSections = [];
        var artistId = object.id;
        var artistProvider = object.provider || null;
        var _this = this;

        bindBase(this, scroll, function () { return last; });

        function findArtistLineFocusTarget(lineElement) {
            if (!lineElement) return null;

            var cards = $(lineElement).find('.lm-card.selector');
            if (cards.length) return cards.get(0);

            var nonMore = $(lineElement).find('.selector').not('.card-more');
            if (nonMore.length) return nonMore.get(0);

            return $(lineElement).find('.card-more.selector').get(0) || null;
        }

        function focusArtistHeader() {
            artistActiveLine = -1;
            last = artistHeader;

            if (last) {
                scroll.update($(last), true);
                setBackground(artistHeaderImage);
                Lampa.Controller.toggle('content');
                return true;
            }

            return false;
        }

        function resetArtistRender() {
            Lampa.Arrays.destroy(items);
            items = [];
            artistLines = [];
            artistActiveLine = -1;
            artistHeader = null;
            body.empty();
        }

        function renderArtist(focusLineIndex) {
            if (!artistData) return;

            resetArtistRender();
            scroll.minus();

            var wrapper = $('<div class="lm-full__wrapper"></div>');
            var header = $('<div class="lm-full__header selector"></div>');
            var poster = $('<div class="lm-full__poster"></div>');
            var posterImg = $('<img class="lm-full__poster-img" src="" alt="" />');
            var info = $('<div class="lm-full__info"></div>');
            var title = $('<div class="lm-full__title"></div>').text(artistData.name || 'Unknown Artist');
            var subtitle = $('<div class="lm-full__artist"></div>').text(artistProvider === 'youtubeaudio' ? 'YouTube Music' : artistProvider === 'soundcloudcharts' ? 'SoundCloud' : artistData.country || artistData.sort_name || 'MusicBrainz Artist');
            var totalSectionEntries = artistSections.reduce(function (sum, section) {
                return sum + (Array.isArray(section.entries) ? section.entries.length : 0);
            }, 0);
            var albums = Array.isArray(artistData.albums) ? artistData.albums : [];
            var statusElem = $('<div class="lm-full__status"></div>').text(
                artistSections.length
                    ? (artistSections.length + ' разделов · ' + totalSectionEntries + ' карточек')
                    : (albums.length + ' альбомов')
            );
            var content = $('<div class="lm-artist-sections"></div>');
            var img = artistImage(artistData, 250);

            posterImg.on('load', function () {
                posterImg.css('opacity', 1);
            });
            posterImg.on('error', function () {
                posterImg.attr('src', img).css('opacity', 1);
            });
            posterImg.attr('src', img);

            poster.append(posterImg);
            info.append(title);
            info.append(subtitle);
            info.append(statusElem);
            header.append(poster);
            header.append(info);
            wrapper.append(header);
            wrapper.append(content);
            body.append(wrapper);

            artistHeader = header[0];
            artistHeaderImage = img;
            last = artistHeader;

            header.on('hover:focus', function () {
                last = this;
                artistActiveLine = -1;
                scroll.update($(this), true);
                setBackground(img);
            });

            function appendCard(grid, entry, entries, index) {
                var cardObj = new MusicCardItem(entry);
                var render = cardObj.render();

                render.on('hover:focus', function () {
                    last = render[0];
                    scroll.update(render, true);
                    setBackground(entry.background || IMG_BG);
                });

                render.on('hover:enter', function () {
                    activateEntry(entry, entries, index);
                });

                render.on('hover:long', function () {
                    activateEntryMenu(entry, entries, index);
                });

                grid.append(render);
                items.push(cardObj);
            }

            if (artistSections.length) {
                artistSections.forEach(function (section) {
                    var entries = Array.isArray(section.entries) ? section.entries : [];
                    if (!entries.length) return;

                    scroll.minus();

                    var lineEntries = cloneHomeEntries(entries);
                    var line = createMusicLine({
                        title: section.title || 'Раздел',
                        entries: lineEntries,
                        hasMore: !!section.next_page || !!section.has_more,
                        onSelect: function (entry, index) {
                            activateEntry(index >= 0 ? entries[index] : entry, entries, index);
                        },
                        onMenu: function (entry, index) {
                            activateEntryMenu(index >= 0 ? entries[index] : entry, entries, index);
                        },
                        onFocus: function (entry) {
                            setBackground((entry && entry.background) || IMG_BG);
                            requestAnimationFrame(function () {
                                var current = line.render(true).querySelector('.focus');
                                if (current) last = current;
                            });
                        },
                        onFocusMore: function () {
                            setBackground(artwork('album', section.title || 'Раздел', 'Ещё', ['#1d2632', '#486077']));
                            requestAnimationFrame(function () {
                                var current = line.render(true).querySelector('.focus');
                                if (current) last = current;
                            });
                        },
                        onMore: function () {
                            openSearchSection(section.title || 'Раздел', entries, {
                                pagination: section.next_page ? {
                                    kind: 'artist_section',
                                    section_id: section.id || '',
                                    provider: artistProvider || section.source_provider || '',
                                    next_page: section.next_page,
                                    limit: HOME_SECTION_LIMIT
                                } : null
                            });
                        }
                    });

                    $(line.render(true)).addClass('lm-home-line lm-artist-line');

                    var lineIndex = artistLines.length;

                    line.use({
                        onDown: function () {
                            if (lineIndex < artistLines.length - 1)
                                openArtistLine(lineIndex + 1);
                        },
                        onUp: function () {
                            if (lineIndex > 0) {
                                openArtistLine(lineIndex - 1);
                                return;
                            }

                            focusArtistHeader();
                        },
                        onBack: _this.back
                    });

                    content.append(line.render(true));
                    artistLines.push(line);
                    items.push(line);
                });
            } else {
                var grid = $('<div class="lm-grid"></div>');
                content.append(grid);

                albums.forEach(function (album) {
                    album.artist_name = album.artist_name || artistData.name;
                    var entry = {
                        type: 'album',
                        id: album.id,
                        title: album.title || 'Untitled',
                        subtitle: album.artist_name || formatDate(album.date),
                        badge: formatYear(album.date) || 'ALBUM',
                        image: albumImage(album, 250),
                        background: albumImage(album, 500),
                        raw: album
                    };

                    appendCard(grid, entry, [entry], 0);
                });
            }

            _this.activity.loader(false);
            _this.activity.toggle();

            if (typeof focusLineIndex === 'number' && focusLineIndex >= 0) {
                requestAnimationFrame(function () {
                    openArtistLine(Math.min(focusLineIndex, artistLines.length - 1));
                });
            }
        }

        function openArtistLine(index) {
            if (index < 0 || index >= artistLines.length || !artistLines[index]) return false;

            artistActiveLine = index;
            var line = artistLines[index];
            scroll.update($(line.render(true)), true);

            requestAnimationFrame(function () {
                if (!artistLines[index]) return;
                line.toggle();

                requestAnimationFrame(function () {
                    if (!artistLines[index]) return;

                    var lineElement = line.render(true);
                    var target = findArtistLineFocusTarget(lineElement);
                    if (!target) return;

                    last = target;
                    Lampa.Controller.collectionSet(lineElement);
                    Lampa.Controller.collectionFocus(target, lineElement);
                    scroll.update($(target), true);
                });
            });

            return true;
        }

        this.onControllerDown = function () {
            if (artistActiveLine < 0 && artistLines.length)
                return openArtistLine(0);

            return false;
        };

        this.create = function () {
            this.activity.loader(true);
            scroll.minus();

            var artistUrl = MUSIC.endpoints.artist + '?id=' + encodeURIComponent(artistId);
            if (artistProvider)
                artistUrl += '&provider=' + encodeURIComponent(artistProvider);

            request(artistUrl, function (artist) {
                if (destroyed) return;

                artistData = artist;
                artistSections = (Array.isArray(artist.sections) ? artist.sections : [])
                    .map(mapSearchSection)
                    .filter(Boolean);
                scroll.append(body);
                html.append(scroll.render());
                renderArtist();
            }, function () {
                if (destroyed) return;
                body.append(new Lampa.Empty().render());
                scroll.append(body);
                html.append(scroll.render());
                _this.activity.loader(false);
            });

            return this.render();
        };

        this.render = function () { return html; };
        this.destroy = function () {
            destroyed = true;
            Lampa.Arrays.destroy(items);
            scroll.destroy();
            html.remove();
        };
    }

    function MusicAlbumFull(object) {
        var scroll = new Lampa.Scroll({ mask: false, over: true });
        var html = $('<div></div>');
        var body = $('<div class="category-full lm-full"></div>');
        var last;
        var destroyed = false;
        var albumId = object.id;
        var albumProvider = object.provider || null;
        var _this = this;

        bindBase(this, scroll, function () { return last; });

        this.create = function () {
            this.activity.loader(true);
            scroll.minus();

            var albumUrl = MUSIC.endpoints.album + '?id=' + encodeURIComponent(albumId);
            if (albumProvider)
                albumUrl += '&provider=' + encodeURIComponent(albumProvider);

            request(albumUrl, function (album) {
                if (destroyed) return;

                if (!album || album.available === false || (!album.id && !album.title && !album.artist_name)) {
                    body.append(new Lampa.Empty().render());
                    scroll.append(body);
                    html.append(scroll.render());
                    _this.activity.loader(false);
                    return;
                }

                var wrapper = $('<div class="lm-full__wrapper"></div>');
                var header = $('<div class="lm-full__header selector"></div>');
                var poster = $('<div class="lm-full__poster"></div>');
                var posterImg = $('<img class="lm-full__poster-img" src="" alt="" />');
                var info = $('<div class="lm-full__info"></div>');
                var title = $('<div class="lm-full__title"></div>').text(album.title || 'Untitled');
                var artistElem = $('<div class="lm-full__artist"></div>').text(album.artist_name || 'Unknown Artist');
                var actions = $('<div class="lm-full__actions"></div>');
                var playAlbumBtn = $('<div class="selector lm-small-btn lm-small-btn--primary">▶ Слушать</div>');
                var shuffleAlbumBtn = $('<div class="selector lm-small-btn">Перемешать</div>');
                var tracksBox = $('<div class="lm-full__tracks"></div>');
                var img = albumImage(album, 500);
                var tracks = (album.tracks || []).map(function (track) {
                    track.artist_name = track.artist_name || album.artist_name;
                    track.album_title = track.album_title || album.title;
                    return track;
                });
                var totalMs = tracks.reduce(function (sum, item) {
                    return sum + (item && item.duration_ms ? item.duration_ms : 0);
                }, 0);
                var metaElem = $('<div class="lm-full__meta"></div>').text(
                    [formatYear(album.date), formatTrackCountLabel(tracks.length), formatTotalDurationLabel(totalMs)]
                        .filter(Boolean)
                        .join(' · ')
                );

                posterImg.on('load', function () {
                    posterImg.css('opacity', 1);
                });
                posterImg.on('error', function () {
                    posterImg.attr('src', img).css('opacity', 1);
                });
                posterImg.attr('src', img);

                poster.append(posterImg);
                info.append(title);
                info.append(artistElem);
                info.append(metaElem);
                actions.append(playAlbumBtn);
                actions.append(shuffleAlbumBtn);
                info.append(actions);
                header.append(poster);
                header.append(info);
                wrapper.append(header);
                wrapper.append(tracksBox);
                body.append(wrapper);
                scroll.append(body);
                html.append(scroll.render());

                // тинт шапки из среднего цвета обложки — как в фулл-плеере
                computeStandaloneIosArtTint(img, function (tint) {
                    if (destroyed || !tint) return;

                    try {
                        wrapper.get(0).style.setProperty('--lm-hero-tint', tint);
                    } catch (e) {}
                });

                last = header[0];

                header.on('hover:focus', function () {
                    last = this;
                    scroll.update($(this), true);
                    setBackground(img);
                });

                playAlbumBtn.on('hover:focus', function () {
                    last = playAlbumBtn[0];
                    scroll.update(playAlbumBtn, true);
                    setBackground(img);
                });

                playAlbumBtn.on('hover:enter', function () {
                    if (tracks.length)
                        playTrack(tracks[0], tracks, 0);
                });

                shuffleAlbumBtn.on('hover:focus', function () {
                    last = shuffleAlbumBtn[0];
                    scroll.update(shuffleAlbumBtn, true);
                    setBackground(img);
                });

                shuffleAlbumBtn.on('hover:enter', function () {
                    playTracksShuffled(tracks);
                });

                tracks.forEach(function (track, index) {
                    var row = buildTrackRow(track, index, { hideAlbum: true });

                    row.on('hover:focus', function () {
                        last = row[0];
                        scroll.update(row, true);
                        setBackground(img);
                    });

                    row.on('hover:enter', function () {
                        playTrack(track, tracks, index);
                    });

                    row.on('hover:long', function () {
                        openTrackMenu(track, tracks, index, null, null, null, { hideOpenAlbum: true });
                    });

                    tracksBox.append(row);
                });

                _this.activity.loader(false);
                _this.activity.toggle();
            }, function () {
                if (destroyed) return;
                body.append(new Lampa.Empty().render());
                scroll.append(body);
                html.append(scroll.render());
                _this.activity.loader(false);
            });

            return this.render();
        };

        this.render = function () { return html; };
        this.destroy = function () {
            destroyed = true;
            scroll.destroy();
            html.remove();
        };
    }

    // ===== REGISTRATION / STYLES =====

    function add() {
        if ($('.menu__item[data-action="lampac_music"]').length) return;

        bindMusicClientDebugErrors();
        bindLampaPlayerDebug();
        registerMusicPlayerSetting();

        Lampa.Player.listener.follow('start', function (data) {
            if (!data || !data.from_music_cluster) {
                resetMusicPlayerBridgeForNonMusic('player-start');

                if (isStandaloneIosAudioActive())
                    stopStandaloneIosAudioPlayback();

                return;
            }

            stopMusicPlaybackLoading();
            traceEmbeddedIos('player-start', 'url=' + describeMusicValue(data.url), true);
            scheduleEmbeddedIosMediaPrepare('player-start');

            if (data.music_track_id)
                syncActivePlaybackHistory(data);

            syncMusicMediaSession(data);
            startMusicPlayerPanelFix(data);
            [0, 120, 450, 1000].forEach(function (delay) {
                setTimeout(function () {
                    ensureLampaPlayerController('player-start+' + delay);
                }, delay);
            });
        });

        Lampa.Player.listener.follow('destroy', function () {
            traceEmbeddedIos('player-destroy', '', true);

            if (isStandaloneIosAudioActive())
                syncStandaloneIosMediaSession();
            else
                syncMusicMediaSession(null);

            stopMusicPlayerPanelFix();
        });

        if (Lampa.PlayerPanel && Lampa.PlayerPanel.listener && typeof Lampa.PlayerPanel.listener.follow === 'function') {
            Lampa.PlayerPanel.listener.follow('quality', function () {
                scheduleMusicPlayerPanelRebind('panel-quality');
            });
            Lampa.PlayerPanel.listener.follow('mouse_rewind', handleMusicPlayerPanelMouseRewind);
        }

        if (Lampa.PlayerVideo && Lampa.PlayerVideo.listener && typeof Lampa.PlayerVideo.listener.follow === 'function') {
            Lampa.PlayerVideo.listener.follow('canplay', function () {
                scheduleMusicPlayerPanelRebind('video-canplay');
            });
        }

        var menuButton = $('<li class="menu__item selector" data-action="lampac_music"><div class="menu__ico">' + MENU_ICON + '</div><div class="menu__text">' + MUSIC.title + '</div></li>');

        menuButton.on('hover:enter', function () {
            Lampa.Activity.push({
                title: MUSIC.title,
                component: 'lampac_music_home',
                page: 1,
                noinfo: true
            });
        });

        $('.menu .menu__list').eq(0).append(menuButton);
        $('body').append(Lampa.Template.get('lampac_music_style', {}, true));
        restoreQueueSnapshot();
    }

    function createMusic() {
        Lampa.Template.add('lampac_music_style', '\
            <style>\
            .card--mask::before, .card--mask::after { display: none !important; }\
            .lm-grid { padding: 1em; }\
            .lm-search-bar { padding: 1em 1em 0 1em; }\
            .lm-search-actions { display: flex; align-items: center; gap: 0.65em; flex-wrap: wrap; }\
            .lm-search-btn {\
                display: inline-flex;\
                align-items: center;\
                justify-content: center;\
                min-width: 14em;\
                background: rgba(255, 255, 255, 0.10);\
                border-radius: 0.4em;\
                padding: 0.82em 1.4em;\
                font-size: 1.15em;\
                text-align: center;\
                transition: background 0.2s;\
            }\
            .lm-search-btn svg { width: 1.2em; height: 1.2em; display: block; }\
            .lm-search-btn--icon {\
                width: 3.1em;\
                min-width: 3.1em;\
                height: 3.1em;\
                padding: 0;\
                border-radius: 0.72em;\
            }\
            .lm-search-btn--icon svg {\
                width: 1.28em;\
                height: 1.28em;\
            }\
            .lm-search-btn--search svg {\
                width: 1.22em;\
                height: 1.22em;\
            }\
            .lm-search-btn.focus {\
                background: #fff;\
                color: #000;\
            }\
            .lm-auth-btn {\
                display: inline-flex;\
                align-items: center;\
                justify-content: center;\
                min-width: 12em;\
                background: rgba(117, 63, 255, 0.22);\
                border-radius: 0.4em;\
                padding: 0.82em 1.25em;\
                font-size: 1.05em;\
                text-align: center;\
                transition: background 0.2s;\
            }\
            .lm-auth-btn.focus {\
                background: #fff;\
                color: #000;\
            }\
            .lm-search-status {\
                margin-top: 0.7em;\
                color: #aeb7c4;\
                font-size: 0.95em;\
            }\
            .lm-search-screen {\
                display: block;\
                padding: 1em 1em 0;\
            }\
            .lm-search-screen > * {\
                width: 100%;\
            }\
            .lm-search-input-wrap {\
                width: 100%;\
            }\
            .lm-search-input-btn {\
                display: block;\
                width: 100%;\
                box-sizing: border-box;\
                padding: 0.62em 0;\
                border: 0;\
                background: transparent;\
                color: #f4f6fa;\
                font-size: 2.05em;\
                line-height: 1.2;\
                text-align: left;\
                border-radius: 0;\
                border-bottom: 1px solid rgba(255,255,255,0.10);\
            }\
            .lm-search-input-field {\
                display: block;\
                width: 100%;\
                margin: 0;\
                padding: 0;\
                border: 0;\
                outline: none;\
                box-shadow: none;\
                background: transparent;\
                color: inherit;\
                font: inherit;\
                line-height: inherit;\
                appearance: none;\
                -webkit-appearance: none;\
            }\
            .lm-search-input-field::placeholder {\
                color: rgba(244,246,250,0.58);\
            }\
            .lm-search-input-btn--placeholder {\
                color: rgba(244,246,250,0.58);\
            }\
            .lm-search-input-btn.focus {\
                color: #ffffff;\
                border-bottom-color: rgba(255,255,255,0.35);\
            }\
            .lm-search-history {\
                display: flex;\
                align-items: center;\
                gap: 0.85em;\
                padding: 0.9em 0 1.15em;\
                overflow-x: auto;\
                overflow-y: hidden;\
            }\
            .lm-search-history.hide { display: none; }\
            .lm-search-history-item {\
                display: inline-flex;\
                align-items: center;\
                gap: 0.65em;\
                flex-shrink: 0;\
                padding: 0.9em 1.15em;\
                border-radius: 0.72em;\
                background: rgba(255,255,255,0.10);\
                color: #f4f6fa;\
                font-size: 1.05em;\
            }\
            .lm-search-history-item.focus {\
                background: #fff;\
                color: #000;\
            }\
            .lm-search-history-item__icon {\
                opacity: 0.78;\
                font-size: 1em;\
            }\
            .lm-search-history-item__title {\
                white-space: nowrap;\
            }\
            .lm-search-screen .lm-search-actions {\
                padding: 0.15em 0 0.8em;\
            }\
            .lm-auth-status {\
                margin-top: 0.35em;\
                color: #c6afd9;\
                font-size: 0.92em;\
                white-space: nowrap;\
                overflow: hidden;\
                text-overflow: ellipsis;\
            }\
            .lm-search-sources {\
                display: flex;\
                align-items: center;\
                gap: 0.7em;\
                padding: 0.35em 1em 1em;\
                flex-wrap: nowrap;\
                overflow-x: auto;\
                overflow-y: hidden;\
            }\
            .lm-search-screen .lm-search-sources {\
                padding: 0 0 1em;\
            }\
            .lm-search-source-tab {\
                display: inline-flex;\
                align-items: center;\
                gap: 0.55em;\
                flex-shrink: 0;\
                padding: 0.82em 1.05em;\
                border-radius: 0.72em;\
                background: rgba(255,255,255,0.08);\
                color: #d8e0ea;\
                font-size: 1.02em;\
                transition: background 0.2s, color 0.2s;\
            }\
            .lm-search-source-tab.active {\
                background: rgba(255,255,255,0.16);\
                color: #ffffff;\
            }\
            .lm-search-source-tab--pending .lm-search-source-tab__count {\
                color: #ffffff;\
                background: rgba(255,255,255,0.20);\
            }\
            .lm-search-source-tab.focus {\
                background: #fff;\
                color: #000;\
            }\
            .lm-search-source-tab__title {\
                white-space: nowrap;\
            }\
            .lm-search-source-tab__count {\
                display: inline-flex;\
                align-items: center;\
                justify-content: center;\
                min-width: 1.65em;\
                height: 1.65em;\
                padding: 0 0.35em;\
                border-radius: 999px;\
                background: rgba(255,255,255,0.14);\
                font-size: 0.8em;\
                font-weight: 700;\
                line-height: 1;\
            }\
            .lm-search-source-tab.focus .lm-search-source-tab__count {\
                background: rgba(0,0,0,0.10);\
                color: #000;\
            }\
            .lm-search-empty {\
                padding: 0.2em 1em 1.2em;\
                color: #aeb7c4;\
                font-size: 1em;\
            }\
            .lm-home-line { width: 100%; padding: 0.2em 0 0.8em; }\
            .lm-home-line .items-line__head { padding: 0 1em 0.45em; }\
            .lm-home-line .items-line__title { font-size: 1.95em; line-height: 1.14; font-weight: 700; color: #f3f6fa; }\
            .lm-home-line .items-line__body { width: 100%; }\
            .lm-home-line .mapping--line { padding: 0 1em 0.2em; }\
            .lm-home-line .mapping--line > * { flex-shrink: 0; }\
            .lm-home-line .mapping--line > .lm-card {\
                width: 20%;\
                min-width: 14.5em;\
                max-width: 18em;\
                padding: 0;\
                display: block;\
            }\
            .lm-home-line .mapping--line > .card-more { flex-shrink: 0; }\
            .lm-home-tags {\
                display: flex;\
                flex-wrap: wrap;\
                gap: 0.8em;\
                padding: 0.4em 1em 0.6em;\
            }\
            .lm-home-tag {\
                padding: 0.72em 1.05em;\
                border-radius: 999px;\
                background: rgba(255,255,255,0.10);\
                font-size: 1em;\
                transition: background 0.2s;\
            }\
            .lm-home-tag.focus { background: #fff; color: #000; }\
            .lm-home-info { padding: 0.2em 1em 1em; }\
            .lm-home-info__row {\
                padding: 0.9em 1em;\
                margin-bottom: 0.55em;\
                border-radius: 0.45em;\
                background: rgba(255,255,255,0.06);\
                color: #d7deea;\
                line-height: 1.4;\
            }\
            .lm-section-title {\
                width: 100%;\
                padding: 0.45em 0.7em 0.2em;\
                box-sizing: border-box;\
                font-size: 1.35em;\
                font-weight: 700;\
                color: #f3f6fa;\
            }\
            .lm-card {\
                display: inline-block;\
                vertical-align: top;\
                width: 16.6666%;\
                padding: 0.7em;\
                box-sizing: border-box;\
                transition: transform 0.2s ease;\
            }\
            .lm-card.focus {\
                transform: scale(1.07);\
                z-index: 10;\
                position: relative;\
            }\
            .lm-card__view {\
                position: relative;\
                padding-bottom: 100%;\
                background: #202020;\
                border-radius: 0.45em;\
                overflow: hidden;\
                box-shadow: 0 0.5em 1em rgba(0,0,0,0.3);\
            }\
            .lm-card.focus .lm-card__view {\
                box-shadow: 0 0.8em 2em rgba(0,0,0,0.6);\
            }\
            .lm-card.focus .lm-card__view::after {\
                border: solid 0.25em #fff;\
                content: "";\
                display: block;\
                position: absolute;\
                left: 0;\
                top: 0;\
                right: 0;\
                bottom: 0;\
                border-radius: 0.45em;\
                z-index: 5;\
            }\
            .lm-card__img {\
                position: absolute;\
                top: 0;\
                left: 0;\
                width: 100%;\
                height: 100%;\
                object-fit: cover;\
                opacity: 0;\
                transition: opacity 0.3s ease;\
            }\
            .loaded .lm-card__img { opacity: 1; }\
            .lm-card__badge {\
                position: absolute;\
                bottom: 0.55em;\
                right: 0.55em;\
                background: rgba(0,0,0,0.72);\
                color: #fff;\
                padding: 0.2em 0.55em;\
                border-radius: 0.3em;\
                font-size: 0.78em;\
                font-weight: 700;\
                z-index: 2;\
            }\
            .lm-card__info {\
                min-height: 3.6em;\
                margin-top: 0.55em;\
                overflow: hidden;\
            }\
            .lm-card__title {\
                font-size: 1.05em;\
                font-weight: 700;\
                white-space: nowrap;\
                overflow: hidden;\
                text-overflow: ellipsis;\
            }\
            .lm-card__subtitle {\
                font-size: 0.9em;\
                color: #aaa;\
                white-space: nowrap;\
                overflow: hidden;\
                text-overflow: ellipsis;\
                margin-top: 0.18em;\
            }\
            .lm-full { padding: 2em; width: 100%; }\
            .lm-full__wrapper { width: 100%; max-width: 1200px; }\
            .lm-full__header {\
                display: flex;\
                align-items: center;\
                gap: 2em;\
                margin-bottom: 1.6em;\
                padding: 1.6em;\
                border-radius: 1.1em;\
                background: linear-gradient(135deg, var(--lm-hero-tint, rgba(56,62,82,0.38)) 0%, rgba(18,20,26,0.55) 100%);\
                transition: background 0.3s;\
            }\
            .lm-full__header.focus { outline: 2px solid rgba(255,255,255,0.55); outline-offset: -2px; }\
            .lm-full__poster { width: 260px; flex-shrink: 0; }\
            .lm-full__poster-img {\
                width: 100%;\
                aspect-ratio: 1 / 1;\
                object-fit: cover;\
                border-radius: 0.9em;\
                box-shadow: 0 1.4em 3em rgba(0,0,0,0.55), 0 0.3em 0.9em rgba(0,0,0,0.35);\
                opacity: 0;\
                transition: opacity 0.3s ease;\
            }\
            .lm-full__actions { margin-top: 1.3em; display: flex; flex-wrap: wrap; gap: 0.7em; }\
            .lm-small-btn {\
                display: inline-flex;\
                align-items: center;\
                justify-content: center;\
                min-width: 10.5em;\
                background: rgba(255, 255, 255, 0.10);\
                border-radius: 999px;\
                padding: 0.72em 1.4em;\
                font-size: 1em;\
                font-weight: 700;\
                transition: background 0.2s, color 0.2s;\
            }\
            .lm-small-btn--primary { background: #f5f5f7; color: #111; }\
            .lm-small-btn.focus { background: #fff; color: #000; outline: 2px solid rgba(255,255,255,0.8); }\
            .lm-full__title { font-size: 2.5em; font-weight: 800; letter-spacing: -0.012em; line-height: 1.08; margin-bottom: 0.22em; }\
            .lm-full__artist { font-size: 1.28em; color: rgba(235,235,245,0.72); margin-bottom: 0.45em; }\
            .lm-full__meta { font-size: 0.98em; color: rgba(235,235,245,0.55); font-weight: 600; }\
            .lm-full__empty { padding: 2em 1em; color: rgba(235,235,245,0.55); font-size: 1.05em; }\
            .lm-full__year { font-size: 1.1em; margin-bottom: 0.7em; }\
            .lm-full__status { font-size: 1.02em; color: rgba(235,235,245,0.55); font-weight: 700; }\
            .lm-track {\
                display: flex;\
                align-items: center;\
                gap: 1em;\
                padding: 0.85em 1em;\
                border-bottom: 1px solid rgba(255,255,255,0.05);\
                border-radius: 0.6em;\
                transition: background 0.2s;\
            }\
            .lm-track:hover { background: rgba(255,255,255,0.06); }\
            .lm-track--current {\
                background: rgba(255,255,255,0.08);\
            }\
            .lm-track.focus { background: #fff; color: #000; }\
            .lm-track.focus .lm-track__num, .lm-track.focus .lm-track__meta, .lm-track.focus .lm-track__time { color: rgba(0,0,0,0.68); }\
            .lm-track__num { width: 2.4em; color: rgba(235,235,245,0.45); font-weight: 600; text-align: right; font-variant-numeric: tabular-nums; flex-shrink: 0; }\
            .lm-track__num-play { display: none; font-size: 0.82em; }\
            .lm-track.focus .lm-track__num-text, .lm-track:hover .lm-track__num-text { display: none; }\
            .lm-track.focus .lm-track__num-play, .lm-track:hover .lm-track__num-play { display: inline; }\
            .lm-track__thumb { width: 3.1em; height: 3.1em; border-radius: 0.5em; overflow: hidden; flex-shrink: 0; background: rgba(255,255,255,0.06); }\
            .lm-track__thumb img { width: 100%; height: 100%; object-fit: cover; display: block; }\
            .lm-track__body { flex: 1; min-width: 0; }\
            .lm-track__name { font-size: 1.06em; font-weight: 700; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }\
            .lm-track__meta { font-size: 0.86em; color: rgba(235,235,245,0.5); margin-top: 0.24em; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }\
            .lm-track__time { width: 4.2em; text-align: right; color: rgba(235,235,245,0.5); font-weight: 600; font-variant-numeric: tabular-nums; flex-shrink: 0; }\
            .lm-track--current .lm-track__name, .lm-track--current .lm-track__num-text { color: #ffffff; }\
            .lm-full__tracks { padding-bottom: 5em; }\
            .player-video.lm-player-video--music {\
                position: relative;\
                background: #101012 !important;\
                overflow: hidden;\
            }\
            .player-video.lm-player-video--music .player-video__display,\
            .player-video.lm-player-video--music .player-video__video,\
            .player-video.lm-player-video--music video {\
                opacity: 0 !important;\
            }\
            .player-panel__music-lyrics {\
                min-width: 3.35em;\
                padding-left: 0.65em;\
                padding-right: 0.65em;\
                font-size: 0.92em;\
                font-weight: 800;\
                letter-spacing: 0;\
                text-transform: none;\
            }\
            .player-panel__music-lyrics.active {\
                background: #fff;\
                color: #111318;\
            }\
            .lm-player-visual {\
                position: fixed;\
                inset: 0;\
                z-index: 0;\
                display: flex;\
                align-items: center;\
                justify-content: center;\
                overflow: hidden;\
                color: #f5f5f7;\
                pointer-events: none;\
                background: #101012;\
            }\
            .lm-player-visual__backdrop {\
                position: absolute;\
                inset: -3em;\
                background-size: cover;\
                background-position: center;\
                filter: blur(52px) saturate(150%);\
                opacity: 0.46;\
                transform: scale(1.12);\
            }\
            .lm-player-visual__backdrop::after {\
                content: "";\
                position: absolute;\
                inset: 0;\
                background: radial-gradient(circle at 50% 42%, rgba(255,255,255,0.10) 0%, rgba(16,16,18,0.34) 38%, rgba(10,10,12,0.88) 100%);\
            }\
            .lm-player-visual__top {\
                position: absolute;\
                top: max(2.1em, env(safe-area-inset-top));\
                right: max(2.1em, env(safe-area-inset-right));\
                z-index: 3;\
                pointer-events: auto;\
            }\
            .lm-player-visual__lyrics-toggle {\
                padding: 0.58em 1.05em;\
                border-radius: 999px;\
                background: rgba(255,255,255,0.14);\
                color: rgba(245,245,247,0.92);\
                font-size: 1em;\
                font-weight: 750;\
                backdrop-filter: blur(18px);\
                -webkit-backdrop-filter: blur(18px);\
                box-shadow: inset 0 0 0 1px rgba(255,255,255,0.16), 0 0.8em 1.8em rgba(0,0,0,0.24);\
            }\
            .lm-player-visual__lyrics-toggle.focus,\
            .lm-player-visual__lyrics-toggle.active {\
                background: rgba(255,255,255,0.92);\
                color: #111318;\
            }\
            .lm-player-visual__center {\
                position: relative;\
                z-index: 1;\
                display: flex;\
                flex-direction: column;\
                align-items: center;\
                justify-content: center;\
                width: min(78vw, 32em);\
                text-align: center;\
                transform: translateY(-2vh);\
            }\
            .lm-player-visual__art-view {\
                display: flex;\
                flex-direction: column;\
                align-items: center;\
                justify-content: center;\
                width: 100%;\
            }\
            .lm-player-visual__art {\
                width: min(46vh, 42vw, 24em);\
                aspect-ratio: 1 / 1;\
                border-radius: 1.35em;\
                overflow: hidden;\
                background: rgba(255,255,255,0.08);\
                box-shadow: 0 2.2em 4.5em rgba(0,0,0,0.48), 0 0.45em 1.2em rgba(0,0,0,0.32);\
            }\
            .lm-player-visual__art img {\
                width: 100%;\
                height: 100%;\
                object-fit: cover;\
                display: block;\
            }\
            .lm-player-visual__title {\
                max-width: 100%;\
                margin-top: 1.05em;\
                font-size: 2.15em;\
                line-height: 1.12;\
                font-weight: 800;\
                white-space: nowrap;\
                overflow: hidden;\
                text-overflow: ellipsis;\
                text-shadow: 0 0.2em 0.9em rgba(0,0,0,0.38);\
            }\
            .lm-player-visual__artist {\
                max-width: 100%;\
                margin-top: 0.32em;\
                font-size: 1.18em;\
                color: rgba(235,235,245,0.72);\
                white-space: nowrap;\
                overflow: hidden;\
                text-overflow: ellipsis;\
                text-shadow: 0 0.2em 0.9em rgba(0,0,0,0.34);\
            }\
            .lm-player-visual__lyrics-view {\
                display: none;\
                width: min(84vw, 54em);\
                max-height: min(70vh, 34em);\
                position: relative;\
                pointer-events: auto;\
            }\
            .lm-player-visual--lyrics .lm-player-visual__center {\
                width: min(84vw, 54em);\
                transform: translateY(0);\
            }\
            .lm-player-visual--lyrics .lm-player-visual__art-view { display: none; }\
            .lm-player-visual--lyrics .lm-player-visual__lyrics-view { display: block; }\
            .lm-player-visual__lyrics-scroll {\
                max-height: min(70vh, 34em);\
                overflow-y: auto;\
                -webkit-overflow-scrolling: touch;\
                padding: 1.2em 1.45em 1.6em;\
                border-radius: 1.15em;\
                background: rgba(10,10,12,0.34);\
                box-shadow: inset 0 0 0 1px rgba(255,255,255,0.08);\
                text-align: left;\
            }\
            .lm-player-visual__lyrics-follow {\
                display: none;\
                position: absolute;\
                right: 1.1em;\
                bottom: 1.1em;\
                z-index: 2;\
                padding: 0.48em 0.85em;\
                border-radius: 999px;\
                background: rgba(255,255,255,0.9);\
                color: #111318;\
                font-size: 0.92em;\
                font-weight: 800;\
                box-shadow: 0 0.8em 2em rgba(0,0,0,0.28);\
            }\
            .lm-player-visual--lyrics-manual .lm-player-visual__lyrics-follow { display: block; }\
            .lm-player-visual__lyrics-follow.focus { background: #fff; color: #000; }\
            .lm-player-visual__lyrics-line {\
                padding: 0.34em 0.12em;\
                font-size: 1.42em;\
                line-height: 1.32;\
                font-weight: 800;\
                color: rgba(245,245,247,0.42);\
                transition: color 0.22s ease, transform 0.22s ease;\
            }\
            .lm-player-visual__lyrics-line.active {\
                color: #ffffff;\
                transform: translateX(0.18em);\
            }\
            .lm-player-visual__lyrics-line--empty { font-size: 1em; opacity: 0.64; }\
            .lm-player-visual__lyrics-plain {\
                white-space: pre-wrap;\
                font-size: 1.22em;\
                line-height: 1.54;\
                font-weight: 650;\
                color: rgba(245,245,247,0.88);\
            }\
            .lm-player-visual__lyrics-message {\
                min-height: 8em;\
                display: flex;\
                align-items: center;\
                justify-content: center;\
                text-align: center;\
                font-size: 1.2em;\
                font-weight: 700;\
                color: rgba(245,245,247,0.72);\
            }\
            .lm-ios-full-player {\
                position: fixed;\
                inset: 0;\
                z-index: 1002;\
                display: none;\
                color: #f5f5f7;\
                background: #101012;\
                overflow: hidden;\
            }\
            .lm-ios-full-player--visible { display: block; }\
            body.lm-ios-full-player-open .lm-ios-player { display: none !important; }\
            .lm-ios-full-player__backdrop {\
                position: absolute;\
                inset: -2em;\
                background-size: cover;\
                background-position: center;\
                filter: blur(46px) saturate(160%);\
                opacity: 0.52;\
                transform: scale(1.1);\
            }\
            .lm-ios-full-player__backdrop::after {\
                content: "";\
                position: absolute;\
                inset: 0;\
                background: linear-gradient(180deg, var(--lm-full-tint, rgba(52,52,60,0.42)) 0%, rgba(13,13,16,0.78) 52%, rgba(10,10,12,0.94) 100%);\
            }\
            .lm-ios-full-player__shell {\
                position: relative;\
                height: 100%;\
                box-sizing: border-box;\
                display: flex;\
                flex-direction: column;\
                overflow: hidden;\
                padding: calc(env(safe-area-inset-top, 0px) + 0.8em) 1.05em calc(env(safe-area-inset-bottom, 0px) + 1.2em);\
                transition: transform 0.18s cubic-bezier(0.2,0.8,0.2,1), opacity 0.18s ease;\
                will-change: transform;\
            }\
            .lm-ios-full-player--gesture-dragging .lm-ios-full-player__shell { transition: none; }\
            .lm-ios-full-player__grabber {\
                width: 2.6em;\
                height: 0.3em;\
                flex-shrink: 0;\
                border-radius: 999px;\
                background: rgba(255,255,255,0.30);\
                margin: 0 auto 0.75em;\
            }\
            .lm-ios-full-player__head {\
                display: flex;\
                align-items: center;\
                justify-content: space-between;\
                gap: 0.8em;\
                width: 100%;\
                max-width: 46em;\
                margin: 0 auto 1.05em;\
            }\
            .lm-ios-full-player__head-title {\
                min-width: 0;\
                flex: 1;\
                text-align: center;\
                font-size: 0.92em;\
                font-weight: 700;\
                color: rgba(245,245,247,0.72);\
                white-space: nowrap;\
                overflow: hidden;\
                text-overflow: ellipsis;\
            }\
            .lm-ios-full-player__tool {\
                width: 2.65em;\
                height: 2.65em;\
                display: inline-flex;\
                align-items: center;\
                justify-content: center;\
                flex-shrink: 0;\
                border-radius: 999px;\
                background: rgba(255,255,255,0.07);\
                color: rgba(255,255,255,0.85);\
            }\
            .lm-ios-full-player__tool svg { width: 1.15em; height: 1.15em; display: block; }\
            .lm-ios-full-player__hero {\
                max-width: 34em;\
                width: 100%;\
                margin: 0 auto;\
                text-align: center;\
                min-height: 0;\
                flex: 1 1 auto;\
                display: flex;\
                flex-direction: column;\
                justify-content: center;\
            }\
            .lm-ios-full-player__art {\
                width: min(82vw, 23em, 46vh);\
                aspect-ratio: 1 / 1;\
                margin: 0 auto 1.35em;\
                border-radius: 0.95em;\
                overflow: hidden;\
                background: rgba(255,255,255,0.08);\
                box-shadow: 0 1.6em 3.6em rgba(0,0,0,0.55), 0 0.3em 1em rgba(0,0,0,0.35);\
                transition: transform 0.38s cubic-bezier(0.2,0.9,0.3,1.18), box-shadow 0.38s ease;\
            }\
            .lm-ios-full-player--paused .lm-ios-full-player__art {\
                transform: scale(0.86);\
                box-shadow: 0 0.9em 2.2em rgba(0,0,0,0.42);\
            }\
            .lm-ios-full-player__art img {\
                width: 100%;\
                height: 100%;\
                object-fit: cover;\
                display: block;\
            }\
            .lm-ios-full-player__title {\
                font-size: 1.62em;\
                letter-spacing: -0.012em;\
                line-height: 1.16;\
                font-weight: 800;\
                display: -webkit-box;\
                -webkit-line-clamp: 2;\
                -webkit-box-orient: vertical;\
                white-space: normal;\
                overflow: hidden;\
            }\
            .lm-ios-full-player__artist {\
                margin-top: 0.35em;\
                color: rgba(235,235,245,0.68);\
                font-size: 1.02em;\
                white-space: nowrap;\
                overflow: hidden;\
                text-overflow: ellipsis;\
            }\
            .lm-ios-full-player__progress {\
                margin-top: 1.35em;\
            }\
            .lm-ios-full-player__seek {\
                -webkit-appearance: none;\
                appearance: none;\
                width: 100%;\
                height: 2.1em;\
                margin: 0;\
                background: transparent;\
                --lm-seek-fill: 0%;\
            }\
            .lm-ios-full-player__seek::-webkit-slider-runnable-track {\
                height: 0.36em;\
                border-radius: 999px;\
                background: linear-gradient(90deg, rgba(245,245,247,0.95) 0%, rgba(245,245,247,0.95) var(--lm-seek-fill), rgba(255,255,255,0.22) var(--lm-seek-fill), rgba(255,255,255,0.22) 100%);\
            }\
            .lm-ios-full-player__seek::-webkit-slider-thumb {\
                -webkit-appearance: none;\
                width: 1.15em;\
                height: 1.15em;\
                margin-top: -0.4em;\
                border-radius: 50%;\
                background: #ffffff;\
                box-shadow: 0 0.12em 0.5em rgba(0,0,0,0.45);\
            }\
            .lm-ios-full-player__times {\
                display: flex;\
                justify-content: space-between;\
                margin-top: 0.32em;\
                color: rgba(235,235,245,0.58);\
                font-size: 0.82em;\
                font-variant-numeric: tabular-nums;\
            }\
            .lm-ios-full-player__actions {\
                display: flex;\
                align-items: center;\
                justify-content: center;\
                gap: clamp(0.6em, 3.4vw, 1.4em);\
                margin-top: 1.35em;\
            }\
            .lm-ios-full-player__btn--mini {\
                width: 3em;\
                height: 3em;\
                position: relative;\
                color: rgba(245,245,247,0.55);\
            }\
            .lm-ios-full-player__btn--mini svg { width: 1.25em; height: 1.25em; }\
            .lm-ios-full-player__btn--mini.active { color: #ffffff; }\
            .lm-ios-full-player__btn--mini.active::after {\
                content: "";\
                position: absolute;\
                bottom: 0.3em;\
                left: 50%;\
                transform: translateX(-50%);\
                width: 0.28em;\
                height: 0.28em;\
                border-radius: 50%;\
                background: #ffffff;\
            }\
            .lm-ios-full-player__btn {\
                width: 3.9em;\
                height: 3.9em;\
                display: inline-flex;\
                align-items: center;\
                justify-content: center;\
                border-radius: 999px;\
                color: #fff;\
                background: transparent;\
            }\
            .lm-ios-full-player__btn:active { background: rgba(255,255,255,0.10); }\
            .lm-ios-full-player__btn svg { width: 1.7em; height: 1.7em; display: block; }\
            .lm-ios-full-player__btn--primary {\
                width: 4.9em;\
                height: 4.9em;\
                background: #f5f5f7;\
                color: #111;\
                box-shadow: 0 0.5em 1.4em rgba(0,0,0,0.35);\
            }\
            .lm-ios-full-player__btn--primary:active { background: #e4e4e8; }\
            .lm-ios-full-player__btn--primary svg { width: 1.9em; height: 1.9em; }\
            .lm-ios-full-player__quick-actions {\
                width: 100%;\
                display: grid;\
                grid-template-columns: repeat(6, minmax(0, 1fr));\
                gap: 0.45em;\
                margin-top: 1.1em;\
            }\
            .lm-ios-full-player__quick {\
                min-width: 0;\
                height: 3.2em;\
                display: inline-flex;\
                flex-direction: column;\
                align-items: center;\
                justify-content: center;\
                gap: 0.32em;\
                padding: 0 0.35em;\
                border-radius: 1em;\
                background: rgba(255,255,255,0.06);\
                color: rgba(245,245,247,0.72);\
                font-size: 0.74em;\
                font-weight: 700;\
                white-space: nowrap;\
            }\
            .lm-ios-full-player__quick.active { background: rgba(255,255,255,0.20); color: #fff; }\
            .lm-ios-full-player__quick svg { width: 1em; height: 1em; display: block; flex-shrink: 0; }\
            .lm-ios-full-player__quick span { overflow: hidden; text-overflow: ellipsis; }\
            .lm-ios-full-player__timer-status {\
                display: none;\
                margin-top: 0.55em;\
                color: rgba(235,235,245,0.62);\
                font-size: 0.82em;\
                font-weight: 700;\
            }\
            .lm-ios-full-player__timer-status--visible { display: block; }\
            .lm-ios-full-player__stop {\
                display: inline-flex;\
                align-items: center;\
                justify-content: center;\
                min-width: 8.5em;\
                min-height: 2.6em;\
                margin: 0.8em auto 0;\
                padding: 0 1.05em;\
                border-radius: 999px;\
                background: transparent;\
                color: rgba(245,245,247,0.5);\
                font-size: 0.86em;\
                font-weight: 700;\
            }\
            .lm-ios-full-player__queue-summary {\
                flex: 0 0 auto;\
                padding: 0 0.75em 0.65em;\
                color: rgba(235,235,245,0.58);\
                font-size: 0.86em;\
                font-weight: 700;\
            }\
            .lm-ios-full-player__queue-list {\
                min-height: 0;\
                flex: 1 1 auto;\
                overflow-y: auto;\
                -webkit-overflow-scrolling: touch;\
                padding-bottom: 1em;\
            }\
            .lm-ios-full-player__queue-divider {\
                text-align: center;\
                font-size: 0.74em;\
                letter-spacing: 0.06em;\
                text-transform: uppercase;\
                color: rgba(255, 255, 255, 0.45);\
                padding: 0.9em 0 0.35em;\
            }\
            .lm-ios-full-player__queue-item {\
                display: flex;\
                align-items: center;\
                gap: 0.82em;\
                min-height: 4.4em;\
                padding: 0.55em 0.62em;\
                border-radius: 0.72em;\
                color: #f5f5f7;\
            }\
            .lm-ios-full-player__queue-item--current { background: rgba(255,255,255,0.12); }\
            .lm-ios-full-player__queue-item.focus { background: #f5f5f7; color: #111; }\
            .lm-ios-full-player__queue-img {\
                width: 3.2em;\
                height: 3.2em;\
                flex-shrink: 0;\
                border-radius: 0.45em;\
                overflow: hidden;\
                background: rgba(255,255,255,0.08);\
            }\
            .lm-ios-full-player__queue-img img { width: 100%; height: 100%; object-fit: cover; display: block; }\
            .lm-ios-full-player__queue-body { min-width: 0; flex: 1; text-align: left; }\
            .lm-ios-full-player__queue-track { font-size: 0.98em; font-weight: 700; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }\
            .lm-ios-full-player__queue-artist { margin-top: 0.18em; color: rgba(235,235,245,0.58); font-size: 0.84em; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }\
            .lm-ios-full-player__queue-item.focus .lm-ios-full-player__queue-artist { color: rgba(0,0,0,0.58); }\
            .lm-ios-full-player__queue-time { width: 3.5em; flex-shrink: 0; color: rgba(235,235,245,0.58); font-size: 0.8em; font-weight: 700; font-variant-numeric: tabular-nums; text-align: right; }\
            .lm-ios-full-player__queue-item.focus .lm-ios-full-player__queue-time { color: rgba(0,0,0,0.58); }\
            .lm-ios-full-player__btn.disabled { opacity: 0.36; pointer-events: none; }\
            .lm-ios-full-player__sheet {\
                position: absolute;\
                inset: 0;\
                z-index: 4;\
                display: none;\
                align-items: flex-end;\
                justify-content: center;\
                background: rgba(0,0,0,0.48);\
            }\
            .lm-ios-full-player--sheet-open .lm-ios-full-player__sheet { display: flex; }\
            .lm-ios-full-player__sheet-panel {\
                width: 100%;\
                max-width: 44em;\
                max-height: 72%;\
                display: flex;\
                flex-direction: column;\
                overflow: hidden;\
                border-radius: 1.2em 1.2em 0 0;\
                background: rgba(28,28,30,0.96);\
                border: 1px solid rgba(255,255,255,0.10);\
                box-shadow: 0 -1em 2.6em rgba(0,0,0,0.42);\
                transition: transform 0.18s cubic-bezier(0.2,0.8,0.2,1);\
                will-change: transform;\
            }\
            .lm-ios-full-player--sheet-dragging .lm-ios-full-player__sheet-panel { transition: none; }\
            .lm-ios-full-player__sheet-panel::before {\
                content: "";\
                width: 2.8em;\
                height: 0.28em;\
                flex: 0 0 auto;\
                margin: 0.55em auto 0;\
                border-radius: 999px;\
                background: rgba(235,235,245,0.34);\
            }\
            .lm-ios-full-player__sheet-head {\
                display: flex;\
                align-items: center;\
                justify-content: space-between;\
                gap: 1em;\
                padding: 1em 1em 0.65em;\
                flex: 0 0 auto;\
            }\
            .lm-ios-full-player__sheet-title { font-size: 1.08em; font-weight: 800; }\
            .lm-ios-full-player__sheet-close {\
                width: 2.3em;\
                height: 2.3em;\
                display: inline-flex;\
                align-items: center;\
                justify-content: center;\
                border-radius: 999px;\
                background: rgba(255,255,255,0.10);\
                color: rgba(255,255,255,0.86);\
                flex-shrink: 0;\
            }\
            .lm-ios-full-player__sheet-close svg { width: 1em; height: 1em; display: block; }\
            .lm-ios-full-player__sheet-body {\
                min-height: 0;\
                overflow-y: auto;\
                -webkit-overflow-scrolling: touch;\
                padding: 0.2em 0.75em 1em;\
            }\
            .lm-ios-full-player__lyrics {\
                padding: 0.3em 0.6em 1.4em;\
            }\
            .lm-ios-full-player__lyrics-line {\
                padding: 0.4em 0.3em;\
                font-size: 1.16em;\
                font-weight: 700;\
                line-height: 1.32;\
                color: rgba(245,245,247,0.4);\
                border-radius: 0.5em;\
                transition: color 0.25s ease;\
            }\
            .lm-ios-full-player__lyrics-line.active { color: #ffffff; }\
            .lm-ios-full-player__lyrics-line--empty { font-size: 0.9em; opacity: 0.6; }\
            .lm-ios-full-player__lyrics--plain {\
                white-space: pre-wrap;\
                font-size: 1em;\
                line-height: 1.5;\
                color: rgba(245,245,247,0.85);\
            }\
            .lm-ios-full-player__sheet-body--queue {\
                display: flex;\
                flex-direction: column;\
                overflow: hidden;\
                padding-left: 0.75em;\
                padding-right: 0.75em;\
            }\
            .lm-ios-full-player__sheet-body--queue .lm-ios-full-player__queue-list {\
                overflow-y: auto;\
                -webkit-overflow-scrolling: touch;\
            }\
            .lm-ios-full-player__sheet-row {\
                display: flex;\
                align-items: center;\
                gap: 0.8em;\
                min-height: 3.7em;\
                padding: 0.7em 0.75em;\
                border-radius: 0.82em;\
                color: #f5f5f7;\
            }\
            .lm-ios-full-player__sheet-row.active { background: rgba(255,255,255,0.13); }\
            .lm-ios-full-player__sheet-row.disabled { opacity: 0.45; pointer-events: none; }\
            .lm-ios-full-player__sheet-row-main { min-width: 0; flex: 1; }\
            .lm-ios-full-player__sheet-row-title { font-size: 0.98em; font-weight: 700; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }\
            .lm-ios-full-player__sheet-row-subtitle { margin-top: 0.18em; color: rgba(235,235,245,0.58); font-size: 0.84em; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }\
            .lm-ios-full-player__sheet-row-trailing { flex-shrink: 0; color: rgba(235,235,245,0.62); font-size: 0.82em; font-weight: 700; }\
            .lm-ios-full-player__sheet-message { padding: 1.4em 0.8em; color: rgba(235,235,245,0.68); font-weight: 700; text-align: center; }\
            .lm-ios-full-player__sheet-message--loading { color: rgba(245,245,247,0.9); }\
            .lm-ios-player {\
                position: fixed;\
                left: 0.85em;\
                right: 0.85em;\
                top: 74%;\
                bottom: auto;\
                transform: translateY(-50%);\
                z-index: 1001;\
                display: none;\
                align-items: center;\
                justify-content: space-between;\
                gap: 1em;\
                padding: 0.9em 1em;\
                border-radius: 1.28em;\
                background: rgba(28, 28, 30, 0.72);\
                border: 1px solid rgba(255,255,255,0.08);\
                box-shadow: 0 1.1em 2.5em rgba(0,0,0,0.28);\
                backdrop-filter: blur(24px) saturate(180%);\
                -webkit-backdrop-filter: blur(24px) saturate(180%);\
                flex-direction: column;\
                align-items: stretch;\
            }\
            .lm-ios-player--visible { display: flex; }\
            .lm-ios-player__top { display: flex; align-items: flex-start; justify-content: space-between; gap: 0.9em; width: 100%; }\
            .lm-ios-player__meta { min-width: 0; flex: 1; }\
            .lm-ios-player__title { font-size: 1.08em; font-weight: 700; letter-spacing: -0.01em; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }\
            .lm-ios-player__artist { margin-top: 0.2em; font-size: 0.9em; color: rgba(235,235,245,0.62); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }\
            .lm-ios-player__tools { display: flex; align-items: center; gap: 0.48em; flex-shrink: 0; }\
            .lm-ios-player__queue {\
                height: 2.4em;\
                display: inline-flex;\
                align-items: center;\
                gap: 0.42em;\
                padding: 0 0.82em;\
                border-radius: 999px;\
                background: rgba(255,255,255,0.10);\
                color: rgba(255,255,255,0.92);\
                font-size: 0.82em;\
                font-weight: 700;\
                white-space: nowrap;\
            }\
            .lm-ios-player__queue svg { width: 1em; height: 1em; display: block; }\
            .lm-ios-player__toolbtn {\
                width: 2.4em;\
                height: 2.4em;\
                display: inline-flex;\
                align-items: center;\
                justify-content: center;\
                border-radius: 999px;\
                background: rgba(255,255,255,0.10);\
                color: rgba(255,255,255,0.9);\
            }\
            .lm-ios-player__toolbtn svg { width: 1em; height: 1em; display: block; }\
            .lm-ios-player__progress { display: flex; align-items: center; gap: 0.65em; width: 100%; }\
            .lm-ios-player__time {\
                width: 3.15em;\
                font-size: 0.76em;\
                color: rgba(235,235,245,0.58);\
                font-variant-numeric: tabular-nums;\
                text-align: center;\
                flex-shrink: 0;\
            }\
            .lm-ios-player__seek {\
                flex: 1;\
                margin: 0;\
                accent-color: #f5f5f7;\
            }\
            .lm-ios-player__actions { display: flex; align-items: center; justify-content: center; gap: 0.9em; width: 100%; }\
            .lm-ios-player__btn {\
                width: 3.05em;\
                height: 3.05em;\
                display: inline-flex;\
                align-items: center;\
                justify-content: center;\
                border-radius: 999px;\
                color: #fff;\
                background: rgba(255,255,255,0.10);\
                box-sizing: border-box;\
            }\
            .lm-ios-player__btn svg {\
                width: 1.14em;\
                height: 1.14em;\
                display: block;\
            }\
            .lm-ios-player__btn--primary {\
                width: 3.55em;\
                height: 3.55em;\
                background: #f5f5f7;\
                color: #111;\
                box-shadow: 0 0.3em 0.9em rgba(255,255,255,0.16);\
            }\
            .lm-ios-player__btn--primary svg { width: 1.3em; height: 1.3em; }\
            .lm-ios-player__toolbtn--close { color: rgba(255,255,255,0.76); }\
            .lm-ios-player__btn.disabled, .lm-ios-player__queue.disabled {\
                opacity: 0.38;\
            }\
            @media screen and (max-width: 1200px) { .lm-card { width: 20%; } }\
            @media screen and (max-width: 900px) { .lm-card { width: 25%; } .lm-full__header { flex-direction: column; } .lm-full__poster { width: 220px; } }\
            @media screen and (max-width: 700px) {\
                .lm-card { width: 33.3333%; }\
                .lm-ios-full-player__shell { padding-left: 0.88em; padding-right: 0.88em; }\
                .lm-ios-full-player__head { margin-bottom: 0.65em; }\
                .lm-ios-full-player__tool { width: 2.48em; height: 2.48em; }\
                .lm-ios-full-player__art { width: min(74vw, 18em, 38vh); margin-bottom: 0.95em; border-radius: 0.95em; }\
                .lm-ios-full-player__title { font-size: 1.18em; }\
                .lm-ios-full-player__artist { font-size: 0.9em; }\
                .lm-ios-full-player__progress { margin-top: 0.82em; }\
                .lm-ios-full-player__actions { gap: 0.76em; margin-top: 0.86em; }\
                .lm-ios-full-player__btn { width: 2.9em; height: 2.9em; }\
                .lm-ios-full-player__btn--primary { width: 3.55em; height: 3.55em; }\
                /* на телефоне 6 чипов в ряд обрезают подписи (ellipsis) —\
                   два ряда по три читаются лучше */\
                .lm-ios-full-player__quick-actions { gap: 0.4em; margin-top: 0.82em; grid-template-columns: repeat(3, minmax(0, 1fr)); }\
                .lm-ios-full-player__quick { height: 3.25em; padding: 0 0.2em; font-size: 0.68em; }\
                .lm-ios-full-player__timer-status { margin-top: 0.42em; font-size: 0.76em; }\
                .lm-ios-full-player__stop { min-height: 2.32em; margin-top: 0.58em; font-size: 0.78em; }\
                .lm-ios-full-player__queue-item { min-height: 4.05em; padding: 0.48em 0.5em; }\
                .lm-ios-full-player__queue-img { width: 2.9em; height: 2.9em; }\
                .lm-ios-full-player__sheet-panel { max-height: 78%; }\
                .lm-ios-player { left: 0.72em; right: 0.72em; top: 75%; padding: 0.92em 0.94em; gap: 0.86em; }\
                .lm-ios-player__title { font-size: 1em; }\
                .lm-ios-player__artist { font-size: 0.86em; }\
                .lm-ios-player__tools { gap: 0.42em; }\
                .lm-ios-player__queue { height: 2.28em; padding: 0 0.74em; font-size: 0.78em; }\
                .lm-ios-player__toolbtn { width: 2.28em; height: 2.28em; }\
                .lm-ios-player__progress { gap: 0.5em; }\
                .lm-ios-player__time { width: 2.9em; font-size: 0.74em; }\
                .lm-ios-player__actions { gap: 0.78em; }\
                .lm-ios-player__btn { width: 2.92em; height: 2.92em; }\
                .lm-ios-player__btn svg { width: 1.08em; height: 1.08em; }\
                .lm-ios-player__btn--primary { width: 3.35em; height: 3.35em; }\
                .lm-ios-player__btn--primary svg { width: 1.22em; height: 1.22em; }\
                .lm-ios-player__toolbtn svg { width: 0.96em; height: 0.96em; }\
            }\
            @media screen and (max-height: 560px) {\
                .lm-ios-full-player__shell { padding-top: calc(env(safe-area-inset-top, 0px) + 0.45em); padding-bottom: calc(env(safe-area-inset-bottom, 0px) + 0.55em); }\
                .lm-ios-full-player__head { margin-bottom: 0.38em; }\
                .lm-ios-full-player__tool { width: 2.18em; height: 2.18em; }\
                .lm-ios-full-player__art { width: min(46vw, 12em, 30vh); margin-bottom: 0.48em; border-radius: 0.82em; }\
                .lm-ios-full-player__title { font-size: 1.02em; -webkit-line-clamp: 1; }\
                .lm-ios-full-player__artist { margin-top: 0.18em; font-size: 0.78em; }\
                .lm-ios-full-player__progress { margin-top: 0.52em; }\
                .lm-ios-full-player__actions { gap: 0.62em; margin-top: 0.58em; }\
                .lm-ios-full-player__btn { width: 2.55em; height: 2.55em; }\
                .lm-ios-full-player__btn--primary { width: 3.05em; height: 3.05em; }\
                .lm-ios-full-player__quick-actions { margin-top: 0.58em; gap: 0.34em; }\
                .lm-ios-full-player__quick { height: 2.75em; font-size: 0.62em; }\
                .lm-ios-full-player__timer-status { margin-top: 0.28em; font-size: 0.68em; }\
                .lm-ios-full-player__stop { min-height: 2.05em; margin-top: 0.34em; font-size: 0.68em; }\
            }\
            </style>\
        ');

        Lampa.Component.add('lampac_music_home', MusicComponent);
        Lampa.Component.add('lampac_music_search', MusicComponent);
        Lampa.Component.add('lampac_music_bookmarks', MusicBookmarksFull);
        Lampa.Component.add('lampac_music_entries', MusicEntriesFull);
        Lampa.Component.add('lampac_music_section', MusicSectionFull);
        Lampa.Component.add('lampac_music_artist', MusicArtistFull);
        Lampa.Component.add('lampac_music_album', MusicAlbumFull);

        if (window.appready) add();
        else {
            Lampa.Listener.follow('app', function (event) {
                if (event.type === 'ready') add();
            });
        }
    }

    createMusic();
})();
