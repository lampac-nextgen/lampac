# MovPI

Онлайн-источник **MovPI** (`https://moviesapi.club`) для **англоязычной** выдачи: учитываются **`CoreInit.conf.disableEng`** и **Playwright**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Добавляется пункт только если:

- язык **`null`** или **`en`** и **`disableEng == false`**;
- **`args.source`** — **`tmdb`** или **`cub`**, **`args.id`** парсится в **`long > 0`**;
- **`PlaywrightBrowser`** не **`disabled`**.

Пункт: **`movpi`**, **`MovPI`**, суффикс **` (ENG)`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`MovPI`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 1025`**, **`streamproxy = true`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "movpi"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/movpi`** | Основная выдача. |
| **`lite/movpi/video`**, **`lite/movpi/video.m3u8`** | Видео / HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
