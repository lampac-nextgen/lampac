# AnimeON

Онлайн-источник **AnimeON** (`https://animeon.club`) с пометкой **«(Украинский)»** в **`Invoke`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Только **`args.isanime`**. Пункт: **`new(conf, arg_title: " (Украинский)")`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`AnimeON`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 145`**, **`rch_access`**, **`stream_access`**, **`rchstreamproxy = web`**, **`geo_hide = ["RU", "BY"]`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "animeon"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/animeon`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
