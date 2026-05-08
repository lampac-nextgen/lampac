# AdminPanel

Веб-**админка** Lampac: статический UI и JSON API для просмотра и правки **`current.conf`**, секций **`init.conf`**, пользователей **`users.json`**. Доступ через атрибут **`[Authorization]`** с редиректом на **`/adminpanel/auth`**.

## Назначение

- Читает и сохраняет конфигурацию рядом с рабочей директорией: **`current.conf`** (основной рабочий файл), **`init.conf`**, **`users.json`** — см. константы в контроллере.
- Группы ключей для UI строятся из **`ConfigSectionGroups`** (в т.ч. каталог секций и «осиротевшие» ключи).

## HTTP

| Маршрут | Описание |
|---------|----------|
| `GET /adminpanel/auth` | Страница входа (**`auth.html`**). |
| `GET /adminpanel` | Главная панель (**`index.html`**). |
| `GET /adminpanel/api/groups` | JSON групп секций по текущему конфигу. |
| `GET /adminpanel/api/groups/catalog` | То же + блок **«Прочее»** для ключей не из каталога. |
| `GET/POST /adminpanel/api/init` | Чтение/сохранение полного init или его частей. |
| `GET/POST /adminpanel/api/init/section/{key}` | Работа с одной секцией. |
| `GET/POST /adminpanel/api/current` | Текущий runtime-конфиг. |
| `GET/POST /adminpanel/api/users-json` | Пользователи **accsdb** / `users.json`. |

Точное поведение методов и тела запросов — в **`AdminPanelController.cs`**.

## Конфигурация модуля

Отдельной секции **`AdminPanel`** в минимальном `ModInit` нет — включение только через **`manifest.json`**.

Авторизация наследует общие механизмы Lampac (**токен**, локальный доступ, **accsdb** — как настроено в хосте).

## Файлы

| Файл | Роль |
|------|------|
| `AdminPanelController.cs` | Все маршруты API и раздача HTML. |
| `ConfigSectionGroups.cs` | Метаданные групп для редактора. |
| `auth.html`, `index.html` | UI в каталоге модуля. |
