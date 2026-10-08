# ZetflixDB

Онлайн-слой **ZetflixDB**: строит embed-URL плеера obrut.show (тот же, что встраивает zet-flix.online) и перенаправляет в **`lite/videodb`**. Загрузку и разбор страницы, манифесты и прокси потока выполняет модуль **VideoDB** со своими настройками.

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

По умолчанию: **`apihost = "https://54243ba5.obrut.show"`**, **`displayindex = 515`**. Основной хост пустой.

Embed-URL: **`{apihost}/embed/AO/kinopoisk/{kp}/`**, где **`{kp}`** — base64 от kinopoisk_id без **`=`**, записанный задом наперёд (`1045479` → `QO3QTN0ATM`). Через **`init.conf`** меняется только **`apihost`**.

Заголовки, cookie антибота (**`wd_approval`**, вручную или автоматически через **`cdp`**) и таймаут задаются в секции **`VideoDB`**, см. [README VideoDB](../VideoDB/README.md#антибот-obrutshow).

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "zetflixdb"`** → **`~ 2160p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/zetflixdb`** | Редирект в **`lite/videodb?uri=...`**. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
