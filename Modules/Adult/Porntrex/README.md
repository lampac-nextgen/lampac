# Porntrex

Раздел **Sisi**: источник **Porntrex** (`https://www.porntrex.com`). Префикс маршрутов **`ptx`**. **18+**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`** — один канал **`porntrex.com`**.

## Конфигурация

Секция в `init.conf`: **`Porntrex`** (`SisiSettings`).

По умолчанию: **`displayindex = 18`**, **`streamproxy = true`**, **`rch_access` / `stream_access = apk`**, для потока и картинок задан **`referer`** на домен Porntrex.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`ptx`** | Основная выдача. |
| **`ptx/vidosik`** | Карточка видео. |
| **`ptx/strem`** | Потоковый endpoint (см. **`Controller.cs`**). |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
