# CDNvideohub

Онлайн-источник **CDNvideohub**: плеер/API **`https://plapi.cdnvideohub.com`**, **`streamproxy: true`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

В список попадает запись **только при** **`args.kinopoisk_id > 0`**. Используется именованный элемент: **`new(conf, "cdnvideohub", "VideoHUB")`** — см. **`ModInit`** для точной связки плагина и отображаемого имени.

## Конфигурация

Секция в `init.conf`: **`CDNvideohub`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 540`**, **`httpversion = 2`**, **`rch_access` / `stream_access`**, заголовки с **`referer`** на **`https://hdkino.pub/`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "cdnvideohub"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/cdnvideohub`** | Основная выдача. |
| **`lite/cdnvideohub/video.m3u8`** | HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`Model.cs`**, **`OnlineApi.cs`** — по **`manifest.json`**.
