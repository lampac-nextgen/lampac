# Mikai

Онлайн-источник **Mikai** (`https://api.mikai.me`) с украинской подписью в списке: **`arg_title: " (Украинский)"`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Только для аниме: при **`!args.isanime`** возвращается **`null`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`Mikai`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 150`**, **`rch_access`**, **`stream_access`**, **`rchstreamproxy = web`**, **`geo_hide = ["RU", "BY"]`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "mikai"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/mikai`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
