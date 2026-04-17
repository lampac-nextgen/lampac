# Провайдеры контента (Providers)

В Lampac NextGen встроена поддержка множества провайдеров (источников) контента. Они разбиты на категории и располагаются в соответствующих подпапках модуля `Online`.

## 1. VOD (Онлайн кинотеатры - Русскоязычные)
Находятся в `Modules/OnlineRUS` и `Modules/OnlinePaid`.
Некоторые требуют авторизации (токен в `init.conf`).

| Провайдер | Сервис | Маршрут | Примечания |
| --- | --- | --- | --- |
| `Alloha` | Alloha CDN | `/lite/alloha` | |
| `CDNvideohub` | CDN VideoHub | `/lite/cdnvideohub` | |
| `Collaps` | Collaps | `/lite/collaps` | Поддерживает DASH |
| `FanCDN` | FanCDN | `/lite/fancdn` | |
| `Filmix` | Filmix | `/lite/filmix` | Требует токен |
| `FlixCDN` | FlixCDN | `/lite/flixcdn` | |
| `GetsTV` | GetsTV | `/lite/getstv` | |
| `HDVB` | HDVB | `/lite/hdvb` | |
| `IptvOnline`| IPTV Online | `/lite/iptvonline` | |
| `KinoPub` | KinoPub | `/lite/kinopub` | Требует токен |
| `Kinobase` | KinoBase | `/lite/kinobase` | |
| `Kinogo` | Kinogo | `/lite/kinogo` | |
| `Kinotochka`| Kinotochka | `/lite/kinotochka` | |
| `LeProduction`| Le Production | `/lite/leproduction` | |
| `Mirage` | Mirage CDN | `/lite/mirage` | |
| `PiTor` | PidTor | `/lite/pidtor` | Стриминг прямо через торренты |
| `PizdatoeHD`| PizdatoeHD | `/lite/pizdatoehd` | |
| `Rezka` | HDRezka | `/lite/rezka` | |
| `RutubeMovie`| Rutube Видео | `/lite/rutubemovie`| |
| `Spectre` | Spectre | `/lite/spectre` | |
| `VeoVeo` | VeoVeo | `/lite/veoveo` | |
| `Vibix` | Vibix | `/lite/vibix` | |
| `VideoDB` | VideoDB | `/lite/videodb` | |
| `Videoseed` | Videoseed | `/lite/videoseed` | |
| `VkMovie` | VK Видео | `/lite/vkmovie` | |
| `VoKino` | VoKino | `/lite/vokino` | |
| `Zetflix` | Zetflix | `/lite/zetflix` | |
| `ZetflixDB` | ZetflixDB | `/lite/zetflixdb` | |
| `iRemux` | iRemux | `/lite/iremux` | |


## 2. Грузинские CDN (GEO)
Находятся в `Modules/OnlineGEO`.

| Провайдер | Сервис | Маршрут |
| --- | --- | --- |
| `AsiaGe` | AsiaGe | `/lite/asiage` |
| `Geosaitebi`| Geosaitebi | `/lite/geosaitebi`|
| `Kinoflix` | Kinoflix | `/lite/kinoflix` |


## 3. Аниме
Находятся в `Modules/OnlineAnime`.

| Провайдер | Сервис | Маршрут |
| --- | --- | --- |
| `AniLiberty`| AniLiberty | `/lite/aniliberty`|
| `AniLibria` | AniLibria | `/lite/anilibria` |
| `AniMedia` | AniMedia | `/lite/animedia` |
| `AnimeGo` | AnimeGo | `/lite/animego` |
| `AnimeLib` | AnimeLib | `/lite/animelib` |
| `AnimeON` | AnimeON | `/lite/animeon` |
| `Animebesst`| Animebesst | `/lite/animebesst`|
| `Animevost` | Animevost | `/lite/animevost` |
| `Dreamerscast`| Dreamerscast | `/lite/dreamerscast`|
| `Kodik` | Kodik | `/lite/kodik` |
| `Mikai` | Mikai | `/lite/mikai` |
| `MoonAnime` | MoonAnime | `/lite/moonanime` |

## 4. Англоязычные провайдеры (ENG)
Находятся в `Modules/OnlineENG`.

| Провайдер | Сервис | Маршрут |
| --- | --- | --- |
| `AutoEmbed` | AutoEmbed | `/lite/autoembed` |
| `HydraFlix` | HydraFlix | `/lite/hydraflix` |
| `MovPI` | MovPI | `/lite/movpi` |
| `PlayEmbed` | PlayEmbed | `/lite/playembed` |
| `RgShows` | RgShows | `/lite/rgshows` |
| `SmashyStream`| SmashyStream | `/lite/smashystream`|
| `TwoEmbed` | TwoEmbed | `/lite/twoembed` |
| `VidLink` | VidLink | `/lite/vidlink` |
| `VidSrc` | VidSrc | `/lite/vidsrc` |
| `Videasy` | Videasy | `/lite/videasy` |

## 5. Украинские CDN (UKR)
Находятся в `Modules/OnlineUKR`.

| Провайдер | Сервис | Маршрут |
| --- | --- | --- |
| `Ashdi` | Ashdi | `/lite/ashdi` |
| `BamBoo` | BamBoo | `/lite/bamboo` |
| `Eneyida` | Eneyida | `/lite/eneyida` |
| `HdvbUA` | HdvbUA | `/lite/hdvbua` |
| `Kinoukr` | KinoUkr | `/lite/kinoukr` |
| `Tortuga` | Tortuga | `/lite/tortuga` |
| `UaKino` | UaKino | `/lite/uakino` |

---

## 6. Контент 18+ (SISI)
Модули жестко закодированных платформ находятся в `Modules/Adult`.

| Провайдер | Маршрут |
| --- | --- |
| `BongaCams` | `/bgs` |
| `Chaturbate`| `/chu` |
| `Ebalovo` | `/elo` |
| `Eporner` | `/epr` |
| `HQporner` | `/hqr` |
| `PornHub` | `/phub`, `/phubprem` |
| `Porntrex` | `/ptx` |
| `Runetki` | `/runetki` |
| `Spankbang` | `/sbg` |
| `Tizam` | `/tizam` |
| `Xhamster` | `/xmr` |
| `Xnxx` | `/xnx` |
| `Xvideos` | `/xds`, `/xdsred` |

## 6. NextHUB (18+)
Дополнительный браузер 18+ сайтов, работающий на основе простых `YAML` файлов парсинга. Эндпоинт `/nexthub`.
Поддерживает десятки сайтов (описания лежат в папке `Modules/NextHUB/sites/`). Включается параметром `NextHUB: true` в секции `sisi` файла конфигурации.
