# AniLiberty

Онлайн-источник **AniLiberty** по API **`https://api.anilibria.app`** (имя секции в коде **`AniLiberty`**).

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Только **`args.isanime`**. Пункт: плагин **`aniliberty`**, имя **`AniLiberty`**.

## Глобальный поиск

**`with_search.Add("aniliberty")`**.

## Конфигурация

Секция в `init.conf`: **`AniLiberty`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 110`**, **`stream_access = apk,cors,web`**, **`httpversion = 2`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "aniliberty"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/aniliberty`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
