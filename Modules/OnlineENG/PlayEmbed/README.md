# PlayEmbed

Онлайн-источник **PlayEmbed** (`https://vidora.su`, в комментарии «Omega») для ENG — условия как у **MovPI** (**`disableEng`**, **`tmdb`/`cub`**, Playwright).

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Плагин **`playembed`**, имя **`PlayEmbed`**, суффикс **` (ENG)`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`Playembed`** (`OnlinesSettings`).

По умолчанию: **`enable = false`**, **`displayindex = 1040`**, **`streamproxy = true`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "playembed"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/playembed`** | Основная выдача. |
| **`lite/playembed/video`**, **`lite/playembed/video.m3u8`** | Видео / HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
