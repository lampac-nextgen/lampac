# AniLibria

Онлайн-источник **AniLibria** по API **`https://api.anilibria.tv`** (в конфиге тип **`AnilibriaOnline`**). В шаблоне **`enable = false`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Условие (`Invoke` / `Spider`)

Только **`args.isanime`**. В **`Invoke`**: плагин **`anilibria`**, имя **`Anilibria`**.

## Глобальный поиск

**`with_search.Add("anilibria")`**.

## Конфигурация

Секция в `init.conf`: **`AniLibria`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 105`**, **`enable = false`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "anilibria"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/anilibria`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
