# FanCDN

Онлайн-источник **FanCDN** (`https://fanserial.me`), **`streamproxy: true`**. В шаблоне по умолчанию модуль **выключен**: **`enable = false`** — включите в `init.conf`, если нужен источник.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Запись добавляется при **`args.kinopoisk_id > 0`** и **`args.serial == -1 || args.serial == 0`**.

## Конфигурация

Секция в `init.conf`: **`FanCDN`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 520`**, **`httpversion = 2`**, **`rch_access = apk`**, **`rhub_safety = false`**, **`imitationHuman = true`**, отдельные **`headers`** и **`headers_stream`** для сайта и потока.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "fancdn"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/fancdn`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
