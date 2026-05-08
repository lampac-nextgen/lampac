# Sync

Синхронизация **закладок Lampa** и пользовательских данных с **сервером**: REST API, JS-плагины, БД **SQLite** через `SqlContext`, плюс лёгкий запуск **NWS** только в режиме регистрации.

## Назначение

- Хранит и объединяет закладки между устройствами (списки, добавление/удаление, общие endpoint’ы для клиента).
- Регистрирует лимит WAF для префикса **`^/bookmark`**.
- Создаёт каталоги `database/storage` и `database/storage/temp` для связки со Storage-модулем (общая структура данных на сервере).
- Запускает `NwsEvents.Start(onlyreg: true)` — обслуживаются преимущественно регистрационные сообщения WebSocket без полного набора обработчиков (полный режим даёт модуль **SyncEvents**).

## HTTP-маршруты (обзор)

| Префикс / файл | Назначение |
|----------------|------------|
| `sync.js`, `sync/js/{token}` | Плагин синхронизации для клиента. |
| `invc-ws.js`, `invc-ws/js/{token}` | Вспомогательный скрипт для WebSocket / инвентаризации (см. контроллер). |
| **`BookmarkController`** | `bookmark.js`, `/bookmark/list`, `/bookmark/set`, `/bookmark/add`, `/bookmark/added`, `/bookmark/remove` и др. |

Подробная семантика параметров — в `Controllers/BookmarkController.cs` и `SyncController.cs`.

## Конфигурация

Секция в `init.conf`: **`Sync`** — **`ModuleInvoke.Init("Sync", new ModuleConf { … })`**.

В **`updateConf`** по умолчанию задаётся только **`limit_map`** для закладок:

| Префикс WAF | Лимит |
|-------------|--------|
| **`^/bookmark`** | **10** запросов за **1** с |

Полная схема **`ModuleConf`** — в **`Models/ModuleConf.cs`**.

## Связь с SyncEvents

В **`Loaded`**: **`NwsEvents.Start(onlyreg: true)`** ( **`using SyncEvents`** ) — укороченный режим WebSocket. Для **полной** обработки сообщений подключите модуль **SyncEvents** (**`onlyreg: false`**). В **`Dispose`**: **`NwsEvents.Stop()`** — остановка совпадает с жизненным циклом модуля **Sync**.

## Интерфейсы

**`IModuleLoaded`**, **`IModuleConfigure`** — в **`Configure`** регистрируется **`AddDbContextFactory<SqlContext>`**.

## Пути на диске

**`modpath = baseconf.path`**; создаются **`database/storage`** и **`database/storage/temp`**. При загрузке правила **`conf.limit_map`** вставляются в начало **`CoreInit.conf.WAF.limit_map`**.

## Компоненты

| Путь | Роль |
|------|------|
| `Controllers/SyncController.cs` | JS sync / invc-ws. |
| `Controllers/BookmarkController.cs` | API закладок. |
| `SqlContext.cs` | Контекст БД. |

После загрузки вызывается **`SqlContext.Initialization(baseconf.app.ApplicationServices)`** — подготовка БД под DI хоста.

## Зависимости

**Entity Framework**: **`IModuleConfigure.Configure`** → **`AddDbContextFactory<SqlContext>`**. Модуль **SyncEvents** при необходимости дополняет режим **`NwsEvents`** (см. раздел выше).
