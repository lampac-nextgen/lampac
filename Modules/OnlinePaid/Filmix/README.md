# Filmix

Мультипрофильный модуль **Filmix**: **`ModuleInvoke.DeserializeInit(new ModuleConf())`** — три контура (**Filmix**, **FilmixTV**, **FilmixPartner**) с разными префиксами и маршрутами.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Поведение (`Invoke`)

В список попадают до трёх пунктов:

- **`conf.Filmix`** — если не скрыт логикой «бесплатного окна» по времени Киева (**`hidefreeStart`/`hidefreeEnd`** при отсутствии токенов);
- **`conf.FilmixTV`** с префиксом **`filmixtv`**;
- **`conf.FilmixPartner`** с префиксом **`fxapi`**, имя **`Filmix`**.

**`Spider`** регистрирует те же три объекта.

## Глобальный поиск

**`filmix`**, **`filmixtv`**, **`fxapi`**.

## Подпись качества

**`OnlineApiQuality`**: для **`filmix`** учитываются **`pro`** и **`token`** из **`kitconf["Filmix"]`** и **`conf.Filmix`** → **` ~ 2160p`** или **` - 720p`** / **` - 480p`**; для **`fxapi`** и **`filmixtv`** → **` ~ 2160p`**.

## Конфигурация

Секция в `init.conf`: структура **`ModuleConf`** с вложенными **`Filmix`**, **`FilmixTV`**, **`FilmixPartner`** (см. **`Filmix.Models`**).

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/filmix`**, **`lite/filmixpro`** | Основные контуры Filmix. |
| **`lite/filmixtv`** | Filmix TV. |
| **`lite/fxapi`**, **`lite/fxapi/lowlevel/{*uri}`** | Партнёрский API / низкоуровневый прокси. |

## Файлы

**`ModInit.cs`**, контроллеры в **`Controllers/`**, **`Models/`**.
