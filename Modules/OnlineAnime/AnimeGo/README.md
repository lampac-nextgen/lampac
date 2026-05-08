# AnimeGo

Онлайн-источник **AnimeGo** (`https://animego.me`, **`streamproxy: true`**). В шаблоне **`enable = false`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Условие (`Invoke` / `Spider`)

Только **`args.isanime`**.

## Глобальный поиск

**`with_search.Add("animego")`**.

## Конфигурация

Секция в `init.conf`: **`AnimeGo`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 155`**, **`httpversion = 2`**, **`headers_stream`** с **`origin`/`referer`** на **`aniboom.one`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "animego"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/animego`** | Основная выдача. |
| **`lite/animego/video.m3u8`** | HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
