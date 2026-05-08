# PornHub

Раздел **Sisi**: интеграция **PornHub** и **PornHub Premium**: два основных профиля настроек в одном модуле плюс опциональные LGBT-каналы. Конфигурация подхватывается через **`ModuleInvoke.DeserializeInit`**. **18+**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`**.

## Каналы (`Invoke`)

Всегда регистрируются:

- **`pornhub.com`** → префикс **`phub`** (настройки **`conf.PornHub`**);
- **`pornhubpremium.com`** → **`phubprem`** (**`conf.PornHubPremium`**, по умолчанию в шаблоне **`enable: false`**).

Если **`args.lgbt`**: добавляются **`phubgay`** и **`phubtrans`** (**`phubgay`**, **`phubsml`**).

Значения по умолчанию для **`PornHub`** / **`PornHubPremium`** заданы в **`ModuleConf.cs`** (хосты **`rt.pornhub.com`** и **`rt.pornhubpremium.com`**, **`streamproxy`**, заголовки **`cookie`**, **`sec-fetch-*`**, отдельные **`headers_image`** / **`headers_stream`**).

## Конфигурация

Секция в `init.conf`: десериализация в тип **`ModuleConf`** (поля **`PornHub`**, **`PornHubPremium`** — оба **`SisiSettings`**). Переопределите параметры в JSON, не меняя структуру, ожидаемую хостом.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`phub`**, **`phubgay`**, **`phubsml`** | Основные и LGBT-разделы (см. **`Controllers/PornHub.cs`**). |
| **`phub/vidosik`** | Карточка видео. |
| **`phubprem`**, **`phubprem/vidosik`** | Premium (см. **`Controllers/PornHubPremium.cs`**). |

## Файлы

**`ModInit.cs`**, **`ModuleConf.cs`**, **`Controllers/PornHub.cs`**, **`Controllers/PornHubPremium.cs`**, **`Service.cs`**.
