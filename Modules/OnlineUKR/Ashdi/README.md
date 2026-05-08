# Ashdi

Модуль **Ashdi**: украинский онлайн-слой на **`BaseOnlineController`** с базой **`https://base.ashdi.vip`** в шаблоне **`ModuleInvoke.Init`**. Как и **Tortuga/HdvbUA**, **`ModInit` только держит `Ashdi.conf`** — **`IModuleOnline` не реализован**; клиент ходит на **`lite/ashdi`**.

## Интерфейс

**`IModuleLoaded`**: **`Loaded`/`Dispose`**, перечитка **`EventListener.UpdateInitFile`**.

## Конфигурация

Секция в `init.conf`: **`Ashdi`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 800`**, **`rch_access`**, **`stream_access`**, **`rchstreamproxy = web`**, **`geo_hide = ["RU", "BY"]`**.

## HTTP (`AshdiController`)

| Атрибут | Значение |
|---------|----------|
| **`lite/ashdi`** | Основная выдача: **`Index(uri, title, original_title, t, s, rjson)`**, **`[Staticache]`**, **`DecryptQuery(uri)`**, кеш **`ashdi:view:{href}`** (TTL **180** мин), **rhub** fallback, JSON-текст при **`Embed`** (**`textJson: true`** в **`InvokeCacheResult`**). |
| **`lite/ashdi/vod.m3u8`** | HLS (доп. действия в **`Controller.cs`**). |

Шаблонизация ответа через **`AshdiInvoke.Tpl`** (порядок аргументов отличается от Tortuga — см. вызов в контроллере).

## Отличия от Tortuga / HdvbUA

- У **Ashdi** в дефолте задан **непустой** базовый хост (**`base.ashdi.vip`**).
- Кеш embed для основного **`Index`** совпадает по TTL (**180** мин), для **HdvbUA** в коде **40** мин.

## Файлы

**`ModInit.cs`**, **`AshdiController.cs`**, **`AshdiInvoke`**, **`OnlineApi.cs`**, **`Model.cs`**.
