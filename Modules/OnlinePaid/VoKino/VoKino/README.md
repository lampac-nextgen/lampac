# VoKino

Агрегатор онлайна **VoKino**: асинхронный **`IModuleOnlineAsync`** — список балансеров (**VoKino**, **Filmix**, **Alloha**, **Vibix**, **MonFrame**, **Remux**, **Ashdi**, **HDVB**) строится через API **`ModuleConf`** и опционально **`KitInvoke`** / **`kitconf`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnlineAsync`**.

## Условие (`InvokeAsync`)

Работа начинается при **`args.kinopoisk_id > 0`** или **`args.source == "vokino"`** (с подстановкой **`args.id`**). Дальше загружается конфиг (**`enable`**, **`token`**, **`online`** — какие под-балансеры включены). При включённом **`accsdb`** выдача зависит от **`user`** и **`group`**. URL для пунктов: **`{localhost}/lite/vokino?balancer=...`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`VoKino`** (`ModuleConf`). Хост API по умолчанию в коде — **`http://api.vokino.org`** (в комментариях также упоминаются **`api.vokino.pro`** и др.). Задайте **`token`** и флаги **`online.*`** в JSON.

По умолчанию в **`updateConf`**: **`displayindex = 300`**, **`streamproxy = false`**, **`rchstreamproxy = web`**, **`rhub_safety = false`**.

## Подпись качества

**`OnlineApiQuality`**: для **`vokino`**, **`vokino-alloha`**, **`vokino-filmix`** → **` ~ 2160p`**; для **`vokino-vibix`**, **`vokino-monframe`**, **`vokino-remux`**, **`vokino-ashdi`**, **`vokino-hdvb`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/vokinotk`** | Служебный/TK-сценарий (см. **`Controller.cs`**). |
| **`lite/vokino`** | Основная выдача агрегатора. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
