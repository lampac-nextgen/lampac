# PizdatoeHD

Онлайн **PizdatoeHD** (`ModuleConf`): привязка к **`rezka.ag`** в конструкторе, локальная база **`data/PizdatoeDb.json`** (кеш в памяти вне **`lowMemoryMode`**). Требуется **Playwright**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Условие (`Invoke` / `Spider`)

При **`PlaywrightBrowser.disabled`** → **`null`**. Иначе **`new(conf)`**.

## Глобальный поиск

**`with_search.Add("pizdatoehd")`**.

Фоновый **`Timer`** вызывает **`CronParse.Pizda`** с переменным интервалом (**10–30** мин).

## Конфигурация

Секция в `init.conf`: **`PizdatoeHD`** (`ModuleConf`).

По умолчанию: **`enable = true`**, **`displayindex = 331`**, **`hls = true`**, **`streamproxy = true`**, **`stream_access`**, **`headers_stream`** под **`rezka.ag`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "pizdatoehd"`** — **` ~ 2160p`** или **` ~ 720p`** в зависимости от **`conf.premium`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/pizdatoehd`** | Основная выдача. |
| **`lite/pizdatoehd/movie`**, **`lite/pizdatoehd/movie.m3u8`** | Фильм / HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`data/PizdatoeDb.json`**, **`OnlineApi.cs`**, **`Model.cs`**.
