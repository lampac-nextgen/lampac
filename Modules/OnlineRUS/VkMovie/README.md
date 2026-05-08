# VkMovie

Онлайн-источник **VK Видео**: API **`https://api.vkvideo.ru`**, в интерфейсе — плагин **`vkmovie`**, название **`VK Видео`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Источник добавляется, если **`args.serial == -1`** или **`args.serial == 0`** (не режим конкретного сезона сериала в этом контексте). Иначе **`null`**.

## Глобальный поиск

**`CoreInit.conf.online.with_search.Add("vkmovie")`**.

## Конфигурация

Секция в `init.conf`: **`VkMovie`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 570`**, **`streamproxy = true`**, **`headers`** с **`origin`/`referer`** на **`vkvideo.ru`**.

## Подпись качества

**`OnlineApiQuality`**: для **`e.balanser == "vkmovie"`** → **` ~ 2160p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/vkmovie`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
