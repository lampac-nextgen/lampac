# Провайдеры контента (Providers)

В Lampac NextGen встроена поддержка множества провайдеров (источников) контента. Они разбиты на категории и располагаются в соответствующих подпапках модуля `Online`.

## 1. VOD (Онлайн кинотеатры - Русскоязычные)
Находятся в `Modules/OnlineRUS` и `Modules/OnlinePaid`.
Некоторые требуют авторизации (токен в `init.conf`).

| Провайдер | Сервис | Маршрут | Примечания |
| --- | --- | --- | --- |
| `Alloha` | Alloha CDN | `/lite/alloha` | |
| `Collaps` | Collaps | `/lite/collaps` | Поддерживает DASH |
| `Filmix` | Filmix | `/lite/filmix` | Требует токен |
| `HDVB` | HDVB | `/lite/hdvb` | |
| `KinoPub` | KinoPub | `/lite/kinopub` | Требует токен |
| `Kinobase` | KinoBase | `/lite/kinobase` | |
| `Kinogo` | Kinogo | `/lite/kinogo` | |
| `PiTor` | PidTor | `/lite/pidtor` | Стриминг прямо через торренты |
| `Rezka` | HDRezka | `/lite/rezka` | |
| `VkMovie` | VK Видео | `/lite/vkmovie` | |

## 2. Аниме
Находятся в `Modules/OnlineAnime`.

| Провайдер | Сервис | Маршрут |
| --- | --- | --- |
| `AniLibria` | AniLibria | `/lite/anilibria` |
| `AniMedia` | AniMedia | `/lite/animedia` |
| `AnimeGo` | AnimeGo | `/lite/animego` |
| `Kodik` | Kodik | `/lite/kodik` |
| `MoonAnime` | MoonAnime | `/lite/moonanime` |

## 3. Англоязычные провайдеры (ENG)
Находятся в `Modules/OnlineENG`.

| Провайдер | Сервис | Маршрут |
| --- | --- | --- |
| `AutoEmbed` | AutoEmbed | `/lite/autoembed` |
| `SmashyStream` | SmashyStream | `/lite/smashystream` |
| `TwoEmbed` | TwoEmbed | `/lite/twoembed` |
| `VidSrc` | VidSrc | `/lite/vidsrc` |

## 4. Украинские CDN (UKR)
Находятся в `Modules/OnlineUKR`.

| Провайдер | Сервис | Маршрут |
| --- | --- | --- |
| `Ashdi` | Ashdi | `/lite/ashdi` |
| `Eneyida` | Eneyida | `/lite/eneyida` |
| `Kinoukr` | KinoUkr | `/lite/kinoukr` |
| `UaKino` | UaKino | `/lite/uakino` |

---

## 5. Контент 18+ (SISI)
Модули жестко закодированных платформ находятся в `Modules/Adult`.

| Провайдер | Маршрут |
| --- | --- |
| `PornHub` | `/phub`, `/phubprem` |
| `Xnxx` | `/xnx` |
| `Xvideos` | `/xds`, `/xdsred` |
| `Xhamster` | `/xmr` |
| `Chaturbate` | `/chu` |
| `Spankbang` | `/sbg` |

## 6. NextHUB (18+)
Дополнительный браузер 18+ сайтов, работающий на основе простых `YAML` файлов парсинга. Эндпоинт `/nexthub`.
Поддерживает десятки сайтов (описания лежат в папке `Modules/NextHUB/sites/`). Включается параметром `NextHUB: true` в секции `sisi` файла конфигурации.
