# Lampac Next Generation

[![Build](https://github.com/lampac-nextgen/lampac/actions/workflows/build.yml/badge.svg)](https://github.com/lampac-nextgen/lampac/actions/workflows/build.yml)
[![Release](https://github.com/lampac-nextgen/lampac/actions/workflows/release.yml/badge.svg)](https://github.com/lampac-nextgen/lampac/actions/workflows/release.yml)
[![GitHub release (latest SemVer)](https://img.shields.io/github/v/release/lampac-nextgen/lampac?label=version)](https://github.com/lampac-nextgen/lampac/releases)
[![GitHub tag (latest SemVer pre-release)](https://img.shields.io/github/v/tag/lampac-nextgen/lampac?include_prereleases&label=pre-release)](https://github.com/lampac-nextgen/lampac/tags)
[![Telegram](https://img.shields.io/badge/Telegram-Chat-2CA5E0?logo=telegram&logoColor=white)](https://t.me/LampacTalks/13998)
[![DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/lampac-nextgen/lampac)

> Самохостируемый backend-сервер для [Lampa](https://github.com/yumata/lampa). Собирает ссылки на публично доступный контент с 70+ источников и отдаёт их Lampa в виде плагинов. Построен на ASP.NET Core (.NET 10).

---

[Lampa](https://github.com/yumata/lampa) — бесплатное приложение для просмотра информации о фильмах. **Lampac NextGen** расширяет его: собирает ссылки с десятков российских, украинских, аниме- и западных источников, отдаёт в виде JSON API, и дополнительно предоставляет TorrServer, DLNA, транскодинг, синхронизацию закладок и многое другое. Порт по умолчанию — **9118**.

<details>
<summary><strong>Возможности</strong></summary>

- **70+ VOD, аниме и 18+ источников** — провайдеры в `Modules/OnlineRUS`, `OnlinePaid`, `OnlineAnime`, `OnlineENG`, `OnlineUKR`, `OnlineGEO`, `Adult/`
- **TorrServer** — встроенный торрент-сервер как подпроцесс
- **DLNA/UPnP** — медиасервер для локальных файлов
- **JacRed** — агрегатор торрент-индексаторов (совместим с Jackett)
- **Transcoding** — транскодинг через FFmpeg (до 5 потоков)
- **Tracks** — управление субтитрами и дорожками (FFprobe)
- **Sync** — кросс-девайсная синхронизация закладок и истории (SQLite)
- **TimeCode** — сохранение позиции воспроизведения
- **TmdbProxy** — локальный кеш TMDB API
- **LampaWeb** — встроенный хостинг Lampa UI (авто-обновление с GitHub)
- **WebLog** — отладка HTTP и Playwright-трафика в реальном времени
- **Playwright** — автоматизация Chromium/Firefox для обхода JS-защит
- **RCH** — WebSocket-реле для клиентов за NAT
- **WAF** — брандмауэр с геоблокировкой, лимитами и защитой от брутфорса
- **GeoIP** — MaxMind GeoLite2 (базы включены в поставку)
- **Горячая перезагрузка конфига** — `init.conf` применяется без перезапуска
- **Многоплатформенность** — `linux/amd64`, `linux/arm64`

</details>

---

## Содержание

- [Lampac Next Generation](#lampac-next-generation)
  - [Содержание](#содержание)
  - [Быстрый старт](#быстрый-старт)
    - [Docker](#docker)
    - [Нативная установка (Linux)](#нативная-установка-linux)
    - [Ручная сборка](#ручная-сборка)
  - [Конфигурация](#конфигурация)
  - [Модули](#модули)
  - [Провайдеры контента](#провайдеры-контента)
  - [API](#api)
  - [Архитектура](#архитектура)
  - [Зависимости](#зависимости)
  - [Структура проекта](#структура-проекта)
  - [Дополнительная документация](#дополнительная-документация)

---

## Быстрый старт

### Docker

**Основной сценарий** — `docker-compose.yaml`, порт **9118**.

```bash
git clone https://github.com/lampac-nextgen/lampac.git
cd lampac

mkdir -p lampac-docker/config lampac-docker/plugins
cp config/example.init.conf lampac-docker/config/init.conf
printf '%s' 'ваш_пароль_root' > lampac-docker/config/passwd

# Раскомментируйте блок volumes в docker-compose.yaml
docker compose up -d
```

По умолчанию все тома закомментированы — контейнер стартует с `init.conf` и `passwd` из образа. Рабочая директория в контейнере — `/lampac`; файлы читаются из её корня, а не из подкаталога `config/`.

<details>
<summary><strong>Тома и сеть</strong></summary>

| Путь на хосте | Путь в контейнере | Назначение |
| --- | --- | --- |
| `./lampac-docker/config/passwd` | `/lampac/passwd` | Пароль root (WebLog, служебные функции) |
| `./lampac-docker/config/init.conf` | `/lampac/init.conf` | Конфигурация |
| `./lampac-docker/plugins/lampainit.js` | `/lampac/plugins/override/lampainit.js` | Переопределение клиентского плагина |
| `./lampac-docker/cache` | `/lampac/cache` | Кеш |
| `./lampac-docker/database` | `/lampac/database` | БД (Sync, TimeCode, SISI) |
| `./lampac-docker/mods/<Name>` | `/lampac/mods/<Name>` | Пользовательские модули |

Сеть по умолчанию — bridge с IP `10.10.10.10`. Для `host`-режима раскомментируйте `network_mode: host` в compose-файле и согласуйте блоки `ports` / `networks`.

Минимальный пример сервиса:

```yaml
services:
  lampac:
    image: ghcr.io/lampac-nextgen/lampac
    ports:
      - "9118:9118"
    shm_size: 1024mb
    restart: unless-stopped
    volumes:
      - ./lampac-docker/config/passwd:/lampac/passwd
      - ./lampac-docker/config/init.conf:/lampac/init.conf
      - ./lampac-docker/plugins/lampainit.js:/lampac/plugins/override/lampainit.js
```

</details>

<details>
<summary><strong>Dev-режим (порт 29118)</strong></summary>

`docker-compose.dev.yaml` — отдельная инстанция на порту **29118** для разработки. Тома включены по умолчанию.

```bash
mkdir -p lampac-docker/config lampac-docker/plugins
cp config/example.init.conf lampac-docker/config/development.init.conf
# В development.init.conf установите: "listen"."port": 29118

printf '%s' 'ваш_пароль_root' > lampac-docker/config/passwd
cp Modules/LampaWeb/plugins/lampainit.js lampac-docker/plugins/lampainit.js

docker compose -f docker-compose.dev.yaml up -d
```

> Оба compose-файла используют `container_name: lampac` — одновременный запуск без правки невозможен.

</details>

<details>
<summary><strong>Управление модулями в Docker</strong></summary>

Состав загружаемых модулей задаётся двумя механизмами:

1. **`BaseModule.SkipModules`** в `init.conf` — имена модулей, которые не загружаются даже если код есть в образе.
2. **`manifest.json`** в каталоге модуля — ключ `"enable": true|false`. Часть модулей ([AdminPanel](Modules/AdminPanel/manifest.json), [ExternalBind](Modules/ExternalBind/manifest.json)) поставляется с `"enable": false`.

Чтобы включить выключенный модуль без пересборки образа: скопируйте его каталог, отредактируйте `manifest.json` и смонтируйте в `/lampac/module/<Name>/` (штатный) или `/lampac/mods/<Name>/` (пользовательский).

</details>

---

### Нативная установка (Linux)

Поддерживаются Debian/Ubuntu, amd64 и arm64. Скрипт устанавливает .NET 10 runtime, создаёт системного пользователя `lampac` и регистрирует systemd-сервис.

```bash
# Установка
curl -fsSL https://raw.githubusercontent.com/lampac-nextgen/lampac/main/install.sh | sudo bash

# Обновление
curl -fsSL https://raw.githubusercontent.com/lampac-nextgen/lampac/main/install.sh | sudo bash -s -- --update

# Проверка обновления без изменений
curl -fsSL https://raw.githubusercontent.com/lampac-nextgen/lampac/main/install.sh | sudo bash -s -- --update --dry-run

# Пред-релиз
curl -fsSL https://raw.githubusercontent.com/lampac-nextgen/lampac/main/install.sh | sudo bash -s -- --pre-release

# Удаление
curl -fsSL https://raw.githubusercontent.com/lampac-nextgen/lampac/main/install.sh | sudo bash -s -- --remove

# Подробный лог при установке (для диагностики ошибок)
curl -fsSL https://raw.githubusercontent.com/lampac-nextgen/lampac/main/install.sh | sudo bash -s -- --verbose

# Подробный лог при обновлении (для диагностики ошибок)
curl -fsSL https://raw.githubusercontent.com/lampac-nextgen/lampac/main/install.sh | sudo bash -s -- --update --verbose
```

```bash
# Управление сервисом
systemctl status lampac
systemctl restart lampac
journalctl -u lampac -f
```

<details>
<summary><strong>Переменные окружения</strong></summary>

| Переменная | По умолчанию | Описание |
| --- | --- | --- |
| `LAMPAC_INSTALL_ROOT` | `/opt/lampac` | Директория установки |
| `LAMPAC_USER` | `lampac` | Системный пользователь |
| `LAMPAC_UID` | `1000` | UID (если занят — выбирается свободный) |
| `LAMPAC_GID` | `1000` | GID (если занят — выбирается свободный) |
| `LAMPAC_PORT` | `9118` | Порт (для подсказки после установки) |
| `LAMPAC_GITHUB_REPO` | `lampac-nextgen/lampac` | GitHub-репозиторий релизов |
| `LAMPAC_DOTNET_ROOT` | `/usr/share/dotnet` | Путь установки .NET |
| `LAMPAC_DOTNET_CHANNEL` | `10.0` | Версия .NET runtime |

</details>

<details>
<summary><strong>Что сохраняется при обновлении (rsync excludes)</strong></summary>

`--update` использует `rsync --delete` — удаляет файлы отсутствующие в релизе, но следующие пути **защищены**:

| Путь | Описание |
| --- | --- |
| `install.sh` | Сам скрипт |
| `init.conf`, `init.yaml` | Конфигурация |
| `mods/` | Пользовательские модули |
| `data/kinoukr.json`, `data/PizdatoeDb.json` | Локальные БД |
| `*.db`, `*.db-shm`, `*.db-wal` | SQLite (Sync, SISI, TimeCode) |
| `logs/`, `cache/` | Логи и кеш |
| `TorrServer`, `torrserver/`, `data/ts/` | TorrServer и его данные |
| `.local/`, `.aspnet/`, `.claude/`, `.config/`, `.playwright/` | Домашние директории пользователя |
| `users.json`, `passwd`, `current.conf`, `database/` | Пользовательские данные |
| `wwwroot/*.js` | Пользовательские JS (темы, кнопки) |
| `wwwroot/lampa-main/` | Кеш Lampa UI |
| `plugins/override/` | Переопределения плагинов |
| `notifications_date.txt` | Состояние уведомлений |
| `excludes.conf` | Файл дополнительных исключений |

Чтобы защитить свои файлы, создайте `excludes.conf` рядом с `Core.dll`:

```bash
# /opt/lampac/excludes.conf — одно исключение на строку, # — комментарий
my_custom_folder/
config/local.conf
*.custom
```

Пути относительно `LAMPAC_INSTALL_ROOT`, для папок — trailing slash, поддерживаются glob-паттерны.

</details>

---

### Ручная сборка

**Требования:** .NET SDK 10.0+

```bash
./build.sh                          # сборка в publish/
RUNTIME_ID=linux-arm64 ./build.sh   # кросс-компиляция

dotnet publish Core/Core.csproj -c Release -o publish   # напрямую
dotnet build NextGen.slnx                               # проверка компиляции всего solution

cd publish && dotnet Core.dll
```

<details>
<summary><strong>Опции build.sh</strong></summary>

| Флаг | Описание |
| --- | --- |
| `--clean` | Удалить bin/ и obj/ из всех проектов |
| `--format` | Форматирование кода (`dotnet format`) |
| `-o /path` | Кастомная директория вывода |
| `-c Debug` | Debug-конфигурация |

</details>

---

## Конфигурация

Конфигурация хранится в `init.conf` (JSON) или `init.yaml` рядом с `Core.dll`. Проверяется каждую секунду и **перезагружается без перезапуска**. Резервные копии — в `database/backup/init/`.

Примеры: [`config/example.init.conf`](config/example.init.conf), [`config/example.init.yaml`](config/example.init.yaml).

<details>
<summary><strong>Основные параметры</strong></summary>

```jsonc
{
  // Сетевые настройки
  "listen": {
    "ip": "0.0.0.0",
    "port": 9118,
    "scheme": "http",
    "version": true,
    "ResponseCancelAfter": 15    // таймаут ответа, секунды
  },

  // Модули
  "BaseModule": {
    "SkipModules": [],           // имена модулей для отключения
    "LoadModules": [".*"],       // whitelist: имя, группа (OnlineUKR), маска (LME.*)
    "ValidateRequest": true,
    "BlockedBots": true
  },

  // Кеш
  "cache": {
    "extend": 180                // продление TTL, минуты
  },

  // Playwright
  "chromium": { "enable": false, "count": 1, "restart": 3600 },
  "firefox":  { "enable": false, "count": 1 },

  // Remote Client Hub (WebSocket-реле для клиентов за NAT)
  "rch": { "enable": false, "requiredConnected": 1 },

  // Логирование в файл (logs/, 14 дней)
  "serilog": false,

  // Управление памятью GC
  "GC": {
    "Concurrent": true,
    "ConserveMemory": 0,
    "HighMemoryPercent": 90,
    "RetainVM": false
  },

  // Шифрование потоков
  "kit": { "aesgcmkeyName": "" }
}
```

</details>

<details>
<summary><strong>WAF и безопасность</strong></summary>

```jsonc
{
  "WAF": {
    "enable": true,
    "countryAllow": ["RU", "UA", "BY"],   // геоблокировка (пустой — все страны)
    "whiteIps": ["192.168.1.0/24"],        // белый список IP/CIDR
    "bruteForceProtection": true,
    "limit_map": {
      "/lite/": 10,
      "/externalids": 10
    }
  }
}
```

</details>

<details>
<summary><strong>Аутентификация (accsdb)</strong></summary>

```jsonc
{
  "accsdb": {
    "enable": true,
    "accounts": "user1:2026-12-31,user2:2027-06-01",
    // или подробный формат:
    "users": [
      { "id": "user1", "expires": "2026-12-31" },
      { "id": "user2", "expires": "2027-06-01" }
    ]
  }
}
```

</details>

<details>
<summary><strong>VOD, SISI и плагины Lampa UI</strong></summary>

```jsonc
{
  // VOD-плагин
  "online": {
    "name": "Lampac NextGen",
    "version": true,
    "btn_priority_forced": true
  },

  // SISI (18+)
  "sisi": {
    "lgbt": false,
    "NextHUB": true,
    "history": { "enable": false }
  },

  // Статистика (/stats/*)
  "openstat": { "enable": false },

  // Плагины Lampa UI
  "LampaWeb": {
    "initPlugins": {
      "online": true, "sisi": true, "torrserver": true,
      "timecode": true, "jacred": true, "tmdbProxy": true,
      "cubProxy": true, "pirate_store": true
    }
  }
}
```

</details>

<details>
<summary><strong>Конфигурация провайдеров (пример)</strong></summary>

Каждый провайдер настраивается в своём разделе `init.conf`:

```jsonc
{
  "Rezka":  { "enable": true, "host": "https://rezka.ag", "priority": 1 },
  "Filmix": { "enable": true, "host": "https://filmix.biz", "token": "TOKEN", "priority": 2 },
  "KinoPub":{ "enable": true, "token": "TOKEN" },
  "Kodik":  { "enable": true, "token": "TOKEN" }
}
```

</details>

---

## Модули

По умолчанию в `SkipModules` ([`config/base.conf`](config/base.conf)): **Catalog, DLNA, Sync, SyncEvents, Storage, Tracks, Transcoding, WebLog, TelegramAuth, TelegramAuthBot**. WAF и accsdb тоже отключены по умолчанию.

> [!WARNING]
> Модули **DLNA**, **Tracks**, **Transcoding** и **Catalog** не выполняют экранирование входящих запросов. Не включайте их на публично доступном VPS без ограничения доступа через firewall или reverse proxy.

| Модуль | По умолч. | Описание |
| --- | :---: | --- |
| **Online** | ✅ | VOD-ядро: плагин `/online.js`, агрегатор `/lite/*`. Провайдеры в `Modules/Online*/`. WAF: 10 req/s. [README](Online/README.md) |
| **SISI** | ✅ | 18+: плагин `/sisi.js`, SQLite (история, закладки). Платформы в `Modules/Adult/*`. [README](SISI/README.md) |
| **LampaWeb** | ✅ | Хостинг Lampa UI. Авто-обновление с GitHub каждые 90 мин. |
| **TorrServer** | ✅ | Управление процессом TorrServer, прокси `/ts/`. Случайный пароль за сессию. |
| **JacRed** | ✅ | Агрегатор торрент-индексаторов (Rutor, Kinozal, RuTracker, NNMClub, Toloka, Bitru и др.). |
| **NextHUB** | ✅ | 18+ витрина на YAML (`Modules/NextHUB/sites/`). Маршрут `/nexthub`. WAF: 5 req/s. [README](Modules/NextHUB/README.md) |
| **TmdbProxy** | ✅ | Локальный кеш TMDB API (`cache/tmdb/`). |
| **CubProxy** | ✅ | HTTP/HTTPS прокси с файловым кешем (`cache/cub/`). |
| **TimeCode** | ✅ | Сохранение и восстановление позиции воспроизведения. SQLite. |
| **Kit** | ✅ | Шифрование потоков (CryptoKit), конфиг `kit` в `init.conf`. |
| **PidTor** | ✅ | Источник PidTor, маршрут `/lite/pidtor`. |
| **Catalog** | ⛔ | Браузер каталогов из YAML (`sites/`). Маршрут `/catalog/`. Только в доверенной сети. |
| **DLNA** | ⛔ | DLNA/UPnP медиасервер. Форматы: mp4, mkv, ts, webm, avi, flac и др. Только в доверенной сети. |
| **Sync** | ⛔ | Синхронизация закладок и истории. Эндпоинты `/storage/`, `/bookmark/`. SQLite. |
| **SyncEvents** | ⛔ | Трансляция событий синхронизации через WebSocket (NwsEvents). |
| **Storage** | ⛔ | Хранилище данных для Sync, NWS (`onlyreg`). |
| **Tracks** | ⛔ | Субтитры и дорожки (`database/tracks/`), интеграция FFprobe (`/ffprobe`). Только в доверенной сети. |
| **Transcoding** | ⛔ | HLS/DASH транскодинг FFmpeg. До 5 потоков, таймаут 5 мин. `cache/transcoding/`. Только в доверенной сети. |
| **WebLog** | ⛔ | Страница `/weblog`: поток HTTP и Playwright-событий через WebSocket. Требует пароль root. Не включайте публично. |
| **WatchTogether** | ⛔ | Синхронный просмотр (WebSocket-комнаты). |
| **AdminPanel** | ⛔ (manifest) | Веб-админка и JSON API (`/adminpanel/`). `"enable": false` в [manifest.json](Modules/AdminPanel/manifest.json). |
| **ExternalBind** | ⛔ (manifest) | Привязка Lite/Online для удалённых URL (FilmixPro, Rezka, KinoPub). [README](Modules/ExternalBind/README.md) |
| **TelegramAuth** | ⛔ | HTTP API `/tg/auth/…`, интеграция с accsdb. [README](Modules/Community/TelegramAuth/README.md) |
| **TelegramAuthBot** | ⛔ | Telegram-бот для привязки устройств (long polling). [README](Modules/Community/TelegramAuthBot/README.md) |

<details>
<summary><strong>Пользовательские модули</strong></summary>

Создайте подкаталог в `mods/` с `manifest.json` и `.cs`-файлами — Roslyn скомпилирует при запуске:

```json
{
  "name": "MyModule",
  "description": "Описание модуля",
  "version": "1.0",
  "enable": true,
  "dynamic": true
}
```

`dynamic: true` — горячая пересборка при изменении `.cs` файлов без перезапуска сервера. Ориентируйтесь на примеры в `Modules/*/manifest.json`.

</details>

---

## Провайдеры контента

<details>
<summary><strong>VOD — онлайн-кино</strong></summary>

| Провайдер | Группа | Примечания |
| --- | --- | --- |
| `Alloha` | OnlinePaid | |
| `CDNvideohub` | OnlineRUS | |
| `Collaps` | OnlineRUS | Включая DASH-вариант |
| `FanCDN` | OnlineRUS | |
| `Filmix` | OnlinePaid | FilmixPartner, FilmixTV варианты |
| `FlixCDN` | OnlineRUS | |
| `GetsTV` | OnlinePaid | |
| `HDVB` | OnlineRUS | |
| `IptvOnline` | OnlinePaid | |
| `iRemux` | OnlinePaid | |
| `Kinobase` | OnlineRUS | |
| `Kinogo` | OnlineRUS | |
| `Kinotochka` | OnlineRUS | |
| `Kinoflix` / `AsiaGe` / `Geosaitebi` | OnlineGEO | |
| `KinoPub` | OnlinePaid | Требует токен |
| `LeProduction` | OnlineRUS | |
| `Mirage` | OnlineRUS | |
| `PiTor` | Online | Стриминг через торрент |
| `PizdatoeHD` | OnlineRUS | |
| `Rezka` / `RezkaPremium` | OnlinePaid | |
| `RutubeMovie` | OnlineRUS | |
| `Spectre` | OnlineRUS | |
| `VeoVeo` | OnlineRUS | Офлайн БД `data/veoveo.json` |
| `Vibix` | OnlineRUS | |
| `VideoDB` / `Videoseed` | OnlineRUS | Маршруты `/lite/videodb`, `/lite/videoseed` |
| `VkMovie` | OnlineRUS | |
| `VoKino` | OnlinePaid | |
| `Zetflix` / `ZetflixDB` | OnlineRUS | |

</details>

<details>
<summary><strong>Аниме (12 источников)</strong></summary>

| Провайдер | Сервис |
| --- | --- |
| `AniLiberty` | AniLiberty |
| `AniLibria` | AniLibria |
| `AniMedia` | AniMedia |
| `AnimeGo` | AnimeGo |
| `AnimeLib` | AnimeLib |
| `Animebesst` | AnimeBesst |
| `Animevost` | Animevost |
| `Dreamerscast` | Dreamerscast |
| `Kodik` | Kodik (универсальный, VOD + аниме) |
| `Mikai` | Mikai |
| `MoonAnime` | MoonAnime |
| `AnimeON` | AnimeON |

</details>

<details>
<summary><strong>Англоязычный контент (10 источников)</strong></summary>

| Провайдер | Сервис |
| --- | --- |
| `AutoEmbed` | AutoEmbed |
| `HydraFlix` | HydraFlix |
| `MovPI` | MovPI |
| `PlayEmbed` | PlayEmbed |
| `RgShows` | RgShows |
| `SmashyStream` | SmashyStream |
| `TwoEmbed` | TwoEmbed |
| `VidLink` | VidLink |
| `VidSrc` | VidSrc |
| `Videasy` | Videasy |

</details>

<details>
<summary><strong>Украинские CDN (8 источников)</strong></summary>

| Провайдер | Сервис |
| --- | --- |
| `Ashdi` | Ashdi |
| `BamBoo` | BamBoo |
| `Eneyida` | Eneyida |
| `HdvbUA` | HDVB (UA) |
| `Kinoukr` | KinoUkr (офлайн БД `data/kinoukr.json`, ~130k записей) |
| `Tortuga` | Tortuga |
| `UAFilm` | UAFilm |
| `UaKino` | UaKino |

</details>

<details>
<summary><strong>SISI — контент 18+ (15 платформ)</strong></summary>

| Платформа | Маршруты |
| --- | --- |
| BongaCams | `/bgs` |
| Chaturbate | `/chu` |
| Ebalovo | `/elo` |
| Eporner | `/epr` |
| HQporner | `/hqr` |
| PornHub | `/phub`, `/phubgay`, `/phubsml` |
| PornHubPremium | `/phubprem` |
| Porntrex | `/ptx` |
| Runetki | `/runetki` |
| Spankbang | `/sbg` |
| Tizam | `/tizam` |
| Xhamster | `/xmr`, `/xmrgay`, `/xmrsml` |
| Xnxx | `/xnx` |
| Xvideos | `/xds`, `/xdsgay`, `/xdssml` |
| XvideosRED | `/xdsred` |

</details>

<details>
<summary><strong>NextHUB — витрина 18+ на YAML</strong></summary>

Модуль **NextHUB** — витрина сайтов 18+ по YAML-описаниям из `Modules/NextHUB/sites/` (имя файла без расширения = значение параметра `plugin` в URL).

- **Маршрут:** `GET /nexthub?plugin=<name>` — параметры: `plugin` (обязателен), опционально `search`, `sort`, `cat`, `model`, `pg`
- **Конфиг:** `NextHUB.sites_enabled` — если задан, допускает только плагины, имя которых содержится в строке (например `pornhub,beeg`)
- **Переопределения:** `Modules/NextHUB/override/{plugin}.yaml` или `_.yaml` — слияние поверх базового YAML
- **WAF:** лимит 5 req/s на `/nexthub`

[Подробнее — README](Modules/NextHUB/README.md)

</details>

---

## API

<details>
<summary><strong>Core</strong></summary>

| Метод | Путь | Описание |
| --- | --- | --- |
| `GET` | `/version` | Версия сервера |
| `GET` | `/api/headers` | Заголовки текущего запроса |
| `GET` | `/api/geo[?ip=]` | GeoIP-локация IP-адреса |
| `GET` | `/api/myip` | IP-адрес клиента |
| `GET` | `/api/chromium/ping` | Пинг Playwright (`pong`) |
| `POST` | `/rch/result?id=` | RCH-реле: запись результата (макс. 10 МБ) |
| `POST` | `/rch/gzresult?id=` | RCH-реле: запись gzip-результата (макс. 10 МБ) |
| `WS` | `/ws` | NativeWebSocket для RCH push |
| `GET` | `/stats/gc` | Память: heap, WorkingSet, PrivateMemory |
| `GET` | `/stats/request` | Счётчики запросов, активные соединения, топ медленных путей |
| `GET` | `/stats/tempdb` | Кеши и пулы буферов |
| `GET` | `/stats/threadpool` | Диагностика ThreadPool |
| `GET` | `/stats/browser/context` | Состояние Playwright (контексты, счётчики) |

> `/stats/*` (кроме `/stats/gc`) доступны только при `openstat.enable: true`.

</details>

<details>
<summary><strong>Online / SISI / Модули</strong></summary>

**Online (VOD)**

| Метод | Путь | Описание |
| --- | --- | --- |
| `GET` | `/online.js` | Lampa VOD-плагин |
| `GET` | `/online/js/{token}` | Плагин с авторизацией по токену |
| `GET` | `/lite/{provider}` | Список источников от провайдера |
| `GET` | `/externalids` | Маппинг ID (TMDB ↔ KinoPoisk и т.д.) |
| `GET` | `/lifeevents` | SSE-поток событий здоровья провайдеров |

**SISI (18+)**

| Метод | Путь | Описание |
| --- | --- | --- |
| `GET` | `/sisi.js` | Lampa SISI-плагин |
| `GET` | `/sisi/js/{token}` | Плагин с авторизацией по токену |
| `GET` | `/{provider}` | Контент платформы (напр. `/phub`, `/xnx`) |
| `GET` | `/sisi/bookmark` | Управление закладками |
| `GET` | `/sisi/history` | История просмотров |

**Модули**

| Метод | Путь | Описание |
| --- | --- | --- |
| `GET` | `/catalog/{site}/…` | Каталог сайтов |
| `GET` | `/dlna/…` | DLNA медиасервер |
| `GET` | `/storage/…` | Хранилище Sync |
| `GET` | `/bookmark/…` | Закладки Sync |
| `GET` | `/timecode/…` | Позиции воспроизведения |
| `GET` | `/tmdb/…` | TMDB прокси/кеш |
| `GET` | `/transcoding/…` | HLS/DASH транскодинг |
| `GET` | `/ffprobe` | Метаданные дорожек (FFprobe) |
| `GET` | `/nexthub` | NextHUB: браузер 18+ по YAML |
| `GET` | `/nexthub/vidosik` | NextHUB: просмотр элемента (`uri`, `related`) |
| `GET` | `/ts/…` | TorrServer |
| `GET` | `/weblog` | Отладка HTTP/Playwright в реальном времени |

</details>

---

## Архитектура

```text
┌─────────────────────────────────────────────────────────────────┐
│  Core  (ASP.NET Core Web Host, порт 9118)                       │
│  Program.cs → Startup.cs → Middleware Pipeline                  │
├────────────────────┬────────────────────────────────────────────┤
│  Shared (lib)      │  BaseController, CoreInit (конфиг),        │
│                    │  модели, сервисы, Playwright, HTTP-пулы    │
├────────────────────┴────────────────────────────────────────────┤
│  Динамически загружаемые модули                                 │
│  ┌─────────┐ ┌─────────┐ ┌──────────┐ ┌───────────────────┐    │
│  │ Online  │ │  SISI   │ │ Catalog  │ │    LampaWeb       │    │
│  │(VOD API)│ │ + Adult │ │(каталог) │ │(Lampa UI)         │    │
│  └─────────┘ └─────────┘ └──────────┘ └───────────────────┘    │
│  ┌─────────┐ ┌─────────┐ ┌──────────┐ ┌───────────────────┐    │
│  │TorrServr│ │  DLNA   │ │  JacRed  │ │   Transcoding     │    │
│  └─────────┘ └─────────┘ └──────────┘ └───────────────────┘    │
│  ┌─────────┐ ┌─────────┐ ┌──────────┐ ┌───────────────────┐    │
│  │TmdbProxy│ │  Sync   │ │ TimeCode │ │     Tracks        │    │
│  │CubProxy │ │ WebLog  │ │ NextHUB  │ │  AdminPanel, Kit  │    │
│  └─────────┘ └─────────┘ └──────────┘ └───────────────────┘    │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  Modules/OnlineRUS · OnlinePaid · OnlineAnime · OnlineENG │  │
│  │  OnlineUKR · OnlineGEO  — по одному проекту на провайдера │  │
│  │  Modules/Adult/* — платформы 18+                          │  │
│  │  Modules/Community/* — TelegramAuth, TelegramAuthBot      │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

| Слой | Описание |
| --- | --- |
| **Core** | Точка входа, Middleware Pipeline, `ApiController`. [README](Core/README.md) |
| **Shared** | Модели, контроллеры, конфигурация, HTTP-пулы, Roslyn. [README](Shared/README.md) |
| **Online** | VOD-ядро: `/online.js`, `/lite/*`, провайдеры в `Modules/Online*/`. [README](Online/README.md) |
| **SISI** | 18+-ядро: `/sisi.js`, SQLite. Платформы в `Modules/Adult/`. [README](SISI/README.md) |
| **Modules/** | Функциональные модули, прокси, Community, Sync и др. |

<details>
<summary><strong>Загрузка модулей, Roslyn и middleware</strong></summary>

**Загрузка модулей:**

Скомпилированные сборки загружаются из `runtimes/references/`. Исходники модулей из `module/` и `mods/` компилирует **Roslyn** (`CSharpEval`) при запуске — это даёт горячую подгрузку и пользовательские оверлеи.

Порядок загрузки:
1. Сначала `mods/` (пользовательские), затем `module/` (встроенные)
2. Фильтрация: `SkipModules`, `LoadModules` (regex/имя/группа), флаг `enable` в manifest.json
3. `dynamic: true` → горячая пересборка при изменении `.cs` файлов
4. `IModuleConfigure.Configure` → регистрация в DI
5. `IModuleLoaded.Loaded` → вызов после старта приложения

**Middleware Pipeline:**

```
ForwardedHeaders → BaseMod → ModHeaders → RequestInfo
  → [/nws WebSocket] → Routing → Compression
  → ProxyImg → StaticFiles → WAF → Authorization
  → Accsdb → Controllers
```

**Конфигурация:**

- `init.conf` / `init.yaml` — основной конфиг
- `base.conf` — дефолты (fallback)
- Горячая перезагрузка: watcher каждые ~1 сек, бэкапы в `database/backup/init/`

</details>

---

## Зависимости

<details>
<summary><strong>NuGet пакеты (.NET 10.0)</strong></summary>

| Пакет | Версия | Назначение |
| --- | --- | --- |
| `Microsoft.CodeAnalysis.CSharp` + `.Scripting` | 5.0.0 | Roslyn: компиляция модулей на лету |
| `Microsoft.Playwright` | 1.50.0 | Chromium/Firefox автоматизация |
| `HtmlAgilityPack` | 1.12.4 | Парсинг HTML |
| `HtmlKit` | 1.2.0 | Парсинг HTML |
| `MaxMind.GeoIP2` | 5.4.1 | GeoIP (базы `GeoLite2-*.mmdb` включены в поставку) |
| `Newtonsoft.Json` | 13.0.4 | JSON-сериализация |
| `Microsoft.EntityFrameworkCore` (+ Sqlite, Design) | 10.0.2 | ORM для SQLite (Sync, TimeCode, SISI, ExternalIds) |
| `Microsoft.Extensions.DependencyModel` | 10.0.2 | Загрузка зависимостей при динамической компиляции |
| `Microsoft.IO.RecyclableMemoryStream` | 3.0.1 | Пул памяти для потоков |
| `NetVips` / `NetVips.Native` | 3.2.0 / 8.18.0 | Обработка изображений (libvips) |
| `YamlDotNet` | 16.3.0 | Парсинг YAML-конфигурации |
| `Serilog.AspNetCore` + `.Sinks.File` | 9.0.0 / 7.0.0 | Структурное логирование |
| `System.Management` | 10.0.2 | Информация об ОС и железе |

</details>

---

## Структура проекта

<details>
<summary><strong>Дерево каталогов</strong></summary>

```text
lampac/
├── Core/                       # Точка входа, middleware, загрузка модулей
│   ├── Program.cs              # Запуск, инициализация
│   ├── Startup.cs              # DI, HTTP-клиенты, загрузка модулей
│   ├── Controllers/            # ApiController, RchApiEndpoints
│   ├── Middlewares/            # WAF, Accsdb, BaseMod, ProxyImg и другие
│   ├── Services/               # NativeWebSocket, CronCacheWatcher
│   ├── data/                   # GeoIP базы, статические JSON-базы
│   ├── plugins/                # JS-плагины (RCH, NWS)
│   └── wwwroot/                # Статика (SISI UI, stats и др.)
├── Shared/                     # Общая библиотека
│   ├── CoreInit.cs             # Загрузка и hot-reload конфигурации
│   ├── BaseController.cs       # Базовый контроллер
│   ├── Models/                 # Общие модели данных
│   └── Services/               # HTTP, кеш, Playwright, GeoIP, Roslyn
├── Online/                     # VOD-ядро (/online.js, /lite/*, externalids)
├── SISI/                       # 18+-ядро (/sisi.js, SQLite, закладки)
├── Modules/
│   ├── AdminPanel/             # Веб-админка (manifest: enable: false)
│   ├── Adult/                  # Платформы 18+ (15 источников)
│   ├── Catalog/                # Каталог сайтов (YAML)
│   ├── Community/              # TelegramAuth, TelegramAuthBot
│   ├── DLNA/                   # DLNA/UPnP медиасервер
│   ├── ExternalBind/           # Привязка URL (manifest: enable: false)
│   ├── JacRed/                 # Агрегатор торрент-индексаторов
│   ├── Kit/                    # Криптография
│   ├── LampaWeb/               # Хостинг Lampa UI
│   ├── NextHUB/                # 18+ витрина на YAML, sites/*.yaml
│   ├── OnlineAnime/            # 12 аниме-источников
│   ├── OnlineENG/              # 10 англоязычных источников
│   ├── OnlineGEO/              # 3 грузинских источника
│   ├── OnlinePaid/             # 8 платных VOD-источников
│   ├── OnlineRUS/              # 20 российских CDN
│   ├── OnlineUKR/              # 8 украинских источников
│   ├── PidTor/                 # PidTor источник
│   ├── Proxy/                  # CubProxy, TmdbProxy, CorsMedia, Corseu
│   ├── Sync/                   # Sync, SyncEvents, Storage, TimeCode
│   ├── TorrServer/             # Управление TorrServer
│   ├── Tracks/                 # Субтитры и дорожки (FFprobe)
│   ├── Transcoding/            # FFmpeg транскодинг
│   ├── WatchTogether/          # Синхронный просмотр
│   └── WebLog/                 # Отладочный лог HTTP/Playwright
├── TestModules/                # Примеры модулей → mods/ при publish
├── config/
│   ├── base.conf               # Дефолтные значения
│   ├── example.init.conf       # Пример конфига (JSON)
│   └── example.init.yaml       # Пример конфига (YAML)
├── docker-compose.yaml         # Production (порт 9118)
├── docker-compose.dev.yaml     # Dev (порт 29118)
├── Dockerfile                  # Multi-arch образ (amd64, arm64)
├── build.sh                    # dotnet publish Core/Core.csproj → publish/
├── install.sh                  # Нативная установка Linux
└── NextGen.slnx                # Solution (128+ проектов)
```

После `dotnet publish`: исходники модулей — в `module/` (Online, SISI, Modules), TestModules — в `mods/`, DLL-зависимости — в `runtimes/references/`.

</details>

---

## Дополнительная документация

| Документ | О чём |
| --- | --- |
| [Core/README.md](Core/README.md) | `Program`/`Startup`, middleware, загрузка `module/` и `mods/` |
| [Shared/README.md](Shared/README.md) | `CoreInit`, контроллеры, `CSharpEval`, кеш, HTTP, Playwright |
| [Online/README.md](Online/README.md) | VOD-ядро, `/online.js`, `/lite/`, PiTor, Externalids |
| [SISI/README.md](SISI/README.md) | 18+-ядро, платформы `Modules/Adult/*`, таблица маршрутов |
| [Modules/NextHUB/README.md](Modules/NextHUB/README.md) | YAML-сайты, `/nexthub`, конфиг, WAF |
| [Modules/Community/README.md](Modules/Community/README.md) | Telegram-авторизация, клиент Lampa, API |
| [Modules/Community/TelegramAuth/README.md](Modules/Community/TelegramAuth/README.md) | HTTP API `/tg/auth/…`, accsdb, хранилище |
| [Modules/Community/TelegramAuthBot/README.md](Modules/Community/TelegramAuthBot/README.md) | Long polling-бот, команды, конфиг |
| [Modules/ExternalBind/README.md](Modules/ExternalBind/README.md) | Привязка Lite/Online, флаг локального IP |

---

[![Star History Chart](https://api.star-history.com/svg?repos=lampac-nextgen/lampac&type=Date)](https://star-history.com/#lampac-nextgen/lampac&Date)
