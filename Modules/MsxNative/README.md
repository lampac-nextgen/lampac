# MsxNative

Адаптация раздела **Sisi** и модели инициализации клиента для **MSX / MS X** (плеер с особыми User-Agent / признаками в **Shared** — **`Utilities.IsMsxPlayer`**).

Пространство имён: **`MsxNative`**.

## Подписки на `EventListener`

Все обработчики снимаются в **`Dispose`**:

| Событие | Обработчик |
|---------|------------|
| **`Middleware`** | **`Middleware`** (см. ниже) |
| **`BadInitialization`** | **`BadInitialization`** |
| **`SisiChannels`** | **`SisiAPI.Channels`** |
| **`SisiPlaylistResult`** | **`SisiAPI.PlaylistResult`** |
| **`SisiOnResult`** | **`SisiAPI.OnResult`** |

## Middleware (доступ к `/sisi` без accsdb)

Срабатывает **только если одновременно**:

- **`first == true`** (первый проход middleware);
- **`CoreInit.conf.accsdb.enable == true`**;
- **`Utilities.IsMsxPlayer(e.httpContext)`**;
- путь запроса **ровно** **`/sisi`**.

Тогда у **`RequestModel`** из контекста выставляется **`IsAnonymousRequest = true`**, и запрос обрабатывается как анонимный (обход проверки пароля для этого клиента). Во всех остальных случаях обработчик возвращает **`true`** и ничего не меняет.

## BadInitialization

Для запросов, распознанных как MSX (**`IsMsxPlayer`**), в **`e.init`** принудительно: **`rhub = false`**, **`streamproxy = true`**.

## Конфигурация

Отдельной секции **`MsxNative`** в `init.conf` в **`ModInit` нет — включение через **`manifest.json`**. Поведение **Middleware** зависит от глобального **`accsdb`**.

## Файлы

**`ModInit.cs`**, **`SisiAPI*.cs`** (каталог модуля) — логика списков Sisi для MSX.

## Зависимости

Корректная работа **`Utilities.IsMsxPlayer`** в **Shared**; для **Sisi** — те же контракты, что у остальных Sisi-модулей.
