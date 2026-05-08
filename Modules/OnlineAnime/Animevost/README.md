# Animevost

Онлайн-источник **AnimeVost** (`https://animevost.org`) для аниме; есть поддержка **spider**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Условие (`Invoke` / `Spider`)

Только при **`args.isanime`**; иначе **`null`**.

## Глобальный поиск

**`CoreInit.conf.online.with_search.Add("animevost")`**.

## Конфигурация

Секция в `init.conf`: **`Animevost`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 125`**, **`httptimeout = 10`**, **`rch_access`**, **`stream_access`**, **`rchstreamproxy = web`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "animevost"`** → **` ~ 720p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/animevost`** | Основная выдача. |
| **`lite/animevost/video`** | Видео / поток. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
