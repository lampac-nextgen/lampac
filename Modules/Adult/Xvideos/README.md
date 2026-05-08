# Xvideos

Раздел **Sisi**: источник **Xvideos** (базовый хост в конфиге **`https://www.xv-ru.com`** — зеркало/регион). Префикс маршрутов **`xds`**. Контент **18+**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`**.

## Каналы (`Invoke`)

- Всегда: **`xvideos.com`** → **`xds`**.
- Если в событии **`args.lgbt`** включён режим LGBT: добавляются **`xdsgay`** и **`xdstrans`** (внутренние идентификаторы **`xdsgay`**, **`xdssml`**).

## Конфигурация

Секция в `init.conf`: **`Xvideos`** (`SisiSettings`).

По умолчанию: **`httpversion = 2`**, **`displayindex = 12`**, **`rch_access` / `stream_access`** включают **`apk`**, **`cors`**, **`web`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`xds`**, **`xdsgay`**, **`xdssml`** | Списки/разделы. |
| **`xds/stars`**, **`xdsgay/stars`**, **`xdssml/stars`** | Избранное/«stars». |
| **`xds/vidosik`** | Карточка видео. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
