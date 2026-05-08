# Alloha

Онлайн-агрегатор **Alloha** (`ModuleConf`): несколько базовых URL и флаг **`reserve`** задаются в **`ModuleInvoke.Init`**. Конфигурацию безопаснее хранить в `init.conf`, не дублируя endpoint’ы в документации.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Поведение

- **`Invoke`** всегда **`new(conf)`**.
- **`Spider`** использует идентификатор **`alloha-search`**.

## Глобальный поиск

**`with_search.Add("alloha")`**.

## Подпись качества

**`OnlineApiQuality`**: для **`e.balanser == "alloha"`** подмешивается **`m4s`** из **`kitconf["Alloha"]`** при наличии → **` ~ 2160p`** или **` ~ 1080p`**.

## Конфигурация

Секция в `init.conf`: **`Alloha`** (`ModuleConf`).

По умолчанию в коде: **`displayindex = 325`**, **`httpversion = 2`**, **`rch_access`**, **`stream_access`**, **`reserve = true`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/alloha`** | Основная выдача. |
| **`lite/alloha/video`**, **`lite/alloha/video.m3u8`** | Видео / HLS. |
| **`lite/alloha-search`** | Поиск. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
