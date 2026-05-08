# GetsTV

Онлайн-источник **GetsTV** (`https://getstv.com`). В шаблоне **`enable = false`**, **`rhub_safety = false`**, User-Agent под Smart TV.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Поведение

- **`Invoke`** всегда один пункт **`new(conf)`**.
- **`Spider`** регистрирует **`getstv-search`**.

## Глобальный поиск

В **`ModInit`** нет **`with_search.Add`** — при необходимости общего поиска настройте хост/конфиг отдельно.

## Конфигурация

Секция в `init.conf`: **`GetsTV`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 335`**, **`stream_access = apk,cors,web`**, кастомный **`headers`** (WebOS TV UA).

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "getstv"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`/lite/getstv/bind`** | Привязка (см. **`Controller.cs`**). |
| **`lite/getstv`** | Основная выдача. |
| **`lite/getstv/video.m3u8`** | HLS. |
| **`lite/getstv-search`** | Поиск. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
