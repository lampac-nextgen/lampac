# SISI

Модуль контента **18+** для Lampac: встроенные парсеры платформ, плагин Lampa **`/sisi.js`**, история просмотров и закладки в **SQLite** (`SisiContext`).

## Платформы (15 контроллеров)

Базовые HTTP-маршруты (корень сайта Lampac):

| Контроллер | Маршрут (пример) | Примечание |
| --- | --- | --- |
| `BongaCams` | `/bgs` | |
| `Chaturbate` | `/chu` | в т.ч. `/chu/potok` |
| `Ebalovo` | `/elo` | |
| `Eporner` | `/epr` | |
| `HQporner` | `/hqr` | |
| `PornHub` | `/phub` | варианты `/phubgay`, `/phubsml` |
| `PornHubPremium` | `/phubprem` | |
| `Porntrex` | `/ptx` | |
| `Runetki` | `/runetki` | |
| `Spankbang` | `/sbg` | |
| `Tizam` | `/tizam` | |
| `Xhamster` | `/xmr` | варианты `/xmrgay`, `/xmrsml` |
| `Xnxx` | `/xnx` | |
| `Xvideos` | `/xds` | варианты `/xdsgay`, `/xdssml` |
| `XvideosRED` | `/xdsred` | |

Дополнительные вложенные маршруты (`/vidosik`, `/stars` и т.д.) см. в соответствующих файлах `Controllers/*.cs`.

## Связь с NextHUB

**SISI** — нативные C#-источники с фиксированными маршрутами. **NextHUB** (`Modules/NextHUB`) — отдельный модуль для десятков сайтов на YAML; маршрут **`/nexthub`**. Оба относятся к контенту 18+, но не дублируют друг друга.

## Конфигурация

В `ModInit.cs` при инициализации подмешиваются лимиты **WAF** для путей вида `/(sisi|bgs|chu|runetki|elo|epr|hqr|phub|ptx|sbg|tizam|xmr|xnx|xds)` (**5** req/s, `pathId: true`). Секция **`sisi`** в `init.conf` задаёт поведение плагина, историю, закладки и т.д. (см. также `Config/ModuleConf.cs`, `SiteConf`).

## Каталоги данных

При старте создаются `wwwroot/bookmarks/img` и `wwwroot/bookmarks/preview` для сохранения обложек закладок (если включено в конфиге).
