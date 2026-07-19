# Архитектура клиента (plugin.js)

Этот документ — карта для разработчика, открывающего [plugin.js](../plugin.js) впервые. Серверная сторона описана в [README.md](../README.md);

## Почему монолит

Весь клиент — один файл, который сервер отдаёт как `/music.js` (с подстановкой `{localhost}` и `{client_debug_enabled}` при отдаче). Это осознанное решение: файл монтируется в контейнер живьём, правка не требует пересборки, а Lampa загружает плагин одним запросом. Плата — размер (~14 тыс. строк), поэтому навигация построена на двух уровнях маркеров:

- `// ===== ИМЯ =====` — 14 крупных секций (список в шапке файла);
- `// --- имя ---` — подсекции внутри больших секций.

Шапка файла содержит карту секций, точки входа, легенду именований и typedef основных DTO — начинать чтение оттуда.

## Поток данных

### Главный экран

```
/music/home ──> MUSIC_HOME_CACHE (снапшот секций)
                │
                ├── локальные полки из Lampa.Storage: недавние
                │   поиски/альбомы/артисты, закладки, «Мои плейлисты»
                └── discovery-полки сервера (Apple Music, VK Top, SoundCloud)

карточка = entry (mapTrackCard / mapAlbumCard / mapArtistCard / mapPlaylistCard)
«Ещё» ──> /music/section?id=... (полный список, пагинация)
```

Recent-полки живут в `Lampa.Storage` и синхронизируются между экранами событием `MUSIC_RECENT_EVENT` — компоненты подписываются через `bindRecentListener` и обновляют только затронутую секцию, не перерисовывая home целиком.

### Запуск трека

```
тап по треку
  └─> warmupStandaloneIosGesture()      — синхронно В ЖЕСТЕ (только player=ios)
  └─> /music/play?...                   — резолв источника на сервере
        └─> { track, selected_match, sources[] }
  └─> pickPlaybackSource(sources)       — m4a-first для ios-пути
  └─> buildResolvedPlayback()           — playback-объект
        ├─ player=ios   → startStandaloneIosAudioPlayback (свой <audio>)
        └─ player=inner → Lampa.Player.play (штатный плеер)
```

`PLAY_PREFETCH_CACHE` префетчит `/music/play` соседних треков очереди (TTL 10 минут), чтобы next/prev не ждали резолва. Все stream-URL ведут на серверный relay `/music/stream` — клиент не ходит на CDN напрямую.

## Два режима воспроизведения

| | `player=ios` (standalone) | `player=inner` (embedded) |
| --- | --- | --- |
| Media owner | собственный `<audio>` модуля | видеоэлемент `Lampa.PlayerVideo` |
| UI | мини-бар + фулл-плеер (свайпы, шиты) | штатный интерфейс Lampa |
| Lock screen | полный: play/pause/prev/next/скраббер | play/pause/prev/next, без скраббера |
| Состояние | `MUSIC_IOS_AUDIO` | `MUSIC_EMBEDDED_IOS` |
| Код | секции QUEUE / IOS PLAYBACK, IOS FULL PLAYER | подсекция embedded в IOS FULL PLAYER + LAMPA PLAYER BRIDGE |

Режимы нельзя смешивать: у них разные media owner и разная логика Media Session. Почему в embedded-режиме нет скраббера — разобрано в [ios-embedded-lockscreen-seek.md](./ios-embedded-lockscreen-seek.md).

## Жизненный цикл standalone iOS-плеера

Это самая хрупкая зона модуля. Каждый шаг ниже существует из-за конкретного поведения iOS/WebKit; подробности — в «почему»-комментариях у функций.

```
тап «играть» (жестовый контекст!)
  ├─ warmupStandaloneIosGesture()   тихий play() выдаёт элементу user-gesture
  │                                 кредит — иначе iOS игнорирует lock-screen команды
  ├─ armStandaloneIosMediaSessionHandlers()
  │                                 все обработчики ДО первого play(): iOS фиксирует
  │                                 возможности Now Playing при создании сессии
  └─ startStandaloneIosKeepAlive()  Web Audio-контекст держит аудио-сессию,
                                    чтобы resume работал на заблокированном экране

играет трек
  ├─ timeupdate → обновление бара/фулл-плеера (UI-ключи против лишних перерисовок)
  ├─ maybeSyntheticStandaloneIosEnded()  ручной автопереход: WebKit парсит
  │                                      YouTube fMP4 с удвоенной длительностью,
  │                                      штатный 'ended' не стреляет
  └─ scheduleStandaloneIosLockKick()     pause()+play() через 400ms после
                                         блокировки — включает скраббер

команда с lock screen (play/pause/seekto)
  ├─ pause-as-play: повторный pause на стоящем треке при живом keep-alive = play
  └─ watchStandaloneIosPlayProgress()    watchdog: резолв play() не доказывает
        ├─ attempt 0: pause()+play()     звук — проверяем прогресс currentTime
        └─ attempt 1: recoverStandaloneIosPlayback (переприцепка src, страховка)

stop / конец плейлиста
  └─ stopStandaloneIosKeepAlive(), снапшот очереди очищается
```

## Снапшот очереди (v2)

Очередь переживает перезапуск клиента через два ключа `Lampa.Storage` — разнесены, чтобы частый тик не пересобирал всю очередь (это грело телефон):

- `queue_blob_v2` — окно ≤500 треков + режимы (repeat/shuffle/провайдер) + `snapshotId`; пишется **по событиям** (смена трека/состава, toggles, приближение к краю окна);
- `queue_position_v2` — крошечная запись `snapshotId`/`trackId`/`currentTime`; пишется каждые 4 секунды.

Связка через `snapshotId`: позиция от чужого поколения блоба игнорируется. TTL 14 дней считается по свежести позиции. Restore читает v2 с fallback на legacy-ключ v1. `snapshotId` также служит маркером поколения очереди для радио (один запрос автоподборки на поколение).

## Радио на клиенте

Два входа, одна настройка (`radio_autoplay_enabled`): тумблер в фильтре и чип «Подборка» в фулл-плеере. Автоподборка триггерится, когда играет предпоследний трек **по порядку воспроизведения** (при shuffle — по `standaloneIosOrder`, не по физическому индексу); guard — один POST `/music/radio` на поколение очереди + `pending`-флаг. Клиент шлёт seed-треки и `exclude` всей очереди (сервер её не видит). Добавленные треки несут флаг `auto_radio` (едет в снапшот), в шите очереди перед первым из них — разделитель «Дальше автоподборка». «Радио от трека» (`startRadioFromTrack`) строит **новую** очередь: сид первым, волна следом; при неудаче текущая очередь не трогается. `repeat=all`, restored-очередь до play и внешние m3u-плееры автоподборку отключают.

## Анти-гонки

Асинхронные колбэки защищены токенами (`prepareToken`, `playWatchToken`, `switchToken`): перед применением результата колбэк сверяет токен с текущим и молча выходит, если операция устарела (пользователь переключил трек, начался новый watch). Новых async-путей это правило тоже касается.

## Отладка

- `client_debug_enabled: true` в конфиге модуля → `traceStandaloneIosAudio` и heat-метрики шлют события на `/music/clientlog`, сервер пишет их в лог. Это единственный способ отлаживать lock screen на реальном iPhone. После отладки — выключить.
- Имена трейсов стабильные (`play-nudge-ok`, `keepalive-start`, `media-session-pause-as-play`…) — по ним можно грепать историю в логах контейнера.

## Глоссарий

| Термин | Значение |
| --- | --- |
| entry | карточка полки: `{ type, id, title, subtitle, badge, image, raw }` |
| section | полка home или полный экран списка (`/music/section`) |
| match | кандидат audio-источника для трека (`MusicAudioMatch`) |
| pinned | ручной выбор источника; авторитетнее любых эвристик, живёт в SQLite |
| warmup | тихий `play()` синхронно в жесте — выдаёт user-gesture кредит |
| keep-alive | беззвучный Web Audio-контекст, держит аудио-сессию во время паузы |
| nudge | цикл `pause()+play()` — единственное, что размораживает clock элемента |
| lock-kick | rate-переход после блокировки, включающий скраббер на lock screen |
| synthetic-ended | ручной автопереход, когда WebKit завысил длительность и `'ended'` не стреляет |
| sane-duration | выбор между длительностью стрима и метаданных при диком расхождении |
| snapshot / restore | сохранение очереди в Storage (blob + position, v2) и восстановление после перезапуска |
| snapshotId | поколение очереди: связывает blob с позицией, гейтит радио-запросы |
| auto_radio | флаг трека, добавленного автоподборкой; рисует разделитель в очереди |
| heat-метрики | счётчики горячих путей, уходят на `/music/clientlog` при отладке |

## Правила изменений

1. После правки `plugin.js` поднять revision в `lampac-docker/plugins/override/lampainit-invc.js`, иначе клиенты держат кэшированный JS.
2. Всё, что касается standalone iOS-плеера, тестировать на реальном iPhone с заблокированным экраном — симулятор и десктоп-браузер не воспроизводят поведение аудио-сессий iOS.
3. Не удалять «бессмысленные» на вид pause/play-циклы и таймауты — почти каждый из них компенсирует конкретное поведение iOS (см. комментарии). История экспериментов: [ios-embedded-lockscreen-seek.md](./ios-embedded-lockscreen-seek.md).
