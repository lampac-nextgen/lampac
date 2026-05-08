# AutoEmbed

Онлайн-источник **AutoEmbed** (`https://player.autoembed.cc`) для ENG — те же фильтры, что **MovPI** (**`disableEng`**, **`tmdb`/`cub`**, Playwright).

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Плагин **`autoembed`**, имя **`AutoEmbed`**, суффикс **` (ENG)`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`Autoembed`** (`OnlinesSettings`).

По умолчанию: **`enable = false`**, **`displayindex = 1035`**, **`streamproxy = true`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "autoembed"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/autoembed`** | Основная выдача. |
| **`lite/autoembed/video`** | Видео. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
