# TwoEmbed

Онлайн-источник **2Embed** / **EmbedSu** (`https://embed.su`): для ENG; вместо **`PlaywrightBrowser`** проверяется **`Firefox.Status`** (см. **`ModInit`**).

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Как у других ENG-модулей: **`disableEng`**, **`tmdb`/`cub`**, **`id > 0`**, плюс **`Firefox`** не **`disabled`**.

Плагин **`twoembed`**, имя **`2Embed`**, суффикс **` (ENG)`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`Twoembed`** (`OnlinesSettings` — ключ **`ModuleInvoke`** **`Twoembed`**).

По умолчанию: **`enable = false`**, **`displayindex = 1045`**, **`streamproxy = true`**, **`headers_stream`** под **`embed.su`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "twoembed"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/twoembed`** | Основная выдача. |
| **`lite/twoembed/video`**, **`lite/twoembed/video.m3u8`** | Видео / HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
