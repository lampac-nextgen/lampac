# Lampac Next Generation

[![Build](https://github.com/lampac-nextgen/lampac/actions/workflows/build.yml/badge.svg)](https://github.com/lampac-nextgen/lampac/actions/workflows/build.yml)
[![Release](https://github.com/lampac-nextgen/lampac/actions/workflows/release.yml/badge.svg)](https://github.com/lampac-nextgen/lampac/actions/workflows/release.yml)
[![GitHub release (latest SemVer)](https://img.shields.io/github/v/release/lampac-nextgen/lampac?label=version)](https://github.com/lampac-nextgen/lampac/releases)
[![GitHub tag (latest SemVer pre-release)](https://img.shields.io/github/v/tag/lampac-nextgen/lampac?include_prereleases&label=pre-release)](https://github.com/lampac-nextgen/lampac/tags)

> **Самохостируемый backend-сервер для расширения [Lampa](https://github.com/yumata/lampa)** — собирает ссылки на публично доступный контент с десятков российских, украинских, СНГ-сервисов, аниме-источников и западных платформ и передаёт их Lampa в виде плагинов. Построен на ASP.NET Core (.NET 10).

---

## AI Документация

[![DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/lampac-nextgen/lampac)

## Telegram Чат

[Lampac NextGen](https://t.me/LampacTalks/13998)

## Содержание

1. [AI Документация](#ai-документация)
2. [Telegram Чат](#telegram-чат)
3. [Обзор проекта](#обзор-проекта)
4. [Архитектура](#архитектура)
5. [Возможности](#возможности)
6. [Быстрый старт](#быстрый-старт)
   - [Docker](#docker)
   - [Нативная установка (Linux)](#нативная-установка-linux)
   - [Ручная сборка](#ручная-сборка)
7. [Конфигурация](#конфигурация)
   - [Пример конфигурации в репозитории](#пример-конфигурации-в-репозитории)
   - [Основные параметры](#основные-параметры)
   - [Аутентификация (accsdb)](#аутентификация-accsdb)
   - [WAF и безопасность](#waf-и-безопасность)
   - [Провайдеры онлайн-кино](#провайдеры-онлайн-кино)
8. [Модули](#модули)
9. [Провайдеры контента](#провайдеры-контента)
   - [VOD (онлайн-кино)](#vod-онлайн-кино)
   - [Аниме](#аниме)
   - [Англоязычный контент](#англоязычный-контент)
   - [Украинские CDN](#украинские-cdn)
   - [SISI (контент 18+)](#sisi-контент-18)
   - [NextHUB (18+)](#nexthub-18)
10. [API-эндпоинты](#api-эндпоинты)
11. [Зависимости](#зависимости)
12. [Структура проекта](#структура-проекта)

---

## Обзор проекта

[Lampa](https://github.com/yumata/lampa) — бесплатное приложение для просмотра информации о фильмах, новинках и популярных релизах. Оно использует публичные ссылки и не распространяет контент через собственные серверы — вся информация отображается исключительно в познавательных целях.

**Lampac Next Generation** — самохостируемый backend-сервер для расширения [Lampa](https://github.com/yumata/lampa) через плагины. Он собирает ссылки на публично доступный контент с десятков российских, украинских, СНГ-сервисов, аниме-источников и западных платформ, отдаёт их Lampa в виде структурированных JSON API-ответов и дополнительно предоставляет: проксирование метаданных TMDB, встроенный TorrServer, DLNA/UPnP-медиасервер, транскодинг через FFmpeg, управление субтитрами и синхронизацию закладок между устройствами. Запускается на порту **9118**.

---

## Архитектура

```text
┌────────────────────────────────────────────────────────────────┐
│  Core  (ASP.NET Core Web Host, порт 9118)                      │
│  Program.cs → Startup.cs → Middleware Pipeline                 │
├────────────────────┬───────────────────────────────────────────┤
│  Shared (lib)      │  BaseController, CoreInit (конфиг),       │
│                    │  модели, сервисы, Playwright, HTTP-пулы   │
├────────────────────┴───────────────────────────────────────────┤
│  Динамически загружаемые модули                                │
│  ┌─────────┐ ┌─────────┐ ┌──────────┐ ┌───────────────────┐    │
│  │ Online  │ │  SISI   │ │ Catalog  │ │    LampaWeb       │    │
│  │(VOD API)│ │ (18+)   │ │(каталог) │ │(Lampa UI)         │    │
│  └─────────┘ └─────────┘ └──────────┘ └───────────────────┘    │
│  ┌─────────┐ ┌─────────┐ ┌──────────┐ ┌───────────────────┐    │
│  │TorrServr│ │  DLNA   │ │  JacRed  │ │   Transcoding     │    │
│  └─────────┘ └─────────┘ └──────────┘ └───────────────────┘    │
│  ┌─────────┐ ┌─────────┐ ┌──────────┐ ┌───────────────────┐    │
│  │TmdbProxy│ │  Sync   │ │ TimeCode │ │     Tracks        │    │
│  └─────────┘ └─────────┘ └──────────┘ └───────────────────┘    │
│  ┌─────────┐ ┌─────────┐ ┌──────────┐ ┌───────────────────┐    │
│  │CubProxy │ │ WebLog  │ │ NextHUB  │ │ OnlinePacks       │    │
│  │         │ │         │ │ /nexthub │ │ RUS/Anime/ENG/UKR │    │
│  └─────────┘ └─────────┘ └──────────┘ └───────────────────┘    │
└────────────────────────────────────────────────────────────────┘
```

**Слои:**

| Слой | Описание |
| --- | --- |
| **Core** | Точка входа, Middleware Pipeline, основной `ApiController` |
| **Shared** | Общие модели, базовые контроллеры, конфигурация, HTTP-пулы |
| **Online** | Ядро VOD: основные провайдеры; часть источников вынесена в **OnlinePacks** (`Modules/OnlinePacks/`: RUS, Anime, ENG, UKR, GEO) |
| **SISI** | Модуль контента 18+: 15 платформ (см. `SISI/README.md`) |
| **Modules/** | Остальные функциональные модули (каталог, прокси, TorrServer, NextHUB, Sync/Storage и др.) + упаковки Online |

Сборочные модули (Online, SISI, Catalog, прокси, синхронизация и др.) подключаются как **скомпилированные сборки** из каталога `runtimes/references/` при старте процесса (`Core.dll`). Параллельно в образе/публикации присутствуют каталоги **`module/`** и **`mods/`**: туда копируются исходники из `Modules/`, `Online/`, `SISI/` и `TestModules/` (см. `Core.csproj`) — их компилирует **Roslyn** (`CSharpEval`) при запуске, что даёт горячую подгрузку и пользовательские оверлеи. Дополнительно пользователь может положить свои модули в **`mods/`** на машине с уже развёрнутым сервером (рядом с `Core.dll`), не пересобирая solution целиком.

---

## Возможности

- **~54 VOD- и аниме-источника** — ядро `Online/` (Rezka, Filmix, KinoPub и др.) + пакеты **OnlinePacks** (`OnlineRUS`, `OnlineAnime`, `OnlineENG`, `OnlineUKR`, `OnlineGEO`): HDVB, Collaps, Kodik, VidSrc, Kinoflix и другие
- **10 аниме-провайдеров** в `OnlineAnime` — AniLibria, AniLiberty, AnimeGo, AniMedia, Kodik и др.
- **Англоязычный контент** — VidSrc, AutoEmbed, SmashyStream, TwoEmbed, VidLink и другие
- **SISI (18+)** — PornHub, XVideos, XHamster, Chaturbate, BongaCams и другие
- **TorrServer** — встроенный торрент-сервер, управляемый как подпроцесс
- **DLNA/UPnP** — медиасервер для локальных файлов
- **JacRed** — агрегатор торрент-индексаторов (совместим с Jackett)
- **Transcoding** — транскодинг через FFmpeg (до 5 одновременных заданий)
- **Tracks** — управление субтитрами и звуковыми дорожками, интеграция с FFprobe
- **Sync** — кросс-девайсная синхронизация закладок и истории (SQLite)
- **TimeCode** — сохранение позиции воспроизведения (продолжение с места)
- **TmdbProxy** — локальный кеш TMDB API для снижения нагрузки
- **LampaWeb** — встроенный хостинг Lampa UI (авто-обновление с GitHub)
- **WebLog** — веб-страница отладки исходящего HTTP и Playwright-трафика в реальном времени (WebSocket NWS, пароль root)
- **Playwright** — автоматизация браузера (Chromium/Firefox) для обхода JS-защит
- **RCH (Remote Client Hub)** — WebSocket-реле для клиентов за NAT
- **Горячая перезагрузка конфига** — изменения в `init.conf` применяются без перезапуска
- **WAF** — встроенный брандмауэр с ограничением запросов, геоблокировкой и защитой от брутфорса
- **GeoIP** — определение страны и ASN (MaxMind GeoLite2, базы включены в поставку)
- **Многоплатформенность** — `linux/amd64`, `linux/arm64`

---

## Быстрый старт

### Docker

Compose-файлы лежат в **корне репозитория** (`docker-compose.yaml` — обычный запуск, `docker-compose.dev.yaml` — разработка с другим портом и конфигом).

```bash
# Клонировать репозиторий
git clone https://github.com/lampac-nextgen/lampac.git
cd lampac

# Подготовить каталог на хосте (в репозитории его нет — создайте сами)
mkdir -p lampac-docker/config lampac-docker/plugins
cp config/example.init.conf lampac-docker/config/init.conf
# для docker-compose.dev.yaml также нужен отдельный файл:
# cp config/example.init.conf lampac-docker/config/development.init.conf
printf '%s' 'ваш_надёжный_пароль_root' > lampac-docker/config/passwd

# Запуск (из корня клона)
docker compose -f docker-compose.yaml up -d
```

Сервер в продовом compose слушает **9118** (`ports: 9118:9118`). В **`docker-compose.dev.yaml`** проброшен порт **29118** и монтируется `development.init.conf` — удобно, если основной порт уже занят.

**Как устроено в репозитории:** рабочая директория процесса в образе — `/lampac`. Файлы **`passwd`** и **`init.conf`** читаются из **корня этой директории** (не из подкаталога `config/` внутри контейнера). В compose они монтируются так:

| Путь на хосте (пример из репозитория) | Путь в контейнере | Назначение |
| --- | --- | --- |
| `./lampac-docker/config/passwd` | `/lampac/passwd` | Пароль root (WebLog, служебные функции) |
| `./lampac-docker/config/init.conf` | `/lampac/init.conf` | Основная конфигурация |
| `./lampac-docker/plugins/lampainit.js` | `/lampac/plugins/override/lampainit.js` | Переопределение клиентского плагина (опционально) |

В **`docker-compose.yaml`** по умолчанию отдельная bridge-сеть с фиксированным IP контейнера (`10.10.10.10`); `network_mode: host` **закомментирован**. При необходимости host-сети раскомментируйте `network_mode: host` и уберите/измените блок `ports`/`networks` в своём override.

Каталоги **`module/`** и **`mods/`** в образе уже заполнены поставкой; дополнительно монтировать их нужно только если переопределяете исходники модулей (см. закомментированные примеры в compose).

**Упрощённый пример сервиса (эквивалент идеи из репозитория):**

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

### Нативная установка (Linux)

Скрипт автоматически устанавливает .NET 10, создаёт системного пользователя и регистрирует systemd-сервис:

```bash
# Установка
curl -fsSL https://raw.githubusercontent.com/lampac-nextgen/lampac/main/install.sh | sudo bash

# Обновление
curl -fsSL https://raw.githubusercontent.com/lampac-nextgen/lampac/main/install.sh | sudo bash -s -- --update

# Предрелизная версия
curl -fsSL https://raw.githubusercontent.com/lampac-nextgen/lampac/main/install.sh | sudo bash -s -- --pre-release

# Удаление
curl -fsSL https://raw.githubusercontent.com/lampac-nextgen/lampac/main/install.sh | sudo bash -s -- --remove
```

Переменные окружения для кастомизации:

| Переменная | По умолчанию | Описание |
| --- | --- | --- |
| `LAMPAC_INSTALL_ROOT` | `/opt/lampac` | Директория установки |
| `LAMPAC_USER` | `lampac` | Системный пользователь |
| `LAMPAC_PORT` | `9118` | Порт прослушивания |

Управление сервисом:

```bash
systemctl start lampac
systemctl stop lampac
systemctl status lampac
journalctl -u lampac -f
```

### Ручная сборка

Требования: .NET SDK 10.0+

```bash
# Сборка
./build.sh

# Или напрямую
dotnet publish Core/Core.csproj -c Release -o publish

# Запуск
cd publish
dotnet Core.dll
```

Для кросс-компиляции укажите `RUNTIME_ID`:

```bash
RUNTIME_ID=linux-arm64 ./build.sh
```

---

## Конфигурация

Конфигурация хранится в файле `init.conf` (JSON) или `init.yaml`. Файл отслеживается каждую секунду и **перезагружается без перезапуска сервера** при изменении. Резервные копии сохраняются в `database/backup/init/`.

### Пример конфигурации в репозитории

В репозитории лежат примеры [`config/example.init.conf`](config/example.init.conf) и [`config/example.init.yaml`](config/example.init.yaml): скопируйте нужный вариант в рабочий `init.conf` (или используйте `init.yaml` рядом) и отредактируйте под себя. Типичные пути: при [нативной установке](#нативная-установка-linux) — `/opt/lampac/init.conf` (рядом с `Core.dll`, корень задаётся через `LAMPAC_INSTALL_ROOT`); при [Docker](#docker) — на хосте, например `./lampac-docker/config/init.conf`, с монтированием в **`/lampac/init.conf`** в контейнере (как в `docker-compose.yaml`).

Пример демонстрирует:

| Блок | Назначение |
| --- | --- |
| `listen` | Адрес и порт (`0.0.0.0:9118`), схема `http`, отображение версии |
| `BaseModule.SkipModules` | Отключение тяжёлых или ненужных модулей (каталог, DLNA, TorrServer, транскодинг и др.) для облегчённого запуска |
| `chromium` | Включение Playwright/Chromium и путь к бинарнику (актуально для Docker/Linux) |
| `openstat` | Включение openstat |
| `online` | Имя инстанса в интерфейсе Lampa, версия, приоритет кнопок |
| `sisi` | Настройки модуля 18+ (в т.ч. NextHUB, история) |

Это не полный перечень всех ключей — развернутые поля и комментарии см. в разделе [Основные параметры](#основные-параметры) и ниже по документу.

### Основные параметры

```jsonc
{
  // Порт и сетевые настройки
  "listen": {
    "port": 9118,
    "https": false,
    "compression": true,
    "ResponseCancelAfter": 15   // таймаут ответа, секунды
  },

  // Базовые настройки модуля
  "BaseModule": {
    "ValidateRequest": true,    // валидация входящих запросов
    "BlockedBots": true,        // блокировка ботов
    "SkipModules": []           // список отключённых модулей
  },

  // Кеш
  "cache": {
    "extend": 180               // продление TTL кеша, минуты
  },

  // Playwright (браузерная автоматизация)
  "chromium": {
    "enable": false,
    "count": 1,
    "restart": 3600             // перезапуск каждые N секунд
  },
  "firefox": {
    "enable": false,
    "count": 1
  },

  // Remote Client Hub (WebSocket-реле для клиентов за NAT)
  "rch": {
    "enable": false,
    "requiredConnected": 1
  },

  // Логирование (файлы в logs/, 14 дней хранения при включении)
  "serilog": false,

  // Управление памятью
  "GC": {
    "Concurrent": true,
    "ConserveMemory": 0,
    "HighMemoryPercent": 90,
    "RetainVM": false
  },

  // Ключ шифрования потоков
  "kit": {
    "aesgcmkeyName": ""
  }
}
```

### Аутентификация (accsdb)

```jsonc
{
  "accsdb": {
    "enable": true,
    "accounts": "user1:2026-12-31,user2:2027-06-01",
    // или подробный формат:
    "users": [
      { "login": "user1", "expired": "2026-12-31" },
      { "login": "user2", "expired": "2027-06-01" }
    ]
  }
}
```

### WAF и безопасность

```jsonc
{
  "WAF": {
    "enable": true,
    "countryAllow": ["RU", "UA", "BY"],   // геоблокировка (пустой — все)
    "whiteIps": ["192.168.1.0/24"],        // белый список IP/CIDR
    "bruteForceProtection": true,
    "limit_map": {                         // лимиты запросов по маршрутам
      "/lite/": 10,
      "/externalids": 10
    }
  }
}
```

### Провайдеры онлайн-кино

Каждый провайдер настраивается в своём разделе `init.conf`. Пример для Rezka:

```jsonc
{
  "Rezka": {
    "enable": true,
    "host": "https://rezka.ag",
    "priority": 1
  },
  "Filmix": {
    "enable": true,
    "host": "https://filmix.biz",
    "token": "ВАШ_ТОКЕН",
    "priority": 2
  },
  "KinoPub": {
    "enable": true,
    "token": "ВАШ_ТОКЕН"
  },
  "Kodik": {
    "enable": true,
    "token": "ВАШ_ТОКЕН"
  }
}
```

---

## Модули

**Примечание по статусу модулей по умолчанию:** согласно [`config/base.conf`](config/base.conf), в `SkipModules` по умолчанию указаны **Catalog**, **DLNA**, **Sync**, **SyncEvents**, **Storage**, **Tracks**, **Transcoding** и **WebLog**. Также по умолчанию отключены **WAF** и **accsdb** (аутентификация).

> [!WARNING]
> Модули **DLNA**, **Tracks**, **Transcoding** и **Catalog** не выполняют экранирование входящих запросов. **Не включайте их на публично доступном VPS** без ограничения доступа. Рекомендуется либо не активировать эти модули на публичном сервере, либо закрыть к ним доступ на уровне firewall/reverse proxy (разрешить только доверенные IP).

| Модуль | По умолч. | Описание |
| --- | :---: | --- |
| **Online** | ✅ вкл | VOD: ядро `Online/` + **OnlinePacks** (RUS, Anime, ENG, UKR, GEO). Плагин `/online.js`. WAF: 10 req/s на `/lite/`. |
| **SISI** | ✅ вкл | Контент 18+: **15** встроенных платформ (см. `SISI/README.md`). Плагин `/sisi.js`. История и закладки в SQLite. |
| **Catalog** | ⛔ откл | Браузер каталогов сайтов на основе YAML-описаний из папки `sites/`. Эндпоинт `/catalog/`. Без экранирования запросов — только в доверенной сети. |
| **CubProxy** | ✅ вкл | HTTP/HTTPS прокси с файловым кешем (`cache/cub/`), FileSystemWatcher для инвалидации кеша. |
| **DLNA** | ⛔ откл | DLNA/UPnP медиасервер. Обслуживает локальные файлы, автозагрузка трекеров торрентов. Форматы: aac, flac, mp4, mkv, ts, webm, avi и другие. Без экранирования запросов — только в доверенной сети. |
| **JacRed** | ✅ вкл | Агрегатор торрент-индексаторов (совместимый с Jackett API). Источники: Rutor, Megapeer, Kinozal, Rutracker, NNMClub, Toloka, Bitru и другие. |
| **LampaWeb** | ✅ вкл | Встроенный хостинг Lampa Web UI. Автообновление с GitHub каждые 90 минут. |
| **NextHUB** | ✅ вкл | Дополнительный браузер 18+ по YAML (`Modules/NextHUB/sites/`, **42** сайта в поставке). `GET /nexthub?plugin=…`. См. `Modules/NextHUB/README.md`. WAF: 5 req/s на `/nexthub`. |
| **Sync** | ⛔ откл | Синхронизация хранилища и закладок между устройствами. Эндпоинты `/storage/`, `/bookmark/`. SQLite-бэкенд. Для расширенной схемы см. **SyncEvents** и **Storage**. |
| **SyncEvents** | ⛔ откл | Трансляция событий синхронизации через NWS (`NwsEvents`). |
| **Storage** | ⛔ откл | Модуль хранилища в связке с Sync: NWS (`onlyreg`), пользовательские лимиты WAF из конфигурации модуля. |
| **TimeCode** | ✅ вкл | Сохранение и восстановление позиции воспроизведения (`resume from`). SQLite. |
| **TmdbProxy** | ✅ вкл | Локальный кеш TMDB API (`cache/tmdb/`). Снижает нагрузку на TMDB и ускоряет ответы. |
| **TorrServer** | ✅ вкл | Управление внешним процессом TorrServer. Проксирует `/ts/`. Генерирует случайный пароль за сессию. |
| **Tracks** | ⛔ откл | Управление субтитрами и дорожками (`database/tracks/`). Интеграция с FFprobe (`/ffprobe`). Без экранирования запросов — только в доверенной сети. |
| **Transcoding** | ⛔ откл | HLS/DASH транскодинг через FFmpeg. Макс. 5 параллельных заданий. Таймаут простоя 5 мин. Кеш: `cache/transcoding/`. Без экранирования запросов — только в доверенной сети. |
| **WebLog** | ⛔ откл | Отладочная страница `/weblog`: поток исходящих HTTP-ответов сервера и событий Playwright в браузере через WebSocket (`/nws`). Подписка на поток — только после `RegistryWebLog` с паролем root (`rootPasswd`). Содержит URL, заголовки и тела запросов/ответов — не включайте на публично доступном хосте. |

### Подключение пользовательских модулей

**Пользовательский модуль** — создайте поддиректорию в `mods/` с файлом `manifest.json` и исходными `.cs`-файлами. Модуль будет скомпилирован при запуске сервера через Roslyn (`CSharpEval`).

Пример структуры (`mods/MyModule/`):

```json
{
  "name": "MyModule",
  "description": "Описание модуля",
  "version": "1.0"
}
```

Модуль будет скомпилирован через Roslyn (`CSharpEval`) при старте сервера.

---

## Провайдеры контента

### VOD (онлайн-кино)

| Провайдер | Сервис | Примечания |
| --- | --- | --- |
| `Alloha` | Alloha CDN | |
| `CDNvideohub` | CDN VideoHub | |
| `Collaps` | Collaps | Включая DASH-вариант |
| `FanCDN` | FanCDN | |
| `Filmix` | Filmix.my | FilmixPartner, FilmixTV варианты |
| `FlixCDN` | FlixCDN | |
| `Geosaitebi` / `AsiaGe` / `Kinoflix` | Грузинские CDN | `Kinoflix` — `OnlineGEO` |
| `GetsTV` | GetsTV | |
| `HDVB` | HDVB | |
| `IptvOnline` | IPTV Online | |
| `KinoPub` | KinoPub | Требует токен |
| `Kinobase` | KinoBase | |
| `Kinogo` | Kinogo | |
| `Kinotochka` | Kinotochka | |
| `LeProduction` | Le Production | |
| `Mirage` | Mirage CDN | |
| `PiTor` | PidTor | Стриминг через торрент |
| `Rezka` / `RezkaPremium` | HDRezka | |
| `RutubeMovie` | Rutube | |
| `VeoVeo` | VeoVeo | Офлайн БД `data/veoveo.json` |
| `Vibix` | Vibix | |
| `VideoDB` / `Videoseed` | Video CDN (разные обходы) | Маршруты `/lite/videodb`, `/lite/videoseed` |
| `VkMovie` | VK Видео | |
| `VoKino` | VoKino | |
| `iRemux` | iRemux | |

### Аниме

| Провайдер | Сервис |
| --- | --- |
| `AniLiberty` | AniLiberty |
| `AniLibria` | AniLibria |
| `AniMedia` | AniMedia |
| `AnimeGo` | AnimeGo |
| `AnimeLib` | AnimeLib |
| `AnimeBesst` | AnimeBesst |
| `Animevost` | Animevost |
| `Dreamerscast` | Dreamerscast |
| `Kodik` | Kodik (универсальный, VOD + аниме) |
| `MoonAnime` | MoonAnime |

### Англоязычный контент

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

### Украинские CDN

| Провайдер | Сервис |
| --- | --- |
| `Ashdi` | Ashdi |
| `Eneyida` | Eneyida |
| `Kinoukr` | KinoUkr (офлайн БД `data/kinoukr.json`, ~130k записей) |

### SISI (контент 18+)

| Провайдер | Сервис | Эндпоинт |
| --- | --- | --- |
| `BongaCams` | BongaCams | `/bgs` |
| `Chaturbate` | Chaturbate | `/chu` |
| `Ebalovo` | Ebalovo | `/elo` |
| `Eporner` | Eporner | `/epr` |
| `HQporner` | HQporner | `/hqr` |
| `PornHub` | PornHub | `/phub` |
| `PornHubPremium` | PornHub Premium | `/phubprem` |
| `Porntrex` | Porntrex | `/ptx` |
| `Runetki` | Runetki | `/runetki` |
| `Spankbang` | Spankbang | `/sbg` |
| `Tizam` | Tizam | `/tizam` |
| `Xhamster` | XHamster | `/xmr` |
| `Xnxx` | XNXX | `/xnx` |
| `Xvideos` | XVideos | `/xds` (+ варианты `/xdsgay`, `/xdssml`) |
| `XvideosRED` | XVideos RED | `/xdsred` |

### NextHUB (18+)

Модуль **NextHUB** — витрина дополнительных сайтов 18+ по описаниям **YAML** (парсинг списков и просмотр через общий UI). В репозитории: каталог [`Modules/NextHUB/sites/`](Modules/NextHUB/sites/) (**42** файла `.yaml`). Имя файла без расширения — значение параметра `plugin` в URL.

- **Маршрут:** `GET /nexthub` — параметры: `plugin` (обязателен), опционально `search`, `sort`, `cat`, `model`, `pg` (см. `ListController`).
- **Конфиг** (`init.conf`, секция `NextHUB`): `sites_enabled` — если задана непустая строка, плагин разрешён только если его имя **содержится в строке** как подстрока (удобно перечислять имена через запятую, например `pornhub,beeg`); иначе доступны все YAML из `sites/`.
- **Переопределения:** `Modules/NextHUB/override/{plugin}.yaml` или `_.yaml` (слияние поверх базового YAML), см. `Root.goInit`.
- **WAF:** при загрузке модуля добавляется лимит **5** запросов в секунду на `^/nexthub` (по `plugin` в query), см. `Modules/NextHUB/ModInit.cs`.

Подробнее — [`Modules/NextHUB/README.md`](Modules/NextHUB/README.md).

---

## API-эндпоинты

### Core

| Метод | Путь | Описание |
| --- | --- | --- |
| `GET` | `/version` | Версия сервера |
| `GET` | `/api/headers` | Заголовки текущего запроса (JSON/text) |
| `GET` | `/api/geo[?ip=]` | GeoIP-локация IP-адреса |
| `GET` | `/api/myip` | IP-адрес клиента |
| `GET` | `/api/chromium/ping` | Пинг Playwright-браузера (ответ: `pong`) |
| `POST` | `/rch/result?id=` | RCH-реле: запись результата (макс. 10 МБ) |
| `POST` | `/rch/gzresult?id=` | RCH-реле: запись gzip-результата (макс. 10 МБ) |
| `WS` | `/ws` | NativeWebSocket для RCH push |
| `GET` | `/stats/gc` | Использование памяти: managed heap (зарезервировано / занято / фрагментация), WorkingSet и PrivateMemory процесса (JSON) |
| `GET` | `/stats/browser/context` | Статистика Playwright: число контекстов Chromium/Firefox, счётчики запросов контекста, последний ping Chromium (JSON) |
| `GET` | `/stats/request` | Нагрузка: счётчики запросов за минуту/час (base, маршруты, proxy, img, NWS, боты), активные HTTP и TCP-соединения, топ медленных путей (JSON) |
| `GET` | `/stats/tempdb` | Кеши и пулы: memory cache, HybridFileCache, прокси/RCH, счётчики пулов буферов и `RecyclableMemoryStream` (JSON) |
| `GET` | `/stats/threadpool` | Диагностика `ThreadPool`: очередь, worker/IO-потоки, uptime процесса, эвристика «голодания» пула (JSON) |

Эндпоинты `/stats/browser/context`, `/stats/request`, `/stats/tempdb` и `/stats/threadpool` отвечают **404**, если в конфиге не включено `openstat.enable: true`. Путь `/stats/gc` доступен всегда.

### Online (VOD)

| Метод | Путь | Описание |
| --- | --- | --- |
| `GET` | `/online.js` | Lampa VOD-плагин (JavaScript) |
| `GET` | `/online/js/{token}` | Плагин с авторизацией по токену |
| `GET` | `/lite/{provider}` | Список источников от провайдера (напр. `/lite/rezka`) |
| `GET` | `/externalids` | Маппинг внешних ID (TMDB ↔ KinoPoisk и т.д.) |
| `GET` | `/lifeevents` | SSE-поток событий здоровья провайдеров |

### SISI (18+)

| Метод | Путь | Описание |
| --- | --- | --- |
| `GET` | `/sisi.js` | Lampa SISI-плагин (JavaScript) |
| `GET` | `/sisi/js/{token}` | Плагин с авторизацией по токену |
| `GET` | `/{provider}` | Контент платформы (напр. `/phub`, `/xnx`) |
| `GET` | `/sisi/bookmark` | Управление закладками |
| `GET` | `/sisi/history` | История просмотров |

### Маршруты модулей

| Метод | Путь | Описание |
| --- | --- | --- |
| `GET` | `/catalog/{site}/…` | Просмотр каталога сайтов |
| `GET` | `/dlna/…` | DLNA медиасервер |
| `GET` | `/storage/…` | Синхронизация хранилища |
| `GET` | `/bookmark/…` | Синхронизация закладок |
| `GET` | `/timecode/…` | Позиции воспроизведения |
| `GET` | `/tmdb/…` | TMDB прокси/кеш |
| `GET` | `/transcoding/…` | HLS/DASH транскодинг |
| `GET` | `/ffprobe` | Метаданные дорожек (FFprobe) |
| `GET` | `/nexthub` | NextHUB: браузер 18+ по YAML (`plugin`, см. [NextHUB (18+)](#nexthub-18)) |
| `GET` | `/nexthub/vidosik` | NextHUB: просмотр элемента (`uri`, `related`) |
| `GET` | `/ts/…` | TorrServer |
| `GET` | `/weblog` | Отладка HTTP/Playwright в реальном времени |

---

## Зависимости

**Платформа:** .NET 10.0

| Пакет | Версия | Назначение |
| --- | --- | --- |
| `Microsoft.CodeAnalysis.CSharp` + `.Scripting` | 5.0.0 | Roslyn: компиляция модулей на лету |
| `Microsoft.Playwright` | 1.50.0 | Браузерная автоматизация (Chromium/Firefox) |
| `HtmlAgilityPack` | 1.12.4 | Парсинг HTML |
| `MaxMind.GeoIP2` | 5.4.1 | GeoIP (базы `GeoLite2-*.mmdb` включены в поставку) |
| `Newtonsoft.Json` | 13.0.4 | JSON-сериализация |
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.2 | ORM для SQLite (Sync, TimeCode, SISI, ExternalIds) |
| `Microsoft.IO.RecyclableMemoryStream` | 3.0.1 | Пул памяти для потоков |
| `NetVips` / `NetVips.Native` | 3.2.0 / 8.18.0 | Обработка изображений (libvips) |
| `YamlDotNet` | 16.3.0 | Парсинг YAML-конфигурации |
| `Serilog.AspNetCore` + `.Sinks.File` | 9.0.0 / 7.0.0 | Структурное логирование |
| `HtmlKit` | 1.2.0 | Парсинг HTML |
| `System.Management` | 10.0.2 | Информация об ОС и железе |

---

## Структура проекта

```text
lampac/
├── Core/                       # Точка входа, Middleware Pipeline
│   ├── Program.cs              # Запуск приложения, инициализация
│   ├── Startup.cs              # DI-контейнер, HTTP-клиенты, загрузка модулей
│   ├── Controllers/            # ApiController, RchApiEndpoints
│   ├── Middlewares/            # WAF, Accsdb, BaseMod, ProxyImg и другие
│   ├── Services/               # CronCacheWatcher, NativeWebSocket
│   ├── data/                   # GeoIP базы, статические JSON-базы
│   ├── plugins/                # JS-плагины (RCH, NWS)
│   └── wwwroot/                # Статические файлы
├── Shared/                     # Общая библиотека
│   ├── CoreInit.cs             # Загрузка и hot-reload конфигурации
│   ├── BaseController.cs       # Базовый контроллер
│   ├── Models/                 # Общие модели данных
│   └── Services/               # Shared-сервисы
├── Online/                     # Ядро VOD-модуля
│   ├── Controllers/            # Основные провайдеры (RU и общие)
│   ├── Config/                 # Конфигурационные модели
│   ├── ModInit.cs              # Инициализация модуля, загрузка БД
│   └── OnlineApi.cs            # `/online.js` эндпоинт
├── SISI/                       # Модуль контента 18+
│   ├── Controllers/            # Контроллеры платформ
│   ├── ModInit.cs              # Инициализация, SQLite, таймер очистки
│   └── SisiApi.cs              # `/sisi.js` эндпоинт
├── Modules/                    # Дополнительные модули и пакеты Online
│   ├── Catalog/
│   ├── CubProxy/
│   ├── DLNA/
│   ├── JacRed/
│   ├── LampaWeb/
│   ├── NextHUB/
│   ├── OnlinePacks/            # OnlineRUS, OnlineAnime, OnlineENG, OnlineUKR, OnlineGEO
│   ├── Proxy/                  # TmdbProxy и др.
│   ├── Sync/                   # Sync, TimeCode, SyncEvents, Storage
│   ├── TorrServer/
│   ├── Tracks/
│   ├── Transcoding/
│   └── WebLog/
├── TestModules/                # Тестовые/примерные модули (в publish → mods/)
│   └── Lamson/
├── config/
│   ├── base.conf               # Базовый шаблон конфигурации
│   ├── example.init.conf
│   └── example.init.yaml
├── docker-compose.yaml         # Compose (прод), корень репозитория
├── docker-compose.dev.yaml     # Compose для разработки (порт 29118)
├── Dockerfile                  # Мультистейдж, мультиплатформ образ
├── build.sh                    # Скрипт сборки
├── install.sh                  # Скрипт нативной установки (Debian/Ubuntu)
└── NextGen.sln                 # Solution-файл
```

---
