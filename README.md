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
     - [Основной запуск (docker-compose.yaml)](#основной-запуск-docker-composeyaml)
     - [Разработка и тесты (dev compose)](#разработка-и-тесты-dev-compose)
     - [Модули, manifest.json и Docker](#модули-manifestjson-и-docker)
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
13. [Дополнительная документация в репозитории](#дополнительная-документация-в-репозитории) (в т.ч. Core, Shared, Online)

---

## Обзор проекта

[Lampa](https://github.com/yumata/lampa) — бесплатное приложение для просмотра информации о фильмах, новинках и популярных релизах. Оно использует публичные ссылки и не распространяет контент через собственные серверы — вся информация отображается исключительно в познавательных целях.

**Lampac Next Generation** — самохостируемый backend-сервер для расширения [Lampa](https://github.com/yumata/lampa) через плагины. Он собирает ссылки на публично доступный контент с десятков российских, украинских, СНГ-сервисов, аниме-источников и западных платформ, отдаёт их Lampa в виде структурированных JSON API-ответов и дополнительно предоставляет: проксирование метаданных TMDB, встроенный TorrServer, DLNA/UPnP-медиасервер, транскодинг через FFmpeg, управление субтитрами и синхронизацию закладок между устройствами. Запускается на порту **9118**.

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
│  ┌─────────┐ ┌─────────┐ ┌──────────┐ ┌───────────────────┐     │
│  │ Online  │ │  SISI   │ │ Catalog  │ │    LampaWeb       │     │
│  │(VOD API)│ │ + Adult │ │(каталог) │ │(Lampa UI)         │     │
│  └─────────┘ └─────────┘ └──────────┘ └───────────────────┘     │
│  ┌─────────┐ ┌─────────┐ ┌──────────┐ ┌───────────────────┐     │
│  │TorrServr│ │  DLNA   │ │  JacRed  │ │   Transcoding     │     │
│  └─────────┘ └─────────┘ └──────────┘ └───────────────────┘     │
│  ┌─────────┐ ┌─────────┐ ┌──────────┐ ┌───────────────────┐     │
│  │TmdbProxy│ │  Sync   │ │ TimeCode │ │     Tracks        │     │
│  │CubProxy │ │ WebLog  │ │ NextHUB  │ │  AdminPanel, Kit  │     │
│  └─────────┘ └─────────┘ └──────────┘ └───────────────────┘     │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  Modules/OnlineRUS · OnlinePaid · OnlineAnime · OnlineENG │  │
│  │  OnlineUKR · OnlineGEO  — по одному проекту на провайдера │  │
│  │  Modules/Adult/* — платформы 18+ (маршруты /phub, /xnx…)  │  │
│  │  Modules/Community/* — TelegramAuth, TelegramAuthBot      │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

**Слои:**

| Слой | Описание |
| --- | --- |
| **Core** | Точка входа, Middleware Pipeline, основной `ApiController`, часть статики в `Core/wwwroot/`. Подробнее: [Core/README.md](Core/README.md) |
| **Shared** | Общие модели, базовые контроллеры, конфигурация, HTTP-пулы, Roslyn. Подробнее: [Shared/README.md](Shared/README.md) |
| **Online** | Ядро VOD (`Online/`): общие контроллеры и провайдеры; дополнительные источники — **отдельные проекты** в `Modules/OnlineRUS`, `OnlinePaid`, `OnlineAnime`, `OnlineENG`, `OnlineUKR`, `OnlineGEO` |
| **SISI** | Общий модуль 18+ (`SISI/`): плагин `/sisi.js`, SQLite (история, закладки), лимиты WAF. Платформы — в **`Modules/Adult/<Имя>/`**. Подробнее: [SISI/README.md](SISI/README.md) |
| **Modules/** | Функциональные модули (каталог, прокси в `Modules/Proxy/`, TorrServer, NextHUB, Sync и др.), опционально **AdminPanel**, **Kit**, **ExternalBind**, **Community** |

Сборочные модули (Online, SISI, Catalog, прокси, синхронизация и др.) подключаются как **скомпилированные сборки** из каталога `runtimes/references/` при старте процесса (`Core.dll`). Параллельно в образе/публикации присутствуют каталоги **`module/`** и **`mods/`**: туда копируются исходники из `Modules/` (с сохранением вложенных путей, например `module/OnlineRUS/Rezka/…`), `Online/`, `SISI/` и `TestModules/` (см. `Core/Core.csproj`) — их компилирует **Roslyn** (`CSharpEval`) при запуске, что даёт горячую подгрузку и пользовательские оверлеи. Дополнительно пользователь может положить свои модули в **`mods/`** на машине с уже развёрнутым сервером (рядом с `Core.dll`), не пересобирая solution целиком.

---

## Возможности

- **Десятки VOD- и аниме-источников** — ядро `Online/` (в т.ч. PiTor и др.) + отдельные провайдерные проекты в **`Modules/OnlineRUS`**, **`OnlinePaid`**, **`OnlineAnime`**, **`OnlineENG`**, **`OnlineUKR`**, **`OnlineGEO`** (HDVB, Collaps, Kodik, VidSrc, Kinoflix, Rezka, Filmix и другие)
- **Аниме-провайдеры** в `Modules/OnlineAnime/` — AniLibria, AniLiberty, AnimeGo, AniMedia, Kodik, AnimeON, Mikai и др.
- **Англоязычный контент** — VidSrc, AutoEmbed, SmashyStream, TwoEmbed, VidLink и другие
- **SISI (18+)** — общий модуль `SISI/` + платформы в **`Modules/Adult/`** (PornHub, XVideos, XHamster, Chaturbate, BongaCams и другие)
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

**Основной сценарий** для запуска Lampac у себя — файл **`docker-compose.yaml`** (порт **9118**, сеть и проброс портов как у «нормального» сервиса). Из корня клона после подготовки файлов на хосте достаточно:

```bash
docker compose up -d
```

(Compose по умолчанию подхватывает `docker-compose.yaml`; явный вариант тот же: `docker compose -f docker-compose.yaml up -d`.)

Файл **`docker-compose.dev.yaml`** — **не** основной путь. Он нужен **для разработки** репозитория: правки кода/конфига, прогон тестов, отдельная инстанция на порту **29118**, чтобы не мешать контейнеру с **9118**. Для обычного использования ориентируйтесь на **`docker-compose.yaml`**. В начале каждого YAML есть краткие комментарии по обязательным файлам на хосте.

#### Основной запуск (docker-compose.yaml)

Файл: **`docker-compose.yaml`**.

| | |
| --- | --- |
| **Порт** | **9118** (`9118:9118`) |
| **Тома** | Блок **`volumes` по умолчанию закомментирован** — контейнер стартует с **`init.conf` и `passwd` из образа**. Чтобы вести конфиг на хосте, создайте файлы и **раскомментируйте** тома (см. комментарии в файле). Пока тома закомментированы, каталог **`./lampac-docker/`** на хосте **не используется**. |

**Пошагово**

```bash
git clone https://github.com/lampac-nextgen/lampac.git
cd lampac

mkdir -p lampac-docker/config lampac-docker/plugins
cp config/example.init.conf lampac-docker/config/init.conf
# При необходимости отредактируйте init.conf.

printf '%s' 'ваш_надёжный_пароль_root' > lampac-docker/config/passwd

# Раскомментируйте блок volumes в docker-compose.yaml (passwd, init.conf и при необходимости остальное).

docker compose up -d
```

#### Разработка и тесты (dev compose)

Файл: **`docker-compose.dev.yaml`**.

| | |
| --- | --- |
| **Порт** | **29118** |
| **Тома** | Включены: `passwd`, `development.init.conf` → `/lampac/init.conf`, `lampainit.js`. |
| **Когда использовать** | Локальная разработка, эксперименты, отдельный конфиг; не замена основному **`docker-compose.yaml`** для типичного запуска. |

В **`development.init.conf`** в **`listen.port`** укажите **`29118`**, иначе проброс портов не совпадёт с портом приложения (в [`config/example.init.conf`](config/example.init.conf) по умолчанию **9118**). **`lampac-docker/plugins/lampainit.js`** должен существовать до `up` (`touch` или копия [`Modules/LampaWeb/plugins/lampainit.js`](Modules/LampaWeb/plugins/lampainit.js)).

```bash
mkdir -p lampac-docker/config lampac-docker/plugins
cp config/example.init.conf lampac-docker/config/development.init.conf
# В development.init.conf: "listen"."port": 29118

printf '%s' 'ваш_надёжный_пароль_root' > lampac-docker/config/passwd
cp Modules/LampaWeb/plugins/lampainit.js lampac-docker/plugins/lampainit.js

docker compose -f docker-compose.dev.yaml up -d
# http://localhost:29118
```

В обоих compose-файлах задано **`container_name: lampac`**. Одновременно поднять оба без правки **нельзя** — выполните `docker compose down` для одного варианта или переименуйте `container_name` в локальной копии.

**Рабочая директория в контейнере** — `/lampac`. Файлы **`passwd`** и **`init.conf`** приложение читает из **корня `/lampac`**, а не из подкаталога `config/` внутри контейнера. На хосте удобно хранить их в `./lampac-docker/config/`, но пути монтирования должны заканчиваться на **`/lampac/passwd`** и **`/lampac/init.conf`**.

| Путь на хосте (пример) | Путь в контейнере | Назначение |
| --- | --- | --- |
| `./lampac-docker/config/passwd` | `/lampac/passwd` | Пароль root (WebLog, служебные функции) |
| `./lampac-docker/config/init.conf` | `/lampac/init.conf` | Основная конфигурация (прод-подобный compose) |
| `./lampac-docker/config/development.init.conf` | `/lampac/init.conf` | То же имя внутри контейнера — отдельный файл только на хосте (dev compose) |
| `./lampac-docker/plugins/lampainit.js` | `/lampac/plugins/override/lampainit.js` | Переопределение клиентского плагина (опционально; в dev-compose монтирование включено) |

В **`docker-compose.yaml`** по умолчанию используется отдельная bridge-сеть с фиксированным IP контейнера (`10.10.10.10`); **`network_mode: host`** закомментирован. Если нужна сеть хоста — раскомментируйте `network_mode: host` и согласуйте с этим блоки `ports` / `networks` (в комментарии в файле кратко указано то же).

Каталоги **`module/`** и **`mods/`** в образе уже заполнены поставкой. Дополнительные тома нужны, если вы подключаете **свой** каталог модуля в `mods/` (пример в комментариях compose) или хотите **переопределить** файлы модуля, в том числе **`manifest.json`** (см. следующий подраздел).

#### Модули, manifest.json и Docker

Состав загружаемых модулей задаётся двумя основными механизмами (подробнее таблица в разделе [Модули](#модули)):

1. **`init.conf`** — **`BaseModule.SkipModules`**: список **имён** модулей, которые **не** загружаются, даже если их код есть в образе.
2. **`manifest.json`** в каталоге модуля в **`/lampac/module/...`** или **`/lampac/mods/...`**: ключ **`"enable": true|false`** включает или отключает модуль при старте. У части модулей в репозитории по умолчанию **`"enable": false`** (например, [AdminPanel](Modules/AdminPanel/manifest.json), [ExternalBind](Modules/ExternalBind/manifest.json)) — в образе они поставляются выключенными, пока не измените манифест или не смонтируете каталог с **`"enable": true`**.

Чтобы **включить** модуль, выключенный в манифесте образа, без пересборки образа: скопируйте каталог модуля с хоста из репозитория (или из образа), поправьте в нём `manifest.json`, смонтируйте **в тот же относительный путь**, что в образе — обычно **`/lampac/module/<Имя>/`** для штатных модулей или **`/lampac/mods/<Имя>/`** для своих/дополнительных (пример тома для `mods/` см. в **`docker-compose.yaml`**). Пользовательские расширения кладутся в **`mods/<Имя>/`** с `manifest.json` и исходниками — см. [Подключение пользовательских модулей](#подключение-пользовательских-модулей).

**Упрощённый пример сервиса** (когда тома уже согласованы с хостом):

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
# Сборка (обёртка над dotnet publish)
./build.sh

# Или напрямую
dotnet publish Core/Core.csproj -c Release -o publish

# Сборка всех проектов из solution (проверка компиляции модулей)
dotnet build NextGen.slnx

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

В репозитории лежат примеры [`config/example.init.conf`](config/example.init.conf) и [`config/example.init.yaml`](config/example.init.yaml): скопируйте нужный вариант в рабочий `init.conf` (или используйте `init.yaml` рядом) и отредактируйте под себя. Типичные пути: при [нативной установке](#нативная-установка-linux) — `/opt/lampac/init.conf` (рядом с `Core.dll`, корень задаётся через `LAMPAC_INSTALL_ROOT`); при [Docker](#docker) — на хосте, например `./lampac-docker/config/init.conf`, с монтированием в **`/lampac/init.conf`** в контейнере после **раскомментирования** томов в `docker-compose.yaml` (см. [основной запуск в Docker](#основной-запуск-docker-composeyaml)).

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
    "SkipModules": [],          // список отключённых модулей
    "LoadModules": [".*"]       // whitelist: имя модуля, группа (OnlineUKR), маска (LME.*)
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

**Примечание по статусу модулей по умолчанию:** согласно [`config/base.conf`](config/base.conf), в `SkipModules` по умолчанию указаны **Catalog**, **DLNA**, **Sync**, **SyncEvents**, **Storage**, **Tracks**, **Transcoding**, **WebLog**, **TelegramAuth** и **TelegramAuthBot**. Также по умолчанию отключены **WAF** и **accsdb** (аутентификация).

> [!WARNING]
> Модули **DLNA**, **Tracks**, **Transcoding** и **Catalog** не выполняют экранирование входящих запросов. **Не включайте их на публично доступном VPS** без ограничения доступа. Рекомендуется либо не активировать эти модули на публичном сервере, либо закрыть к ним доступ на уровне firewall/reverse proxy (разрешить только доверенные IP).

| Модуль | По умолч. | Описание |
| --- | :---: | --- |
| **Online** | ✅ вкл | VOD: ядро [`Online/`](Online/README.md) (`/online.js`, агрегаты `/lite/…`) + провайдеры в **`Modules/OnlineRUS`**, **`OnlinePaid`**, **`OnlineAnime`**, **`OnlineENG`**, **`OnlineUKR`**, **`OnlineGEO`**. WAF: 10 req/s на `/lite/`. |
| **SISI** | ✅ вкл | Модуль 18+: общая логика в `SISI/` (плагин `/sisi.js`, SQLite, закладки/история). Платформы — **`Modules/Adult/*`**. См. [SISI/README.md](SISI/README.md). |
| **Catalog** | ⛔ откл | Браузер каталогов сайтов на основе YAML-описаний из папки `sites/`. Эндпоинт `/catalog/`. Без экранирования запросов — только в доверенной сети. |
| **CubProxy** | ✅ вкл | HTTP/HTTPS прокси с файловым кешем (`cache/cub/`), FileSystemWatcher для инвалидации кеша. |
| **DLNA** | ⛔ откл | DLNA/UPnP медиасервер. Обслуживает локальные файлы, автозагрузка трекеров торрентов. Форматы: aac, flac, mp4, mkv, ts, webm, avi и другие. Без экранирования запросов — только в доверенной сети. |
| **JacRed** | ✅ вкл | Агрегатор торрент-индексаторов (совместимый с Jackett API). Источники: Rutor, Megapeer, Kinozal, Rutracker, NNMClub, Toloka, Bitru и другие. |
| **LampaWeb** | ✅ вкл | Встроенный хостинг Lampa Web UI. Автообновление с GitHub каждые 90 минут. |
| **NextHUB** | ✅ вкл | Дополнительный браузер 18+ по YAML (`Modules/NextHUB/sites/`). `GET /nexthub?plugin=…`. См. [README](Modules/NextHUB/README.md). WAF: 5 req/s на `/nexthub`. |
| **AdminPanel** | ⛔ откл (manifest) | Веб-админка и JSON API (типичные пути `/adminpanel/`, `/adminpanel/api/`). В [`Modules/AdminPanel/manifest.json`](Modules/AdminPanel/manifest.json) по умолчанию `"enable": false`. |
| **Kit** | ✅ вкл | Сервисный модуль (шифрование потоков и связанная логика; конфиг `kit` в `init.conf`). |
| **ExternalBind** | ⛔ откл (manifest) | Дополнительные привязки/маршруты; по умолчанию выключен в [`Modules/ExternalBind/manifest.json`](Modules/ExternalBind/manifest.json). См. [README](Modules/ExternalBind/README.md). |
| **TelegramAuth** | ⛔ откл (base) | Авторизация через Telegram — [README](Modules/Community/TelegramAuth/README.md), обзор [Community](Modules/Community/README.md). |
| **TelegramAuthBot** | ⛔ откл (base) | Бот для Telegram-авторизации — [README](Modules/Community/TelegramAuthBot/README.md). |
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

Пример структуры (`mods/MyModule/`). Минимально в **`manifest.json`** должны быть осмысленные метаданные и **`"enable": true`**, иначе модуль может не загрузиться:

```json
{
  "name": "MyModule",
  "description": "Описание модуля",
  "version": "1.0",
  "enable": true
}
```

У модулей из поставки нередко есть поле **`"dynamic": true`** (загрузка через Roslyn); для своего модуля ориентируйтесь на ближайший по смыслу пример из `Modules/*/manifest.json`. Модуль будет скомпилирован через Roslyn (`CSharpEval`) при старте сервера.

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
| `BamBoo` | BamBoo |
| `Eneyida` | Eneyida |
| `HdvbUA` | HDVB (UA) |
| `Kinoukr` | KinoUkr (офлайн БД `data/kinoukr.json`, ~130k записей) |
| `Tortuga` | Tortuga |
| `UaKino` | UaKino |

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

Модуль **NextHUB** — витрина дополнительных сайтов 18+ по описаниям **YAML** (парсинг списков и просмотр через общий UI). В репозитории: каталог [`Modules/NextHUB/sites/`](Modules/NextHUB/sites/) (десятки описаний `.yaml`; имя файла без расширения — значение параметра `plugin` в URL). Пример базового шаблона: `Modules/NextHUB/examples/base.yaml`.

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
| `Microsoft.EntityFrameworkCore` (+ Sqlite, Design) | 10.0.2 | ORM для SQLite (Sync, TimeCode, SISI, ExternalIds) |
| `Microsoft.Extensions.DependencyModel` | 10.0.2 | Загрузка зависимостей при динамической компиляции модулей |
| `Microsoft.IO.RecyclableMemoryStream` | 3.0.1 | Пул памяти для потоков |
| `NetVips` / `NetVips.Native` | 3.2.0 / 8.18.0 | Обработка изображений (libvips) |
| `YamlDotNet` | 16.3.0 | Парсинг YAML-конфигурации |
| `Serilog.AspNetCore` + `.Sinks.File` | 9.0.0 / 7.0.0 | Структурное логирование |
| `HtmlKit` | 1.2.0 | Парсинг HTML |
| `System.Management` | 10.0.2 | Информация об ОС и железе |

---

## Структура проекта

Решение: [`NextGen.slnx`](NextGen.slnx) — в нём сгруппированы **Core**, **Shared**, **Online**, **SISI**, **TestModules** и дерево **`Modules/`** (включая вложенные папки по доменам).

```text
lampac/
├── Core/                       # Точка входа, Middleware Pipeline (см. Core/README.md)
│   ├── Program.cs              # Запуск приложения, инициализация
│   ├── Startup.cs              # DI-контейнер, HTTP-клиенты, загрузка модулей
│   ├── Controllers/            # ApiController, RchApiEndpoints
│   ├── Middlewares/            # WAF, Accsdb, BaseMod, ProxyImg и другие
│   ├── Services/               # CronCacheWatcher, NativeWebSocket
│   ├── data/                   # GeoIP базы, статические JSON-базы
│   ├── plugins/                # JS-плагины (RCH, NWS)
│   └── wwwroot/                # Статика хоста (SISI UI, stats, buy и др.)
├── Shared/                     # Общая библиотека (см. Shared/README.md)
│   ├── CoreInit.cs             # Загрузка и hot-reload конфигурации
│   ├── BaseController.cs       # Базовый контроллер
│   ├── Models/                 # Общие модели данных
│   └── Services/               # Shared-сервисы
├── Online/                     # Ядро VOD (см. Online/README.md)
│   ├── Controllers/
│   ├── Config/
│   ├── ModInit.cs
│   └── OnlineApi.cs
├── SISI/                       # Общий модуль 18+ (/sisi.js, SQLite, закладки)
│   ├── ModInit.cs
│   ├── SisiApi.cs
│   └── …                       # см. SISI/README.md; платформы — в Modules/Adult/
├── Modules/
│   ├── AdminPanel/             # Админка (по умолчанию выключена в manifest)
│   ├── Adult/                  # Платформы 18+ (по одному проекту на источник)
│   ├── Catalog/
│   ├── Community/              # TelegramAuth, TelegramAuthBot
│   ├── DLNA/
│   ├── ExternalBind/
│   ├── JacRed/
│   ├── Kit/
│   ├── LampaWeb/
│   ├── NextHUB/                # sites/*.yaml, examples/
│   ├── OnlineAnime/            # AniLibria, Kodik, … (подпроекты)
│   ├── OnlineENG/
│   ├── OnlineGEO/
│   ├── OnlinePaid/             # Rezka, Filmix, KinoPub, …
│   ├── OnlineRUS/
│   ├── OnlineUKR/
│   ├── Proxy/                  # CubProxy, TmdbProxy
│   ├── Sync/                   # Sync, SyncEvents, Storage, TimeCode
│   ├── TorrServer/
│   ├── Tracks/
│   ├── Transcoding/
│   └── WebLog/
├── TestModules/                # Примеры (в publish копируются в mods/)
│   └── Lamson/
├── config/
│   ├── base.conf
│   ├── example.init.conf
│   └── example.init.yaml
├── docker-compose.yaml
├── docker-compose.dev.yaml
├── Dockerfile
├── build.sh                    # dotnet publish Core/Core.csproj → publish/
├── install.sh
└── NextGen.slnx
```

После `dotnet publish` исходники модулей оказываются под **`module/`** (см. `Core/Core.csproj`: `Modules/**` → `module/…`, `Online/` → `module/Online/…`, `SISI/` → `module/SISI/…`), а **TestModules** — под **`mods/`**.

---

## Дополнительная документация в репозитории

| Документ | О чём |
| --- | --- |
| [Core/README.md](Core/README.md) | Хост **`Core`**: `Program`/`Startup`, middleware, загрузка **`module/`** и **`mods/`**, ссылки на код |
| [Shared/README.md](Shared/README.md) | Библиотека **`Shared`**: **`CoreInit`**, базовые контроллеры, **`CSharpEval`**, кеш, HTTP, Playwright |
| [Online/README.md](Online/README.md) | Ядро VOD **`Online/`**: **`online.js`**, **`/lite/`**, PiTor, **`Externalids`**, связь с **`Modules/Online*`** |
| [SISI/README.md](SISI/README.md) | Контент 18+: ядро **`SISI/`**, платформы **`Modules/Adult/*`**, таблица маршрутов |
| [Modules/NextHUB/README.md](Modules/NextHUB/README.md) | YAML-сайты, `/nexthub`, конфиг, WAF |
| [Modules/Community/README.md](Modules/Community/README.md) | Telegram-авторизация, клиент Lampa (`deny.js`, `telegram_auth_gate.js`), ссылки на API |
| [Modules/Community/TelegramAuth/README.md](Modules/Community/TelegramAuth/README.md) | HTTP API `/tg/auth/…`, accsdb, хранилище |
| [Modules/Community/TelegramAuthBot/README.md](Modules/Community/TelegramAuthBot/README.md) | Long polling-бот, команды, конфиг |
| [Modules/ExternalBind/README.md](Modules/ExternalBind/README.md) | Пути привязки Lite/Online и флаг локального IP |

---
