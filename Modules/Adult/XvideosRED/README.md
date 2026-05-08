# XvideosRED

Раздел **Sisi**: вариант **Xvideos RED** (`https://www.xvideos.red`). Префикс маршрутов **`xdsred`**. Отдельная подписка/контент относительно обычного Xvideos; **18+**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`** — один канал **`xvideos.red`**.

## Конфигурация

Секция в `init.conf`: **`XvideosRED`** (`SisiSettings`).

В коде по умолчанию модуль **выключен**: **`enable = false`**, **`displayindex = 20`**. Включите в конфиге при необходимости.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`xdsred`** | Основная выдача. |
| **`xdsred/vidosik`** | Карточка видео. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
