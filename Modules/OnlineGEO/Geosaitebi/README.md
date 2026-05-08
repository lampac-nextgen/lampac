# Geosaitebi

Онлайн-источник **Geosaitebi** (`https://geosaitebi.tv`), **`streamproxy: true`**, акцент на грузинской подписи в списке.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Добавляется **`new(conf, arg_title: " (Грузинский)")`**, если **`args.serial == -1`** или **`args.serial == 0`**.

## Конфигурация

Секция в `init.conf`: **`Geosaitebi`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 905`**, **`rch_access`**, **`stream_access = apk`**, **`rchstreamproxy`**, **`headers_stream`** под домен **`geosaitebi.tv`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "geosaitebi"`** → **` ~ 720p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/geosaitebi`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
