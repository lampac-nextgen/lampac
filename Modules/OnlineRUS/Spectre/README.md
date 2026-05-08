# Spectre

Онлайн-источник **Spectre** (`ModuleConf`): интеграция с внешним API и прокси-слоем; при старте поднимается **`Service`**, подписка **`EventListener.ProxyApiCreateHttpRequest`**. Требуется **Chromium** (**`Chromium.Status`**, не **`PlaywrightBrowser`**) — при **`disabled`** **`Invoke`** возвращает **`null`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Поведение (`Invoke`)

Либо **`null`** (Chromium выключен), либо **`new(conf)`**.

## Конфигурация

Секция в `init.conf`: **`Spectre`** (`ModuleConf`). Endpoint’ы и ключи задаются в типе — см. **`ModuleInvoke.Init`** в **`ModInit`** и переопределите в JSON.

По умолчанию: **`enable = true`**, **`mux`**, **`m4s`**, **`displayindex = 510`**, **`streamproxy = true`**, **`httpversion = 2`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "spectre"`** → **` ~ 2160p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/spectre`** | Основная выдача. |
| **`lite/spectre/video`**, **`lite/spectre/video.m3u8`** | Видео / HLS. |
| **`lite/spectre-search`** | Поиск. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`Service.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
