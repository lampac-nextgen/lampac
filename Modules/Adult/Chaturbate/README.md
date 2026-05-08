# Chaturbate

Раздел **Sisi**: источник **Chaturbate** (`https://chaturbate.com`). Доступ к контенту **18+**.

## Интерфейс

- **`IModuleLoaded`**, **`IModuleSisi`**.

## Регистрация канала (`Invoke`)

Канал **`chaturbate.com`** (префикс маршрутов **`chu`**) добавляется **только если** выполняется хотя бы одно из условий:

- **`priorityBrowser == "http"`**, или **`rhub`**, или Playwright **не** в состоянии **`disabled`**, или задан **`overridehost`**, или непустой **`overridehosts`**.

Иначе **`Invoke`** возвращает **`null`** — источник в списке Sisi не появится (типичный сценарий: нужен браузерный/прокси-путь или переопределение хоста).

## Конфигурация

Секция в `init.conf`: **`Chaturbate`** (`SisiSettings`).

Базовые значения в коде: **`spider = false`**, **`httpversion = 2`**, **`displayindex = 24`**, **`rch_access` / `stream_access`** (`apk`, `cors`, `web` по назначению).

Перечитка при **`EventListener.UpdateInitFile`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`chu`** | Основная выдача (см. **`Controller.cs`**). |
| **`chu/potok`** | Дополнительный поток/раздел (см. контроллер). |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
