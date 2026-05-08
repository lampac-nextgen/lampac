# KinoPub

Онлайн **КиноПаб** (`ModuleConf`): API по умолчанию **`https://api.srvkp.com`** (в комментариях перечислены альтернативные базы для apk/TV). **`with_search.Add("kinopub")`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Поведение

**`Invoke`** и **`Spider`** всегда возвращают **`new(conf)`**.

## Конфигурация

Секция в `init.conf`: **`KinoPub`** (`ModuleConf`).

По умолчанию: **`displayindex = 320`**, **`httpversion = 2`**, **`rhub_safety = false`**, **`filetype`** (**`hls`** / **`hls4`** / **`mp4`**), **`stream_access`**, заголовки «навигации» для запросов.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "kinopub"`** → **` ~ 2160p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/kinopubpro`** | Профиль Pro (см. контроллер). |
| **`lite/kinopub`** | Основная выдача. |
| **`lite/kinopub/subtitles.json`** | Субтитры. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
