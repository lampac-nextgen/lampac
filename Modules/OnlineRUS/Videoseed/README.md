# Videoseed

Онлайн-источник **Videoseed** (`https://videoseed.tv`), **`streamproxy: true`**. В шаблоне по умолчанию **`enable = false`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Требуется **Playwright**: при **`PlaywrightBrowser.Status == PlaywrightStatus.disabled`** возвращается **`null`**. Иначе всегда один пункт с текущим **`conf`**.

## Конфигурация

Секция в `init.conf`: **`Videoseed`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 580`**, **`stream_access`**, **`headers`**, **`headers_stream`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "videoseed"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/videoseed`** | Основная выдача. |
| **`lite/videoseed/video/{*iframe}`** | Вложенные/iframe-сценарии (см. **`Controller.cs`**). |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
