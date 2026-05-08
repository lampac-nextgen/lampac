# Zetflix

Онлайн-источник **Zetflix**: ориентирован на матчинг по **Kinopoisk ID** и работу через **Playwright**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие появления в выдаче (`Invoke`)

Пункт добавляется **только если**:

- **`PlaywrightBrowser.Status != PlaywrightStatus.disabled`**, и  
- **`args.kinopoisk_id > 0`**.

Иначе возвращается **`null`** — источник не предлагается для текущего запроса.

## Конфигурация

Секция в `init.conf`: **`Zetflix`** (`ModuleConf`).

В **`updateConf`**: **`ModuleInvoke.Init("Zetflix", new ModuleConf(...))`** с внутренним строковым параметром конструктора (обфусцированный endpoint в репозитории), **`enable: true`**, **`streamproxy: true`**, **`displayindex = 510`**, **`httpversion = 2`**, **`stream_access`**, **`geostreamproxy = ["ALL"]`**, **`headers = Http.defaultFullHeaders`**.

## Подпись качества

**`OnlineApiQuality`**: для **`e.balanser == "zetflix"`** — **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/zetflix`** | Списки и карточки. |
| **`lite/zetflix/video`**, **`lite/zetflix/video.m3u8`** | Видео / HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`** (крупный), **`manifest.json`**.
