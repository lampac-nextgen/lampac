# iRemux

Онлайн-источник **iRemux** (`https://megaoblako.com`): в **`Invoke`** — плагин **`remux`**, имя **`iRemux`**. В шаблоне **`enable = false`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Пункт добавляется при **`args.serial == -1`** или **`args.serial == 0`**.

## Глобальный поиск

**`with_search.Add("remux")`**.

## Конфигурация

Секция в `init.conf`: **`iRemux`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 537`**, **`stream_access = apk,cors,web`**, **`plugin = "remux"`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "remux"`** → **` ~ 2160p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/remux`** | Основная выдача. |
| **`lite/remux/movie`** | Фильм / разбор (см. **`Controller.cs`**). |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
