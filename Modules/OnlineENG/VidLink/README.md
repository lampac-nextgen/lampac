# VidLink

Онлайн-источник **VidLink** (`https://vidlink.pro`) для ENG — фильтры **`disableEng`**, **`tmdb`/`cub`**, Playwright (как **MovPI**).

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Плагин **`vidlink`**, имя **`VidLink`**, суффикс **` (ENG)`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`VidLink`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 1015`**, **`streamproxy = true`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "vidlink"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/vidlink`** | Основная выдача. |
| **`lite/vidlink/video`** | Видео. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
