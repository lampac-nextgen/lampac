# HdvbUA

Украинский вариант схемы **HDVB** в оболочке **`BaseOnlineController`**: контроллер наследует базовую логику (гидра, RCH, прокси стрима, кеш, **rhub** fallback). Модуль **только загружает** **`OnlinesSettings`** в **`HdvbUA.conf`** — **`IModuleOnline` в `ModInit` нет**; выдача идёт через HTTP.

## Интерфейс

**`IModuleLoaded`**: **`Loaded`/`Dispose`**, перечитка по **`EventListener.UpdateInitFile`**.

## Конфигурация

Секция в `init.conf`: **`HdvbUA`**.

В **`ModuleInvoke.Init`** базовый URL хоста в шаблоне — **пустая строка** (**`""`**). Обязательно задайте реальные endpoint’ы плеера/API в конфиге.

Шаблон по умолчанию: **`displayindex = 815`**, **`rch_access`**, **`stream_access`**, **`geo_hide = ["RU", "BY"]`**.

## HTTP (`HdvbUAController`)

| Атрибут | Значение |
|---------|----------|
| Маршрут | **`GET lite/hdvbua`** |
| Кеш ответа | **`[Staticache]`** |

Параметры действия **`Index`** (см. **`Controller.cs`**): **`uri`**, **`title`**, **`original_title`**, **`t`**, **`s`**, **`rjson`**.

Логика: **`href = DecryptQuery(uri)`** — без валидного **`href`** возвращается ошибка; при блокировке запроса — **`badInitMsg`**; кеш **`InvokeCacheResult`** с ключом вида **`hdvbua:view:{href}`**, TTL **40** минут; при **`IsRhubFallback`** — повтор с меткой **`rhubFallback`**; ответ через **`ContentTpl`** и **`HdvbUAInvoke.Tpl`**.

## Связь с HDVB (RU)

По смыслу тот же семейство **HDVB**, что модуль **`Modules/OnlineRUS/HDVB`**, но отдельная секция конфига и украинский маршрут **`lite/hdvbua`**.

## Файлы

**`ModInit.cs`**, **`HdvbUAController.cs`**, **`HdvbUAInvoke`**, **`OnlineApi.cs`**, **`Model.cs`** — см. **`manifest.json`**.
