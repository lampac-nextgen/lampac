# HydraFlix

Онлайн-источник **HydraFlix**: хост в конфиге **`https://vidfast.pro`** (в комментарии также **`hydraflix.vip`**). ENG-условия как у **MovPI**, плюс **`priorityBrowser = "firefox"`** в шаблоне.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

**`disableEng`**, **`tmdb`/`cub`**, **`id > 0`**, **`PlaywrightBrowser`** активен. Плагин **`hydraflix`**, имя **`HydraFlix`**, **` (ENG)`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`Hydraflix`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 1000`**, **`streamproxy = true`**, **`priorityBrowser = firefox`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "hydraflix"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/hydraflix`** | Основная выдача. |
| **`lite/hydraflix/video`**, **`lite/hydraflix/video.mpd`**, **`lite/hydraflix/video.m3u8`** | DASH / HLS / видео. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
