# KinoUkr

Онлайн **Kinoukr** (`https://kinoukr.com`): локальная база **`data/kinoukr.json`** (при **`lowMemoryMode == false`** загружается в память; таймер обновления раз в **20** минут после первых **5** минут). Украинская подпись для не-аниме контента.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Если **`!args.isanime`** — добавляется **`new(conf, arg_title: " (Украинский)")`**. Для **аниме**-запросов список остаётся **пустым** (источник не предлагается).

## Глобальный поиск

**`with_search.Add("kinoukr")`**.

## Конфигурация

Секция в `init.conf`: **`Kinoukr`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 815`**, **`rch_access`**, **`stream_access`**, **`rchstreamproxy = web`**, **`geo_hide`**, заголовки и **`cookie`** под **`kinoukr.com`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "kinoukr"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/kinoukr`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`data/kinoukr.json`**, **`OnlineApi.cs`**, **`Model.cs`**.
