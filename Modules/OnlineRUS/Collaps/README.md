# Collaps

Онлайн-источник **Collaps** (`ModuleConf`): API по умолчанию задаётся в **`ModuleInvoke.Init`** вместе с **`apihost`**, заголовками и флагом **`streamproxy`**. В шаблоне **`enable = false`** — включите в `init.conf` при необходимости. Чувствительные параметры (**token**, URL API) лучше переопределять через конфиг, не правя репозиторий.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Поведение (`Invoke` / `Spider`)

- **`Invoke`** всегда возвращает **`new(conf)`**.
- **`Spider`** регистрирует поисковый балансер **`collaps-search`**.

## Глобальный поиск

**`with_search.Add("collaps")`** и **`with_search.Add("collaps-dash")`**.

## Подпись качества

**`OnlineApiQuality`** учитывает **`e.balanser`** **`collaps`** / **`collaps-dash`**: для **`collaps`** флаг **`dash`** может подмешиваться из **`kitconf["Collaps"]`**; при **`collaps-dash`** — **` ~ 1080p`**, иначе для **`collaps`** — **` ~ 1080p`** или **` ~ 720p`** в зависимости от **`dash`**.

## Конфигурация

Секция в `init.conf`: **`Collaps`** (`ModuleConf` — см. тип в проекте модуля).

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/collaps`** | Основная выдача. |
| **`lite/collaps-search`** | Поиск (spider). |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
