# AniMedia

Онлайн-источник **AniMedia** (`https://amd.online`) для аниме-запросов.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Только при **`args.isanime`**. Пункт: плагин **`animedia`**, имя **`AniMedia`**.

## Глобальный поиск

**`with_search.Add("animedia")`**.

## Конфигурация

Секция в `init.conf`: **`AniMedia`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 120`** (остальные поля — по дефолтам **`OnlinesSettings`** в типе).

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "animedia"`** → **` ~ 720p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/animedia`** | Основная выдача. |
| **`lite/animedia/video.m3u8`** | HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
