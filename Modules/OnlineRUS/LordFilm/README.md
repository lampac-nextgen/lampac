# LordFilm

Онлайн-источник **LordFilm**: DLE-сайт **`https://gv.lordfilm5.pro`**, вещание через плеер **`api.nextembed.ws`**. В **`Invoke`** всегда возвращается один **`ModuleOnlineItem`** с текущим **`ModuleConf`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**, **`IModuleOnlineSpider`**.

## Глобальный поиск

При **`Loaded`**: **`CoreInit.conf.online.with_search.Add("lordfilm")`** — балансер **`lordfilm`** участвует в общем поиске онлайна (поиск по названию, Kinopoisk API у сайта нет).

## Подпись качества

**`EventListener.OnlineApiQuality`**: для **`e.balanser == "lordfilm"`** возвращается **` ~ 1080p`**, если включён **`dash`**/**`both`**, иначе **` ~ 720p`** (HLS у этого плеера ограничен ~530p).

## Как включить

1. Модуль **уже включён** в репозитории: в [`manifest.json`](manifest.json) стоит **`"enable": true`** и **`"dynamic": true`**. При запуске Lampac подхватывает папку `module/OnlineRUS/LordFilm` и компилирует её на лету движком **Roslyn** — отдельная сборка не нужна (`.csproj` только для локальной разработки).
2. **Перезапустите Lampac** (загрузка состава модулей читается на старте). В логе появится `LordFilm`; в Lampa источник виден как **`LordFilm ~ 720p`** (или **`~ 1080p`**, если включён `dash`/`both`).
3. Секции в `init.conf` **не обязательна** — без неё работают значения по умолчанию из `ModInit.cs`. Добавляйте её только если нужно что-то переопределить.

> Включение/выключение самого модуля (`manifest.json → enable`) требует перезапуска хоста.
> Параметры внутри секции `init.conf` перечитываются на лету (`EventListener.UpdateInitFile`).

## Конфигурация

Секция в `init.conf`: **`LordFilm`** (`ModuleConf : BaseSettings`). Файл `init.conf` лежит рядом с `Core.dll` (шаблон — [`example.init.conf`](../../../example.init.conf), либо `init.yaml` → [`example.init.yaml`](../../../example.init.yaml)). Готовый пример секции — [`init.merge.example.json`](init.merge.example.json):

```jsonc
"LordFilm": {
  "enable": true,                            // вкл/выкл источник
  "displayname": "LordFilm",                 // имя в списке источников
  "displayindex": 575,                       // порядок в списке
  "host": "https://gv.lordfilm5.pro",        // адрес сайта
  "streamproxy": true,                       // для LordFilm проксирование вложенных ссылок включается принудительно
  "httpversion": 2,                          // HTTP/2 к сайту
  "dash": false,                             // false → HLS (~530p, максимум совместимости), true → DASH (до 1080p)
  "both": false,                             // true → показывать и HLS, и DASH отдельными пунктами
  "rch_access": "apk,cors,web",              // где доступен поиск
  "stream_access": "apk,cors,web"            // где доступны ссылки на поток
}
```

Шаблон в коде: **`displayindex = 575`**, **`httpversion = 2`**, **`streamproxy = true`**, **`rch_access = "apk,cors,web"`**, **`stream_access = "apk,cors,web"`**, **`dash = false`**, **`both = false`**.

Другие доступные поля `BaseSettings`: `httptimeout`, `proxy`, `cookie`, `headers`, `headers_stream`, `geostreamproxy`, `rhub*` и др.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/lordfilm`** | Основная выдача: поиск по названию и разбор фильма/сериала. |
| **`lite/lordfilm-search`** | Поиск для спайдера/`similar` (возвращает **`SimilarTpl`**). |
| **`lite/lordfilm/video.m3u8`** | Прокси-переписыватель HLS-мастера: подменяет технические имена дорожек (`rus0`, `ukr4`, `eng6`) на нормальные названия озвучек и принудительно уводит все вложенные ссылки через прокси Lampac. |
| **`lite/lordfilm/video.mpd`** | То же для DASH-манифеста: подменяет `lang="rus0"`… на названия озвучек и переводит `<BaseURL>` на `/proxy-dash/`, чтобы сегменты тоже шли через сервер. |

> Оба потоковых маршрута помечены **`[AllowAnonymous]`** (иначе `accsdb` отдаёт плееру `{"accsdb":true,…}`)
> и принимают только ссылки CDN `*.interkh.com`, чтобы сервер нельзя было использовать как открытый прокси.

## Озвучки и качество

- **Озвучки.** Плеер `nextembed.ws` отдаёт дорожки с техническими подписями (`NAME="rus0"` в HLS, `lang="rus0"` в DASH), поэтому в плеере они видны как `1 / RUS0`, `2 / RUS1` и т.д. Маршруты **`lite/lordfilm/video.m3u8`** и **`lite/lordfilm/video.mpd`** подменяют их на реальные (`Рус. Дублированный`, `HDRezka Studio (укр)`, `Eng.Original`, …) — переключение по-прежнему в плеере, но подписи человеческие.
- **Качество.**
  - **HLS** (по умолчанию, `dash: false`) — максимум **~530p** (варианты `1272x530` и `648x270`). Максимальная совместимость: web / Android / TV.
  - **DASH** (`dash: true`) — до **1080p** (`1920x800`). Но это **AV1 + Opus в WebM** (не H.264), поэтому нужен плеер с поддержкой AV1: современный браузер (Chrome/Edge/Firefox) или Android/TV с AV1. На старых устройствах может не играть.
  - **`both: true`** — у фильма в списке озвучек появятся два пункта: **`HLS`** и **`DASH 1080p`**. У сериалов список серий **не дублируется**: один пункт на серию, а **`HLS` / `DASH 1080p`** выбираются как качество в плеере.

## Как это работает

1. **`LordFilmSession`** обходит JS-челлендж сайта (`get_jhash`), держит cookie-джар (`__js_p_`, `__jhash_`, `__jua_`, `__hash_`, `__lhash_`, `PHPSESSID`) и пересоздаёт сессию примерно раз в 15 минут.
2. Поиск: `POST {host}/search-result/` с `do=search&subaction=search&story=<query>`. Разбор и выбор лучшего совпадения — **`LordFilmParser.PickBest`** (нормализация, год, тип фильм/сериал).
3. Со страницы берётся `//api.nextembed.ws/embed/movie/<id>`, из его HTML извлекаются прямые ссылки **HLS** (приоритет) или **DASH**.
4. Фильм → **`MovieTpl`**; сериал → **`SeasonTpl`** / **`EpisodeTpl`** (сезоны сортируются, дубликаты серий объединяются, дорожки озвучки и субтитры подставляются автоматически).
5. HLS-мастер отдаётся через **`lite/lordfilm/video.m3u8`** (**`RenameAudio`** ставит человеческие подписи дорожкам, **`ProxyUrls`** уводит все вложенные ссылки через Lampac), DASH-манифест — через **`lite/lordfilm/video.mpd`** (**`RenameDashAudio`** + переписывание `<BaseURL>` на `/proxy-dash/`). Что отдавать — решает **`dash`** / **`both`** в `init.conf`.

> **Важно: проксирование принудительное.** CDN (`*.interkh.com`) подписывает сегменты под IP того, кто
> запросил `master.m3u8` / `.mpd`. Модуль качает манифест на сервере, поэтому и сегменты должны идти с сервера:
> `HostStreamProxy(..., force_streamproxy: true)`. Иначе плеер тянет сегменты напрямую со своего IP и получает
> **HTTP 410**. Настройка **`streamproxy`** в секции модуля на это не влияет (можно оставить `false` — работать будет).
> Требуется включённый серверный прокси: **`"serverproxy": { "enable": true }`** в корне `init.conf`.

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`Model.cs`**, **`ModuleConf.cs`**, **`Services/LordFilmSession.cs`**, **`Services/LordFilmParser.cs`** (см. **`manifest.json`**), **`init.merge.example.json`** — пример секции `init.conf`.
