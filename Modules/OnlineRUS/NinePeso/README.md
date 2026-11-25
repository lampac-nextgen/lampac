# NinePeso

Онлайн-источник **NinePeso**: фильмы и сериалы через API **`https://videobalanser.cc`**, **`streamproxy = true`**. Поддерживает несколько озвучек и дополнительные аудиодорожки.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Поведение

- **`Invoke`** — при наличии названия или Кинопоиск/IMDb ID.
- Live-поиск по ID или названию на сайте `tartugi.name`, без локальной базы.
- Глобальный поиск: **`with_search.Add("ninepeso")`**.
- Качество фильмов и серий — разрешение из `max_quality`.

## Конфигурация

Секция в `init.conf`: **`NinePeso`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 550`**, **`httpversion = 2`**, **`httptimeout = 30`**, **`rch_access = "apk"`**.

Адрес сайта задаётся через `host` (поддомен `tartugi.name` может меняться), адрес API — через `apihost`. При включённом RCH серверный fallback доступен через `rhub_fallback = true`.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "ninepeso"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/ninepeso`** | Основная выдача. |
| **`lite/ninepeso-spider`** | Поиск по названию. |
| **`lite/ninepeso/video.m3u8`** | HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`Models/`**, **`Services/LiveSearch.cs`**.
