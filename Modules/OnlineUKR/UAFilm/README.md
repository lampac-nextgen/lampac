# UAFilm

Онлайн-источник **UAFilm** (`https://uafilm.me`), пометка **«(Украинский)»** в списке.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

При **`args.isanime`** возвращается **`null`**. Иначе один пункт **`new(conf, arg_title: " (Украинский)")`**.

## Конфигурация

Секция в `init.conf`: **`UAFilm`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 830`**, **`streamproxy = true`**, **`headers`** с **`referer`** на домен **`uafilm.me`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "uafilm"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/uafilm`** | Основная выдача. |
| **`lite/uafilm/video.m3u8`** | HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
