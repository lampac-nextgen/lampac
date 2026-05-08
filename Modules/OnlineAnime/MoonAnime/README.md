# MoonAnime

Онлайн-источник **MoonAnime** (`https://api.moonanime.art`): требуется **аниме**-запрос и активный **Playwright**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Условие (`Invoke` / `Spider`)

Возвращает данные только если **`args.isanime`** и **`PlaywrightBrowser.Status != disabled`**. Иначе **`null`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`MoonAnime`** (`OnlinesSettings`).

В коде задан **`token`** для API — задайте свой в **`init.conf`**, не полагайтесь на значение из репозитория.

По умолчанию: **`displayindex = 140`**, **`stream_access = apk`**, **`rchstreamproxy = web,cors`**, **`geo_hide = ["RU", "BY"]`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "moonanime"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/moonanime`** | Основная выдача. |
| **`lite/moonanime/video`**, **`lite/moonanime/video.m3u8`** | Видео / HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
