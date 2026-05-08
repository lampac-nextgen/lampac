# Kodik

Онлайн **Kodik** (`ModuleConf`): API **`kodik-api.com`**, плеер **`kodikplayer.com`**, ссылки **`kodikres.com`**, токен в **`ModuleInvoke.Init`** — переопределите в **`init.conf`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Условие (`Invoke`)

Требуется **`args.original_language`**: первый сегмент до **`|`** должен быть одним из **`ja`**, **`ko`**, **`zh`**, **`cn`**, **`th`**, **`vi`**, **`tl`**. Иначе **`null`**.

## Условие (`Spider`)

Только **`args.isanime`**.

## Глобальный поиск

**`with_search.Add("kodik")`**.

## Конфигурация

Секция в `init.conf`: **`Kodik`** (`ModuleConf`).

По умолчанию: **`displayindex = 100`**, **`apihost`**, **`playerhost`**, **`linkhost`**, **`auto_proxy`**, **`cdn_is_working`**, **`referer`** на **`anilib.me`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "kodik"`** → **` ~ 720p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/kodik`** | Основная выдача. |
| **`lite/kodik/video`**, **`lite/kodik/video.m3u8`** | Видео / HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
