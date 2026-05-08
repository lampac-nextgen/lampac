# ZetflixDB

Онлайн-слой **ZetflixDB**: второй базовый URL (`54243ba5.obrut.show` в шаблоне), основной хост в коде пустой — уточняйте в **`init.conf`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Пункт добавляется при **`args.kinopoisk_id > 0`** и выполнении хотя бы одного из: **`conf.rhub`**, **`priorityBrowser == "http"`**, Playwright **не** **`disabled`**.

## Синхронизация с VideoDB

**`EventListener.UpdateCurrentConf`**: при наличии секции **`VideoDB`** в **`CoreInit.CurrentConf`** к **`ZetflixDB.conf`** подмешиваются **`rch_access`**, **`stream_access`**, **`priorityBrowser`** из **`VideoDB`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`ZetflixDB`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 515`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "zetflixdb"`** → **` ~ 2160p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/zetflixdb`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
