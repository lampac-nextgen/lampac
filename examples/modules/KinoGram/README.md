# KinoGram (пример)

Шаблон онлайн-модуля **KinoGram**: загрузка секции **`KinoGram`** в `init.conf`, базовый **`OnlinesSettings`** (хост **`kinogram.com`** в примере), **`displayindex = 1`**, **`streamproxy`**.

## Файлы проекта

| Файл | Роль |
|------|------|
| `ModInit.cs` | `ModuleInvoke.Init`, подписка на **`UpdateInitFile`**. |
| `Controller.cs` | HTTP-слой (пример маршрутов — см. код). |
| `OnlineApi.cs` | Вызовы API источника. |
| `Model.cs` | Модели данных. |

## manifest.json

Указаны **`Controller.cs`**, **`Model.cs`**, **`ModInit.cs`**, **`OnlineApi.cs`**; **`dynamic`: true**.

## Назначение

Отправная точка для своего онлайн-источника: скопируйте структуру в **`Modules/`** и развивайте парсинг по образцу боевых модулей (**OnlineRUS** и т.д.).
