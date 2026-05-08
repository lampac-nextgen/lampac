# BamBoo

Онлайн-источник **BamBoo** (`https://bambooua.com`): украинская обвязка для выбранных исходных языков.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Если задан **`args.original_language`**, берётся первый сегмент до **`|`**. При значении **`ko`**, **`zh`**, **`cn`**, **`th`**, **`vi`**, **`tl`** добавляется **`new(conf, arg_title: " (Украинский)")`**. Иначе список пустой.

## Конфигурация

Секция в `init.conf`: **`BamBoo`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 815`**, **`rch_access`**, **`stream_access`**, **`rchstreamproxy = web`**, **`geo_hide = ["RU", "BY"]`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "bamboo"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/bamboo`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
