# TimeCode

Учёт и синхронизация **таймкодов** (пользовательские отметки на шкале времени): данные в **SQLite** через **`SqlContext`**, API под **`/timecode/`**, JS-плагин **`timecode.js`** (на диске шаблон **`plugin.js`**).

Пространство имён: **`TimeCode`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleConfigure`** — в **`Configure`** регистрируется **`AddDbContextFactory<SqlContext>`** (как в **Sync**).

## Жизненный цикл (`ModInit`)

| Этап | Действие |
|------|----------|
| **`Loaded`** | **`modpath = baseconf.path`**, **`updateConf()`**, **`UpdateInitFile`**, вставка **`limit_map`** в **`WAF`**, **`SqlContext.Initialization(ApplicationServices)`** |
| **`Dispose`** | отписка от **`UpdateInitFile`** (**`NwsEvents` здесь не вызывается** — в отличие от **Sync** / **Storage**) |

## Конфигурация

Секция в `init.conf`: **`TimeCode`** — **`ModuleInvoke.Init("TimeCode", new ModuleBaseConf { … })`**.

Дефолт в коде — только WAF:

| Префикс | Лимит |
|---------|--------|
| **`^/timecode/`** | **10** запросов / **1** с |

Расширения **`ModuleBaseConf`** — общие поля базового модуля.

## HTTP (`TimeCodeController`)

| Маршрут | Метод | Назначение |
|---------|-------|------------|
| **`timecode.js`**, **`timecode/js/{token}`** | **GET** | Читает **`{modpath}/plugin.js`**, подставляет **`{localhost}`**, **`{token}`** |
| **`/timecode/all`** | **GET** | Query **`card_id`** — словарь таймкодов пользователя по карточке (требуется **`requestInfo.user_uid`**) |
| **`/timecode/add`** | **POST** | Query **`card_id`**, form **`id`**, **`data`** — upsert записи в таблицу **`timecodes`** (семафор **`SqlContext.semaphoreKey`**) |

**`getUserid`**: по умолчанию **`user_uid`**, при **`profile_id`** в query — составной id; очистка символов regex’ом.

## Зависимости

- **EF Core**: фабрика **`IDbContextFactory<SqlContext>`**; БД SQLite **`database/TimeCode.sql`** (отдельно от **`database/Sync.sql`** модуля **Sync**).
- Таблица **`timecodes`**, модель **`SqlModel`** — в **`SqlContext.cs`**.

## Связь с Sync / Storage

**Sync** создаёт каталоги **`database/storage`**. **TimeCode** хранит таймкоды в **`database/TimeCode.sql`**, закладки **Sync** — в **`database/Sync.sql`**. Модуль **TimeCode** **не** вызывает **`NwsEvents`**.

## Файлы

| Файл | Роль |
|------|------|
| **`ModInit.cs`** | WAF, EF, инициализация БД |
| **`Controller.cs`** | **`/timecode/*`**, выдача JS |
| **`plugin.js`** | Шаблон клиентского скрипта (отдаётся как **`timecode.js`**) |
| **`SqlContext.cs`**, **`SqlModel`** | Персистентность |
