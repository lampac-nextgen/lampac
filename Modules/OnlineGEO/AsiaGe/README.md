# AsiaGe

Онлайн-источник **AsiaGe** (`https://asia.com.ge`) для грузинского окна вокруг азиатских релизов.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Источник добавляется **узко**:

- **`args.serial == 1`**, и  
- первый код языка из **`args.original_language`** (разбор по **`|`**) — **`ko`** или **`cn`**.

В **`Invoke`** подмешивается **`arg_title: " (Грузинский)"`** для подписи в списке.

Если условия не выполнены — возвращается пустой список.

## Конфигурация

Секция в `init.conf`: **`AsiaGe`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 910`**, **`rch_access`**, **`stream_access`** (`apk`, `web`, `cors`).

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "asiage"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/asiage`** | Основная выдача. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
