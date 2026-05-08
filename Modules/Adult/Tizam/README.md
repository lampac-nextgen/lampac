# Tizam

Раздел **Sisi**: источник **TIZAM** (`https://tv4.tizam.org`), канал в клиенте **`tizam.pw`**, префикс маршрутов **`tizam`**. Контент может быть **18+** в зависимости от каталога сайта.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`** — всегда один пункт в **`Invoke`**.

## Конфигурация

Секция в `init.conf`: **`Tizam`** (`SisiSettings`).

По умолчанию в коде: **`displayindex = 21`**, **`rch_access` / `stream_access`** (`apk`, `cors`), **`rchstreamproxy = web`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`tizam`** | Основная выдача. |
| **`tizam/vidosik`** | Карточка/поток видео. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
