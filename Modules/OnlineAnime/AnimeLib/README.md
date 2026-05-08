# AnimeLib

Онлайн-источник **AnimeLib**: API **`https://hapi.hentaicdn.org`**, **`streamproxy: true`**, **`stream_access = apk`**. В шаблоне **`enable = false`**, **`rhub_safety = false`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Условие (`Invoke` / `Spider`)

Только **`args.isanime`**.

## Глобальный поиск

**`with_search.Add("animelib")`**.

## Конфигурация

Секция в `init.conf`: **`AnimeLib`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 115`**, **`httpversion = 2`**, **`headers`** и **`headers_stream`** с **`referer`** на **`anilib.me`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "animelib"`** → **` ~ 2160p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/animelib`** | Основная выдача. |
| **`lite/animelib/video`** | Видео. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
