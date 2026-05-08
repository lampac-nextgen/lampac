# FlixCDN

Онлайн-источник **FlixCDN**: два базовых URL в **`OnlinesSettings`** — плеер **`https://player0.flixcdn.space`**, API **`https://api0.flixcdn.biz/api`**, **`streamproxy: true`**. По умолчанию в шаблоне **`enable = false`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Добавляется пункт, если **`args.kinopoisk_id > 0`** и **`!args.isanime`** (не аниме-запрос).

## Конфигурация

Секция в `init.conf`: **`FlixCDN`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 525`**, **`httpversion = 1`**, **`rch_access`**, **`stream_access`**, **`headers_stream`** под домен плеера.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "flixcdn"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/flixcdn`** | Основная выдача. |
| **`lite/flixcdn/stream`** | Поток (см. **`Controller.cs`**). |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
