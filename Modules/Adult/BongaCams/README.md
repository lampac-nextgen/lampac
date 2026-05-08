# BongaCams

Раздел **Sisi**: вебкам-платформа **BongaCams** (`https://ee.bongacams.com`). Префикс маршрутов **`bgs`**. **18+**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`**.

## Регистрация канала (`Invoke`)

Канал **`bongacams.com`** появляется только при том же условии, что и у других «браузерных» источников: **`priorityBrowser == "http"`** или **`rhub`**, или Playwright активен, или задан **`overridehost` / `overridehosts`**. Иначе **`null`**.

## Конфигурация

Секция в `init.conf`: **`BongaCams`** (`SisiSettings`).

По умолчанию: **`spider = false`**, **`httpversion = 2`**, **`displayindex = 22`**, **`rch_access = apk`**, **`stream_access = apk,cors,web`**, заголовки **`referer`** и **`x-requested-with`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`bgs`** | Основная выдача (см. **`Controller.cs`**). |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
