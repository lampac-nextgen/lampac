# Ebalovo

Раздел **Sisi**: источник **EBALOVO** (`https://www.ebalovo.pro`). Внутренний идентификатор канала **`ebalovo.porn`**, префикс маршрутов **`elo`**. **18+**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`** — один пункт в **`Invoke`**.

## Конфигурация

Секция в `init.conf`: **`Ebalovo`** (`SisiSettings`).

По умолчанию: **`displayindex = 14`**, **`rch_access = apk`**, **`stream_access = apk,cors`**, **`rchstreamproxy = web`**, **`headers`** и **`headers_stream`** с типичными fetch-заголовками для страниц и видео.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`elo`** | Основная выдача. |
| **`elo/vidosik`** | Карточка видео. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
