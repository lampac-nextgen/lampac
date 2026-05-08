# UaKino

Онлайн-источник **UaKino** (`https://uakino.cx`) с пометкой **«(Украинский)»** при успешной регистрации в **`Invoke`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Пункт **`new(conf, arg_title: " (Украинский)")`** добавляется **только если** **`PlaywrightBrowser.Status != PlaywrightStatus.disabled`**. Иначе возвращается пустой список.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`UaKino`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 825`**, **`rch_access`**, **`stream_access`**, **`geo_hide = ["RU", "BY"]`**, набор **`headers`** под навигацию документа.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "uakino"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/uakino`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
