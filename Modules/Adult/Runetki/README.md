# Runetki

Раздел **Sisi**: источник **Runetki** (`https://rus.runetki5.com`). Префикс маршрутов **`runetki`**. **18+**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`**.

## Регистрация канала (`Invoke`)

Канал **`runetki.com`** регистрируется только если включён HTTP-приоритет, **`rhub`**, Playwright, **`overridehost`** или **`overridehosts`** — иначе **`null`** (см. **`ModInit.Invoke`**).

## Конфигурация

Секция в `init.conf`: **`Runetki`** (`SisiSettings`).

По умолчанию: **`spider = false`**, **`httpversion = 2`**, **`displayindex = 23`**, **`rch_access = apk`**, **`stream_access = apk,cors,web`**, кастомные **`headers`** (`referer`, `x-requested-with`).

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`runetki`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
