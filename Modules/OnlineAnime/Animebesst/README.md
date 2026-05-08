# Animebesst

Онлайн-источник **Animebesst** (`https://anime1.best`) только для **аниме**-запросов; дополнительно участвует в **spider**-контуре.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Условие (`Invoke` / `Spider`)

И **`Invoke`**, и **`Spider`** возвращают данные **только если** **`args.isanime`**. Иначе **`null`**.

## Глобальный поиск

**`CoreInit.conf.online.with_search.Add("animebesst")`**.

## Конфигурация

Секция в `init.conf`: **`Animebesst`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 135`**, **`httpversion = 2`**, **`rch_access = apk`**, **`stream_access`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "animebesst"`** → **` ~ 720p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/animebesst`** | Основная выдача. |
| **`lite/animebesst/video.m3u8`** | HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
