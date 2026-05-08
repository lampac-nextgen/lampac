# IptvOnline

Онлайн-источник **iptv.online** (`https://iptv.online`): в **`Invoke`** явно задаются плагин **`iptvonline`** и отображаемое имя **`iptv.online`**. В шаблоне **`enable = false`**, **`rhub_safety = false`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Поведение (`Invoke`)

Всегда один пункт **`new(conf, "iptvonline", "iptv.online")`**.

## Конфигурация

Секция в `init.conf`: **`IptvOnline`** (`OnlinesSettings`).

В коде комментарий к API дилеров: **`https://iptv.online/ru/dealers/api`**.

По умолчанию: **`displayindex = 345`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "iptvonline"`** → **` ~ 2160p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/iptvonline`** | Основная выдача. |
| **`lite/iptvonline/bind`** | Привязка / служебный endpoint (см. **`Controller.cs`**). |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`Model.cs`**, **`OnlineApi.cs`**.
