# SmashyStream

Онлайн-источник **SmashyStream** (`https://player.smashystream.com`) для ENG: те же фильтры, что у **MovPI** / **VidSrc** (**`disableEng`**, **`tmdb`/`cub`**, Playwright).

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

См. **`ModInit`** — **`smashystream`**, **`SmashyStream`**, **` (ENG)`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: ключ **`Smashystream`** соответствует **`ModuleInvoke.Init("Smashystream", …)`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 1030`**, **`streamproxy = true`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "smashystream"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/smashystream`** | Основная выдача. |
| **`lite/smashystream/video.m3u8`** | HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
