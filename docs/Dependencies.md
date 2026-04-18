# Зависимости и структура проекта (Dependencies & Structure)

## Платформа
Сервер **Lampac Next Generation** построен на платформе **.NET 10.0**.

## Основные библиотеки и пакеты (NuGet)

Проект использует следующие ключевые пакеты для обеспечения функциональности:

| Пакет | Версия | Назначение |
| --- | --- | --- |
| `Microsoft.CodeAnalysis.CSharp` + `.Scripting` | 5.0.0 | **Roslyn:** динамическая компиляция модулей (`.cs` файлов) на лету. |
| `Microsoft.Playwright` | 1.50.0 | Браузерная автоматизация (управление Chromium/Firefox) для обхода JS-защит и парсинга динамического контента. |
| `HtmlAgilityPack` | 1.12.4 | Эффективный парсинг HTML-документов. |
| `MaxMind.GeoIP2` | 5.4.1 | Определение геопозиции по IP-адресу (базы `GeoLite2-*.mmdb` включены в поставку в папке `Core/data/`). |
| `Newtonsoft.Json` | 13.0.4 | JSON-сериализация и десериализация. |
| `Microsoft.EntityFrameworkCore` (+ Sqlite, Design) | 10.0.2 | ORM для работы с локальными базами данных SQLite (модули Sync, TimeCode, SISI, ExternalIds). |
| `Microsoft.Extensions.DependencyModel` | 10.0.2 | Загрузка зависимостей при динамической компиляции модулей. |
| `Microsoft.IO.RecyclableMemoryStream` | 3.0.1 | Высокопроизводительный пул памяти для работы с потоками (снижает нагрузку на GC). |
| `NetVips` / `NetVips.Native` | 3.2.0 / 8.18.0 | Быстрая обработка и кеширование изображений (основано на libvips). |
| `YamlDotNet` | 16.3.0 | Парсинг YAML-конфигурации (используется для `init.yaml` и модуля NextHUB). |
| `Serilog.AspNetCore` + `.Sinks.File` | 9.0.0 / 7.0.0 | Структурное логирование приложения (пишет логи в папку `logs/`). |
| `HtmlKit` | 1.2.0 | Дополнительный парсер HTML. |
| `System.Management` | 10.0.2 | Получение информации об ОС и железе для эндпоинтов статистики. |

---

## Дерево каталогов репозитория

Для понимания того, где находится тот или иной код, ознакомьтесь со структурой репозитория. Весь код сгруппирован в Solution-файле `NextGen.slnx`.

```text
lampac/
├── Core/                       # Точка входа приложения, Pipeline и базовые эндпоинты
│   ├── Program.cs              # Запуск приложения
│   ├── Startup.cs              # DI-контейнер, HTTP-клиенты, загрузка модулей
│   ├── Controllers/            # Системные контроллеры (статистика, geo, версия)
│   ├── Middlewares/            # Логика обработки запросов (WAF, Auth, кеши)
│   ├── Services/               # Базовые сервисы
│   ├── data/                   # Базы данных (GeoIP)
│   ├── plugins/                # JS-плагины для клиента
│   └── wwwroot/                # Статические файлы, отдаваемые веб-сервером
├── Shared/                     # Общая библиотека (используется ядром и всеми модулями)
│   ├── CoreInit.cs             # Механизм загрузки и перезагрузки конфига
│   ├── BaseController.cs       # Базовые классы для парсеров
│   ├── Models/                 # Модели данных
│   └── Services/               # Утилиты (Http, Кеш, Playwright, CSharpEval)
├── Online/                     # Ядро VOD (Видео по запросу)
│   ├── Controllers/            # Встроенные парсеры (например, PiTor)
│   ├── OnlineApi.cs            # Генерация плагина online.js и сбор агрегации
│   └── manifest.json
├── SISI/                       # Общий модуль контента 18+
│   ├── SisiApi.cs              # Плагин sisi.js
│   ├── SQL/                    # База данных SQLite для истории и закладок
│   └── manifest.json
├── Modules/                    # Папка со всеми независимыми расширениями
│   ├── AdminPanel/             # Админка
│   ├── Adult/                  # Хардкодные парсеры 18+ (PornHub, Xnxx и т.д.)
│   ├── Community/              # Telegram авторизация
│   ├── DLNA/                   # Медиасервер
│   ├── ExternalBind/           # Модуль привязок аккаунтов (например Filmix)
│   ├── JacRed/                 # Агрегатор торрентов
│   ├── NextHUB/                # YAML парсер 18+ сайтов
│   ├── OnlineAnime/            # Провайдеры Аниме (AniLibria, Kodik...)
│   ├── OnlineENG/              # Западные провайдеры (VidSrc, AutoEmbed...)
│   ├── OnlineGEO/              # Грузинские провайдеры
│   ├── OnlinePaid/             # Провайдеры по подписке (Rezka, Filmix...)
│   ├── OnlineRUS/              # Русскоязычные провайдеры (HDVB, Collaps...)
│   ├── OnlineUKR/              # Украинские провайдеры (UaKino, Eneyida...)
│   ├── Proxy/                  # Прокси для TMDB и Cub
│   ├── Sync/                   # Синхронизация между устройствами
│   ├── TorrServer/             # Интеграция торрент-клиента
│   ├── Tracks/                 # Субтитры и аудиодорожки
│   ├── Transcoding/            # HLS Транскодинг через FFmpeg
│   └── WebLog/                 # Отладка
├── TestModules/                # Тестовые модули-примеры
├── config/                     # Базовые конфиги (base.conf, example.init.conf)
├── docker-compose.yaml         # Конфигурация для запуска в Docker (Prod)
├── docker-compose.dev.yaml     # Конфигурация для запуска в Docker (Dev)
├── Dockerfile                  # Инструкция сборки Docker-образа
├── build.sh                    # Bash-скрипт сборки проекта (dotnet publish)
├── install.sh                  # Bash-скрипт установки Lampac как системного сервиса (systemd)
└── NextGen.slnx                # Основной файл решения Visual Studio / JetBrains Rider
```

*При публикации проекта (`dotnet publish`), исходники модулей из `Modules/` автоматически копируются в директорию `module/` и компилируются на лету движком `Roslyn` при старте приложения.*
