# Rezka

> [!WARNING]  
> С 09.04.2026 HDrezka вернула политику полного запрета сторонних приложений для Premium аккаунтов, используйте модуль на свой страх и риск, возможна блокировки аккаунта.

Онлайн **Rezka** (`RezkaSettings`, база **`https://rezka.ag`**). В шаблоне **`enable = false`**. **`with_search.Add("rezka")`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Поведение

**`Invoke`** и **`Spider`** всегда **`new(conf)`**.

## Конфигурация

Секция в `init.conf`: **`Rezka`** (`RezkaSettings`).

По умолчанию: **`displayindex = 330`**, **`streamproxy = true`**, **`stream_access`**, **`ajax = false`**, **`reserve = true`**, **`hls = true`**, **`scheme = http`**, **`headers = Http.defaultUaHeaders`**.

## Подпись качества

**`OnlineApiQuality`**: для **`rezka`** при наличии **`kitconf["Rezka"]`** подмешивается **`premium`** → **`~ 2160p`** или **`~ 720p`**; иначе зависит от **`conf.premium`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/rezka`** | Основная выдача. |
| **`lite/rezka/serial`** | Сериалы. |
| **`lite/rezka/movie`**, **`lite/rezka/movie.m3u8`** | Фильм / HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
