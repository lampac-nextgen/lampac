# ForkPlayerXML

Стартовый URL для клиента ForkPlayer: **`http://<хост>:<порт>/fxml`** (типичный порт Lampac — **9118**).

Интеграция с **ForkPlayer**: XML/JSON-плейлисты для **каталога**, **Sisi** и **онлайна** через **`EventListener`**, редирект с корня **`/`** на **`/fxml`**, правки модели инициализации клиента (**`rhub`**, **`streamproxy`**).

Пространство имён кода: **`ForkXML`**.

## Назначение

- **Catalog**: `CatalogChannels`, `CatalogList`, `CatalogCard` → **`CatalogAPI`**.
- **Sisi**: `SisiChannels`, `SisiPlaylistResult`, `SisiOnResult` → **`SisiAPI`**.
- **Online**: `OnlineChannels`, `OnlineContentTpl`, `VideoTpl` → **`OnlineAPI`**.
- **Middleware**: если запрос распознан как ForkPlayer (**`Utilities.IsForkPlayer`**) и путь **`/`**, выполняется **редирект** на **`/fxml`** с сохранением query через **`Utilities.ClearArgs`** (обработчик возвращает **`false`** — цепочка прерывается).
- **BadInitialization**: для ForkPlayer в модели инициализации выставляются **`rhub = false`**, **`streamproxy = true`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **GET** `/fxml` | Корневой JSON-плейлист ForkPlayer: пункты «Поиск», «Сейчас смотрят», категории фильмов/сериалов/аниме, ссылки на **`/fxml/cub`**, **`/catalog`**, **`/sisi`**. При включённом **`accsdb`** без авторизованного пользователя возвращается сообщение об ошибке доступа (с **`box_mac`). См. **`ForkController`**. |
| **GET** `/fxml/cub` | Выдача каталога по параметрам **`search`**, **`cat`**, **`sort`**, **`page`**; данные берутся с **`http://tmdb.cub.red/`**, кеш в памяти ~5 минут. См. **`CubController`**. |

Контроллеры наследуют **`BaseController`**.

## Конфигурация

Отдельной секции **`ForkPlayerXML`** в минимальном `ModInit` нет — включение через **`manifest.json`**. Доступ к пунктам главного меню зависит от **`CoreInit.conf.accsdb`** и глобальных настроек каталога/Sisi.

## Зависимости

- Реализации **`CatalogAPI`**, **`SisiAPI`**, **`OnlineAPI`** в каталоге модуля.
- Внешний HTTP **`tmdb.cub.red`** для списков в **`/fxml/cub`**.

## Связь с клиентом

ForkPlayer ожидает ответы в формате, который формируют перечисленные API; без этого модуля соответствующие события не обрабатываются.
