# Dreamerscast

Онлайн-источник **Dreamerscast** (`https://dreamerscast.com`) только для аниме; поддерживается **spider**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Условие (`Invoke` / `Spider`)

Оба метода возвращают данные **только при** **`args.isanime`**. В **`Invoke`** пункт именован: плагин **`dreamerscast`**, отображаемое имя **`Dreamerscast`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`** — источник не добавляется в общий список поиска через этот хук.

## Конфигурация

Секция в `init.conf`: **`Dreamerscast`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 130`**, **`rch_access = apk`**, **`stream_access = apk,cors`**, **`rchstreamproxy = web`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "dreamerscast"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/dreamerscast`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
