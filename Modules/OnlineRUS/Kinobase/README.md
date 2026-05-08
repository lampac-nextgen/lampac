# Kinobase

Онлайн **Kinobase** (`https://kinobase.org`): **`ModuleConf`** с HDR (**`hdr: true`** в конструкторе). Требуется **Playwright** — при **`PlaywrightBrowser.disabled`** **`Invoke`**/**`Spider`** возвращают **`null`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Особенности загрузки

В **`Loaded`** читаются **`players/uppod.js`** и **`players/playerjs.js`** в статические строки **`uppod`** и **`playerjs`** для встраиваемых плееров.

## Глобальный поиск

**`with_search.Add("kinobase")`**.

## Конфигурация

Секция в `init.conf`: **`Kinobase`** (`ModuleConf`).

По умолчанию: **`displayindex = 505`**, **`httpversion = 2`**, **`stream_access`**, **`geostreamproxy = ["ALL"]`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "kinobase"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/kinobase`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`players/*.js`**, **`OnlineApi.cs`**, **`Model.cs`**.
