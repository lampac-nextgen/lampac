# Tortuga

Украинский онлайн-слой **Tortuga** на базе **`BaseOnlineController`**: **`DecryptQuery`**, кеш вида **`tortuga:view:{href}`**, **rhub** fallback, **`TortugaInvoke`**. В **`ModInit`** базовый URL хоста — **пустая строка**; без заполнения секции в `init.conf` интеграция с реальным API не заработает.

## Интерфейс

**`IModuleLoaded`** — только загрузка **`Tortuga.conf`** (`OnlinesSettings`) и **`UpdateInitFile`**. **`IModuleOnline` нет** — источник в Lampac подключается через вызовы **`lite/tortuga`**.

## Конфигурация

Секция в `init.conf`: **`Tortuga`** (`OnlinesSettings`).

По умолчанию в коде: **`displayindex = 805`**, **`rch_access`**, **`stream_access`**, **`rchstreamproxy = web`**, **`geo_hide = ["RU", "BY"]`**, первый параметр хоста **`""`**.

## HTTP (`TortugaController`)

| Атрибут | Значение |
|---------|----------|
| Маршрут | **`GET lite/tortuga`** |
| Кеш | **`[Staticache]`** |

Параметры **`Index`**: **`uri`**, **`title`**, **`original_title`**, **`t`** (строка), **`s`**, **`rjson`**.

Кеш результата **`InvokeCacheResult`**: ключ **`tortuga:view:{href}`**, TTL **180** минут; при **`IsRhubFallback`** — переход на **`rhubFallback`**.

## Файлы

**`ModInit.cs`**, **`TortugaController.cs`**, **`TortugaInvoke`**, **`OnlineApi.cs`**, **`Model.cs`**.
