# RgShows

Онлайн-источник **RgShows** (`https://api.rgshows.me`) для ENG: проверяются **`disableEng`** и источник **`tmdb`/`cub`** с **`id > 0`**. В отличие от MovPI/VidSrc, **`Invoke` не проверяет Playwright**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Плагин **`rgshows`**, имя **`RgShows`**, суффикс **` (ENG)`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`Rgshows`** (`OnlinesSettings`).

По умолчанию: **`enable = false`**, **`displayindex = 1050`**, **`streamproxy = true`**, расширенные **`headers`** / **`headers_stream`** под **`rgshows.me`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "rgshows"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/rgshows`** | Основная выдача. |
| **`lite/rgshows/video`** | Видео. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
