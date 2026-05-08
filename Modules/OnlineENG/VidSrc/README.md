# VidSrc

Онлайн-источник **VidSrc** (`https://vidsrc.cc`) для ENG — **`disableEng`**, источник **`tmdb`/`cub`**, положительный числовой **`id`**, Playwright активен.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Плагин **`vidsrc`**, имя **`VidSrc`**, суффикс **` (ENG)`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`Vidsrc`** (ключ инициализации **`Vidsrc`** — см. **`ModuleInvoke.Init`** в **`ModInit`**).

По умолчанию: **`displayindex = 1005`**, **`streamproxy = true`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "vidsrc"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/vidsrc`** | Основная выдача. |
| **`lite/vidsrc/video`**, **`lite/vidsrc/video.m3u8`** | Видео / HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
