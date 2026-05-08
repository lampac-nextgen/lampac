# RutubeMovie

Онлайн-источник **Rutube** в оболочке онлайн-модуля: API по умолчанию **`https://rutube.ru`**, в **`Invoke`** — плагин **`rutubemovie`**, отображаемое имя **`Rutube`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Источник возвращается, если **`args.serial == -1`** или **`args.serial == 0`**. Иначе **`null`**.

## Глобальный поиск

**`CoreInit.conf.online.with_search.Add("rutubemovie")`**.

## Конфигурация

Секция в `init.conf`: **`RutubeMovie`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 565`**, **`streamproxy = true`**, **`rch_access`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "rutubemovie"`** → **` ~ 2160p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/rutubemovie`** | Основная выдача. |
| **`lite/rutubemovie/play`** | Воспроизведение (см. **`Controller.cs`**). |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
