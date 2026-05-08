# Xnxx

Раздел **Sisi**: источник **XNXX** (`https://www.xnxx-ru.com`). Префикс маршрутов **`xnx`**. **18+**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`** — один канал **`xnxx.com`**.

## Конфигурация

Секция в `init.conf`: **`Xnxx`** (`SisiSettings`).

По умолчанию: **`httpversion = 2`**, **`displayindex = 19`**, **`rch_access` / `stream_access`** (`apk`, `cors`, `web`).

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`xnx`** | Основная выдача. |
| **`xnx/vidosik`** | Карточка видео. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
