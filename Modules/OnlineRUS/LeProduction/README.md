# LeProduction

Онлайн-источник **Le-Production** (`https://www.le-production.tv`).

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

В выдачу попадает запись, если **`!args.isanime`** (для аниме-запросов источник не предлагается).

## Конфигурация

Секция в `init.conf`: **`LeProduction`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 545`**, **`rch_access` / `stream_access`**, **`rchstreamproxy = web`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "leproduction"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/leproduction`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
