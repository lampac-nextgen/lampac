# HQporner

Раздел **Sisi**: источник **HQporner** (`https://m.hqporner.com`). Префикс маршрутов **`hqr`**. **18+**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`** — один канал **`hqporner.com`**.

## Конфигурация

Секция в `init.conf`: **`HQporner`** (`SisiSettings`).

По умолчанию: **`displayindex = 15`**, **`rch_access` / `stream_access`** (`apk`, `cors`, `web`), **`geostreamproxy = ["ALL"]`**, заголовки **`referer`** для страниц и изображений.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`hqr`** | Основная выдача. |
| **`hqr/vidosik`** | Карточка видео. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
