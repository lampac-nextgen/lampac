# HDVB

Онлайн-источник **HDVB**: два базовых URL в **`OnlinesSettings`** (видео-хост и API), **`streamproxy = true`**, токен API задаётся в **`ModuleInvoke.Init`** — переопределите через **`init.conf`**, не коммитьте продакшен-секреты в репозиторий.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Поведение

- **`Invoke`** всегда **`new(conf)`**.
- **`Spider`** использует **`hdvb-search`**.

## Глобальный поиск

**`with_search.Add("hdvb")`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "hdvb"`** → **` ~ 1080p`**.

## Конфигурация

Секция в `init.conf`: **`HDVB`** (`OnlinesSettings`). Поля **`referer`** / **`origin`** в шаблоне завязаны на текущие дефолтные домены в коде.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/hdvb`** | Основная выдача. |
| **`lite/hdvb/video`**, **`lite/hdvb/video.m3u8`** | Фильм / HLS. |
| **`lite/hdvb/serial`**, **`lite/hdvb/serial.m3u8`** | Сериалы. |
| **`lite/hdvb-search`** | Поиск. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
