# Kinoflix

Онлайн-источник **Kinoflix** (`https://kinoflix.tv`), **`streamproxy: true`**, с грузинской подписью в выдаче.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

- **`args.kinopoisk_id > 0`**
- язык **не** из набора восточно-азиатских одиночных кодов: **`iscn = args.original_language` in `ja`/`ko`/`zh`/`cn`** — при **`iscn == true`** источник **не** добавляется.

Пункт: **`new(conf, arg_title: " (Грузинский)")`**.

## Конфигурация

Секция в `init.conf`: **`Kinoflix`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 900`**, **`rch_access = apk`**, **`stream_access = apk`**, **`rchstreamproxy`**, **`headers_stream`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "kinoflix"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/kinoflix`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
