# Storage

Серверное **файловое key–value хранилище** для клиента Lampa: чтение/запись blob’ов в **`database/storage/...`**, JS **`backup.js`**, опциональные **временные** файлы под **`/storage/temp/{key}`**. Использует ту же сборку **SyncEvents** и вызов **`NwsEvents.Start(onlyreg: true)`**, что и модуль **Sync** (укороченный режим WebSocket).

Пространство имён: **`Storage`**.

## Интерфейс

**`IModuleLoaded`** (без **`IModuleConfigure`** — персистентность через **файловую систему**, не EF).

## Жизненный цикл (`ModInit`)

| Этап | Действие |
|------|----------|
| **`Loaded`** | **`modpath = baseconf.path`**, **`updateConf()`**, подписка **`UpdateInitFile`**, **`NwsEvents.Start(onlyreg: true)`**, вставка **`conf.limit_map`** в начало **`CoreInit.conf.WAF.limit_map`** |
| **`Dispose`** | отписка от **`UpdateInitFile`**, **`NwsEvents.Stop()`** |

## Конфигурация

Секция в `init.conf`: **`Storage`** — **`ModuleInvoke.Init("Storage", new ModuleConf { … })`**.

| Поле (дефолт в коде) | Назначение |
|----------------------|------------|
| **`enableTemp`** | **`false`** — если **`false`**, **`TempGet`/`TempSet` возвращают «403»** |
| **`limit_map`** | **`^/storage/`** — **10** запросов / **1** с |

Тип **`ModuleConf`** наследует **`ModuleBaseConf`** и добавляет только **`enableTemp`** (`ModuleConf.cs`).

## HTTP (`StorageController`)

| Маршрут | Метод | Назначение |
|---------|-------|------------|
| **`backup.js`**, **`backup/js/{token}`** | **GET** | Подстановка **`{localhost}`**, **`{token}`** в **`modpath/backup.js`** |
| **`/storage/get`** | **GET** | Чтение файла: query **`path`**, **`pathfile`**, **`responseInfo`**; семафор на файл ~20 с |
| **`/storage/set`** | **POST** | Запись тела запроса в файл (до **10 MiB**); query **`path`**, **`pathfile`**, **`connectionId`** — при успехе и непустом **`user_uid`** шлётся **`NwsEvents.SendAsync(..., "storage", ...)`** |
| **`/storage/temp/{key}`** | **GET** | Временное хранилище (только если **`enableTemp`**) |
| **`/storage/temp/{key}`** | **POST** | Запись во временный файл по **`key`** |

Файлы лежат под **`database/storage/`** (логика путей и md5 — **`getFilePath`** в **`Controller.cs`**).

## Связь с другими модулями

- **Sync** при загрузке создаёт **`database/storage`** и **`database/storage/temp`** — имеет смысл включать **Sync** и **Storage** вместе.
- Для **полной** обработки WebSocket (не только **`onlyreg`**) подключите модуль **SyncEvents** — см. **`Modules/Sync/SyncEvents/README.md`**.

## Зависимости

**`SyncEvents`** (`using SyncEvents`), **`NwsEvents`**, **`BaseController`**, файловый кеш скрипта.

## Файлы

| Файл | Роль |
|------|------|
| **`ModInit.cs`** | WAF, **`NwsEvents`**, конфиг |
| **`Controller.cs`** | Все маршруты **`/storage/*`**, **`backup.js`** |
| **`backup.js`** | Шаблон плагина в каталоге модуля |
| **`ModuleConf.cs`** | **`enableTemp`** |
