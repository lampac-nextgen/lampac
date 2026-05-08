# Tracks

Модуль **Tracks**: загружает **`ModuleConf`** и регистрирует лимиты WAF для тяжёлых endpoint’ов (например **`ffprobe`**). Путь модуля доступен как **`Tracks.modpath`**.

## Интерфейс

**`IModuleLoaded`** — без **`IModuleOnline`**.

## Поведение (`Loaded`)

- **`modpath = initspace.path`**;
- **`conf.limit_map`** вставляется в начало **`CoreInit.conf.WAF.limit_map`**;
- создаётся каталог **`database/tracks`**.

## Конфигурация

Секция в `init.conf`: **`Tracks`** (`ModuleConf`).

По умолчанию в коде: **`tsuri = null`**, правило **`^/ffprobe`** — **`limit = 10`** запросов за **`1`** секунду.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`tracks.js`**, **`tracks/js/{token}`** | Клиентский скрипт / токенизированная версия. |
| **`ffprobe`** | Вызов ffprobe (ограничен WAF). |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
