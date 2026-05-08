# Kinotochka

Онлайн-источник **Kinotochka**: API/плеер по умолчанию **`https://kinovibe.vip`**. В **`Invoke`** всегда возвращается один **`ModuleOnlineItem`** с текущим **`OnlinesSettings`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Глобальный поиск

При **`Loaded`**: **`CoreInit.conf.online.with_search.Add("kinotochka")`** — балансер **`kinotochka`** участвует в общем поиске онлайна.

## Подпись качества

**`EventListener.OnlineApiQuality`**: для **`e.balanser == "kinotochka"`** возвращается подпись **` ~ 720p`**.

## Конфигурация

Секция в `init.conf`: **`Kinotochka`** (`OnlinesSettings`).

Шаблон в коде: **`displayindex = 590`**, **`httpversion = 2`**, **`rch_access`**, **`stream_access`**, **`rchstreamproxy = web`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/kinotochka`** | Основная выдача (см. **`Controller.cs`**). |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, при наличии — **`OnlineApi.cs`**, **`Model.cs`** (см. **`manifest.json`**).
