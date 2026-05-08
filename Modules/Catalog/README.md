# Catalog

Каталог **торрент/онлайн-сайтов** на **YAML**: описание меню, списков и карточек в `sites/`, переопределения в **`override/`**. Регистрация каналов через событие **`EventListener.CatalogChannels`** (ForkPlayer и др.). Загрузка init — **`ModInit.goInit(site)`** с кешем и подбором по **displayname**.

## Назначение

- Выдаёт клиенту JSON со списком доступных каталогов и пунктов меню, собранным из включённых **`sites/*.yaml`** (`enable`, `menu`, не `hide`).
- Плагин **`catalog.js`** получает подстановки `{localhost}`, `{token}` и встроенный объект **`catalogs`** из **`Channels()`**.

## HTTP API

| Маршрут | Описание |
|---------|----------|
| `GET catalog.js`, `catalog/js/{token}` | JS-плагин каталога. |
| `GET catalog` | JSON каналов; если подписаны обработчики **`CatalogChannels`**, может вернуть альтернативный ответ (например ForkPlayer). |
| `GET catalog/list` | Списки по параметрам запроса (см. `ListController`). |
| `GET catalog/card` | Карточка контента (см. `CardController`). |

Лимит WAF по умолчанию: префикс **`^/catalog/`** (в `ModInit`).

## Конфигурация

Секция в `init.conf`: **`Catalog`** (`ModuleBaseConf`).

Уточнение полей — в типах модуля и в корневой документации Lampac.

## Структура данных

| Путь | Роль |
|------|------|
| `sites/*.yaml` | Описание каждого сайта: меню, категории, парсеры. |
| `override/{site}.yaml` и **`override/_.yaml`** | Пользовательские переопределения поверх базового YAML. |

## Зависимости

- **YamlDotNet** и логика слияния в **`ModInit.goInit`** (поиск сайта по имени файла или по **displayname**).

## Компоненты

| Файл | Роль |
|------|------|
| `Controllers/ApiController.cs` | `catalog.js`, индекс каналов. |
| `Controllers/ListController.cs` | Списки. |
| `Controllers/CardController.cs` | Карточки. |
| `ModInit.cs` | Конфиг, WAF, `goInit`, события обновления init. |
