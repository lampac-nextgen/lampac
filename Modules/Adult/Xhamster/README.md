# Xhamster

Раздел **Sisi**: источник **xHamster** (`https://ru.xhamster.com`). Префикс маршрутов **`xmr`**. **18+**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`**.

## Каналы (`Invoke`)

- Всегда: **`xhamster.com`** → **`xmr`**.
- При **`args.lgbt`**: добавляются **`xmrgay`** и **`xmrtrans`** (**`xmrgay`**, **`xmrsml`** в коде префиксов).

## Конфигурация

Секция в `init.conf`: **`Xhamster`** (`SisiSettings`).

По умолчанию: **`httpversion = 2`**, **`displayindex = 13`**, **`rch_access` / `stream_access`** (`apk`, `cors`, `web`), **`headers_image`** для превью.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`xmr`**, **`xmrgay`**, **`xmrsml`** | Списки и разделы. |
| **`xmr/vidosik`** | Карточка видео. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
