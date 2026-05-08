# Eporner

Раздел **Sisi**: источник **EPORNER** (`https://www.eporner.com`). Префикс маршрутов **`epr`**. **18+**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`** — один канал **`eporner.com`**.

## Конфигурация

Секция в `init.conf`: **`Eporner`** (`SisiSettings`).

По умолчанию: **`httpversion = 2`**, **`displayindex = 17`**, **`rch_access` / `stream_access`** (`apk`, `cors`), **`rchstreamproxy = web`**, заданы **`headers_image`** под загрузку превью.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`epr`** | Основная выдача. |
| **`epr/vidosik`** | Карточка видео. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
