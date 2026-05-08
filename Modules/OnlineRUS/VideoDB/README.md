# VideoDB

Модуль **VideoDB**: загружает **`OnlinesSettings`** в статический **`VideoDB.conf`** для **`Controller`** (раздача **`lite/videodb`** и манифестов). **`IModuleOnline`** в **`ModInit` не реализован** — регистрация в общем списке онлайна выполняется хостом при обращении к маршрутам модуля.

## Интерфейс

**`IModuleLoaded`** — только **`Loaded`/`Dispose`** и **`EventListener.UpdateInitFile`**.

## Глобальный поиск / качество

Нет **`with_search`** и нет **`OnlineApiQuality`** в этом **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`VideoDB`** (`OnlinesSettings`).

По умолчанию: хост **`https://kinogo.media`**, **`streamproxy = true`**, **`httpversion = 2`**, **`priorityBrowser = "http"`**, **`imitationHuman = true`**, заголовки страницы и потока под **`kinogo.media`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/videodb`** | Основная выдача. |
| **`lite/videodb/manifest`**, **`lite/videodb/manifest.m3u8`** | Манифест / HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
