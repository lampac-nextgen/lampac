# Vibix

Онлайн-источник **Vibix** (`https://vibix.org`): требуется **Playwright**; путь к модулю сохраняется в **`Vibix.path`** для контроллера.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

При **`PlaywrightBrowser.Status == PlaywrightStatus.disabled`** возвращается **`null`**. Иначе один пункт **`new(conf)`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`Vibix`** (`OnlinesSettings`).

В комментарии к коду: документация API **`https://vibix.org/api/external/documentation`**, iframe-пример **`https://coldfilm.ink`**.

По умолчанию: **`displayindex = 585`**, **`streamproxy = true`**, **`httpversion = 2`**, **`headers = Http.defaultFullHeaders`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "vibix"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/vibix`** | Основная выдача. |
| **`lite/vibix/video.m3u8`** | HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
