# Eneyida

Онлайн-источник **Енейіда** (`https://eneyida.tv`): всегда один пункт с пометкой **«(Украинский)»** в названии.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Поведение (`Invoke`)

Без фильтров по сериалу/аниме — **`new ModuleOnlineItem(conf, arg_title: " (Украинский)")`** всегда.

## Конфигурация

Секция в `init.conf`: **`Eneyida`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 820`**, **`rch_access`**, **`stream_access`**, **`geo_hide = ["RU", "BY"]`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "eneyida"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/eneyida`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
