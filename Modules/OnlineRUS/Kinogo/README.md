# Kinogo

Онлайн-источник **Kinogo** (`https://kinogo.luxury`): требуется **Playwright**. При загрузке читается **`playerjs.js`** из каталога модуля в **`Kinogo.playerjs`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Условие (`Invoke` / `Spider`)

При **`PlaywrightBrowser.disabled`** возвращается **`null`**. Иначе один пункт **`new(conf)`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`Kinogo`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 530`**, **`rch_access = apk`**, **`stream_access = apk,cors`**, **`rchstreamproxy = web`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "kinogo"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/kinogo`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`playerjs.js`**, **`OnlineApi.cs`**, **`Model.cs`**.
