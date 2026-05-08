# VeoVeo

Онлайн-источник **VeoVeo** (`https://api.rstprgapipt.com`): локальный кеш каталога **`data/veoveo.json`** (при **`lowMemoryMode == false`** загружается в память при старте, при **`Dispose`** очищается).

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Поведение

- **`Invoke`** всегда **`new(conf)`**.
- **`Spider`** с идентификатором **`veoveo-spider`**.

## Глобальный поиск

**`with_search.Add("veoveo")`**.

## Конфигурация

Секция в `init.conf`: **`VeoVeo`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 550`**, **`httpversion = 2`**, **`stream_access`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "veoveo"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/veoveo`** | Основная выдача. |
| **`lite/veoveo/parsed.m3u8`** | Распарсенный HLS. |
| **`lite/veoveo-spider`** | Spider. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`data/veoveo.json`**, **`OnlineApi.cs`**, **`Model.cs`**.
