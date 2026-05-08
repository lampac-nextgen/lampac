# PidTor

Торренты в онлайне Lampac: поиск раздач через API **JacRed** (`redapi`, по умолчанию `http://jac.red`), фильтрация и сортировка, добавление magnet в **TorrServer** и выдача потоков **`/lite/pidtor/s…`**.

## Назначение

- **`IModuleOnline`**: пункт **Pid&lt;s&gt;T&lt;/s&gt;or** (`pidtor`) появляется только если заданы **`torrs`** и/или **`auth_torrs`** — иначе список пустой.
- **`with_search`**: добавляется ключ **`pidtor`**.
- **`UpdateCurrentConf`**: **`tsport`** берётся из секции **`TorrServer`** в **`current.conf`** (иначе **9085**) для обращения к локальному TS.
- **`OnlineApiQuality`**: для балансера **`pidtor`** подпись **` ~ 2160p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| `GET lite/pidtor` | Поиск торрентов по параметрам карточки (`title`, `original_title`, `year`, `original_language`, `serial`, сезон `s`). Кеш ~40 мин. Запрос к **`{redapi}/api/v2.0/indexers/all/results`**. |
| `GET lite/pidtor/serial/{id}` | Сериалы: список файлов торрента по hash **`id`**, добавление magnet на TS, шаблон серий. |
| `GET lite/pidtor/s{id}` | Стрим выбранного файла (magnet hash **`id`**, опционально **`tsid`**). |

При **`enable == false`** или при ограничении группы (**`NoAccessGroup`**) — **403** или JSON с **`accsdb`**.

## Логика контроллера (кратко)

- Фильтры: **`min_sid`**, **`max_size`** / **`max_serial_size`**, качество в имени (**720p/1080p/2160p/4K**) или **`forceAll`**, **`emptyVoice`**, regex **`filter`** / **`filter_ignore`**, сортировка **`sort`** (`size`, `sid`, …).
- Трекер **`selezen`** отбрасывается.
- Для стрима выбирается хост TorrServer: из **`torrs`**, **`auth_torrs`** (логин/пароль), **`base_auth`** или локальный **`listen.localhost:{tsport}`** с паролем из **`data/ts/accs.db`**.

## Конфигурация

Секция в `init.conf`: **`PidTor`** (`PidTorSettings`).

| Поле | Смысл |
|------|--------|
| `enable` | Включить модуль и HTTP. |
| `redapi` | Базовый URL JacRed API. |
| `apikey` | Ключ для **`/api/v2.0/indexers/all/results`**. |
| `torrs` | Список URL TorrServer для стриминга (или один хост). |
| `auth_torrs` | Отдельные TS с логином/паролем. |
| `base_auth` | Basic-auth к **`torrs`**. |
| `displayname`, `displayindex`, `group`, `group_hide` | Отображение в UI. |
| `min_sid`, `max_size`, `max_serial_size`, `forceAll`, `sort`, `filter`, `filter_ignore`, `emptyVoice` | Отбор раздач. |

## Связанные модули

- **JacRed** — должен отдавать JSON по **`redapi`**.
- **TorrServer** — приём magnet и отдача **`/stream`** (порт **`tsport`**).

## Файлы

| Файл | Роль |
|------|------|
| `ModInit.cs` | Конфиг, `Invoke`, `tsport`, качество. |
| `Controller.cs` | Класс **`PiTor`**, маршруты `lite/pidtor*`. |
| `Model.cs` | DTO торрентов и ответов TS. |
