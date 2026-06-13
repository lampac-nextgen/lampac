# NextHUB

Расширяемые **Sisi-каналы на YAML**: парсинг сайтов через **HTTP и Playwright**, списки и карточки контента, выдача видео по URI. Регистрируется как источник раздела для взрослых через **`IModuleSisi`** (`SisiApi`), UI хостится под **`/nexthub`**.

## Назначение

- Каждый файл `sites/*.yaml` в каталоге модуля описывает «плагин» (меню, списки, поиск, разбор страницы просмотра).
- `Root.goInit(plugin)` загружает и кеширует конфигурацию; каналы в Sisi формируются с зашифрованным query-параметром `plugin`.
- Для сайтов, где нужен браузер, учитывается состояние **Playwright** (`PlaywrightBrowser.Status`); при отключённом Chromium записи с приоритетом не-http могут отфильтроваться.

## HTTP-маршруты

| Маршрут | Описание |
|---------|----------|
| `GET /nexthub` | Лента/меню/поиск: параметры `plugin`, `search`, `sort`, `cat`, `model`, `pg`. Часть query может быть зашифрована (`DecryptQuery`). |
| `GET /nexthub/vidosik` | Страница просмотра: параметр `uri` вида `plugin_-:-_url` (разделитель `_-:-_`). |

Кэширование ответов — через атрибуты/`InvokeCacheResult` в контроллерах.

## WAF

При загрузке в **`CoreInit.conf.WAF.limit_map`** добавляется правило для **`^/nexthub`** с лимитом и учётом query-параметра **`plugin`**.

## Конфигурация

Секция в `init.conf`: **`NextHUB`** (`ModuleInvoke.Init("NextHUB", new ModuleConf())`).

Дополнительные ключи — в **`ModuleConf.cs`** проекта.

## Зависимости

- **Playwright** (для сценариев с браузером), **HtmlAgilityPack**, возможность выполнения **CSharpEval** для динамических выражений в YAML.

## Структура каталога

| Путь | Роль |
|------|------|
| `sites/*.yaml` | Описание каждого источника (списки, view, парсеры). |
| `Controllers/ListController.cs` | Списки, меню и HTTP-запросы ленты. |
| `Controllers/ListController.Playlist.cs` | Преобразование HTML/YAML `contentParse` в `PlaylistItem`. |
| `Controllers/ViewController.cs` | Карточка просмотра. |
| `Services/ModelProbeResolver.cs` | Догрузка моделей по конкретной карточке видео. |
| `Services/ModelProbeSourceSettings.cs` | Список источников, где включена догрузка моделей, cache key и таймауты. |
| `Services/ModelProbeParserRegistry.cs` | Выбор parser-метода по имени источника. |
| `Services/ModelProbeParsers.cs` | Source-specific parser-методы моделей. |
| `Services/ModelProbeParserUtilities.cs` | Общие helper-методы для model probe parser-ов. |
| `SisiApi.cs` | Регистрация каналов в разделе Sisi. |
| `Root.cs` | Загрузка init из YAML. |

## Model Probe

Модели не всегда парсятся на первой странице списка. Для части источников это
замедляет загрузку ленты: нужно открывать страницу каждого видео. Поэтому
NextHUB использует `model_probe`, который передается через сериализуемое поле
`myarg`.

Поток такой:

1. YAML-источник кладет в `PlaylistItem.myarg` строку вида
   `model_probe:nexthub/model?plugin=...&href=...`.
2. Клиент SISI вызывает `model_probe`, когда пользователь открывает меню
   конкретной карточки.
3. `ModelProbeResolver` нормализует `href`, проверяет настройки источника,
   делает HTTP-запрос страницы видео и кеширует результат.
4. `ModelProbeParserRegistry` выбирает parser по `plugin`.
5. Если моделей несколько, клиент показывает пункт `Модели` как вложенную
   папку. Если модель одна, остается обычный прямой пункт модели.

Чтобы добавить model probe для нового источника:

1. В YAML заполнить `pl.myarg = $"model_probe:nexthub/model?...";` в
   `contentParse`.
2. Добавить источник в `ModelProbeSourceSettings`.
3. Добавить parser в `ModelProbeParsers`.
4. Подключить parser в `ModelProbeParserRegistry`.
5. Проверить `docker compose restart lampac` и логи `compilation NextHUB`.
