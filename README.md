# Lampac Next Generation

[![Build](https://github.com/lampac-nextgen/lampac/actions/workflows/build.yml/badge.svg)](https://github.com/lampac-nextgen/lampac/actions/workflows/build.yml)
[![Release](https://github.com/lampac-nextgen/lampac/actions/workflows/release.yml/badge.svg)](https://github.com/lampac-nextgen/lampac/actions/workflows/release.yml)
[![GitHub release (latest SemVer)](https://img.shields.io/github/v/release/lampac-nextgen/lampac?label=version)](https://github.com/lampac-nextgen/lampac/releases)
[![GitHub tag (latest SemVer pre-release)](https://img.shields.io/github/v/tag/lampac-nextgen/lampac?include_prereleases&label=pre-release)](https://github.com/lampac-nextgen/lampac/tags)

> **Самохостируемый backend-сервер для расширения [Lampa](https://github.com/yumata/lampa)** — собирает ссылки на публично доступный контент с десятков российских, украинских, СНГ-сервисов, аниме-источников и западных платформ и передаёт их Lampa в виде плагинов. Построен на ASP.NET Core (.NET 10).

---

## 📚 Документация (WIKI)

Мы подготовили подробную документацию, которая поможет вам установить, настроить и понять архитектуру проекта. Пожалуйста, ознакомьтесь с разделами WIKI:

- 🏠 **[Главная (Home)](wiki/Home.md)** — Обзор возможностей проекта.
- 🚀 **[Установка и запуск](wiki/Installation.md)** — Инструкции по запуску через Docker, нативной установке и сборке.
- ⚙️ **[Конфигурация](wiki/Configuration.md)** — Полное описание настроек `init.conf`, WAF, Playwright и провайдеров.
- 🏗️ **[Архитектура](wiki/Architecture.md)** — Описание слоев (Core, Shared) и процесса динамической компиляции Roslyn.
- 📦 **[Модули](wiki/Modules.md)** — Управление встроенными модулями (DLNA, TorrServer, SISI) и создание собственных расширений.
- 🎬 **[Провайдеры контента](wiki/Providers.md)** — Полный список поддерживаемых источников видео и аниме.
- 🔌 **[API Эндпоинты](wiki/API.md)** — Документация по HTTP-маршрутам сервера.
- 📚 **[Зависимости и структура](wiki/Dependencies.md)** — Используемые NuGet пакеты и подробное дерево файлов репозитория.

## 💬 Сообщество

[Telegram Чат Lampac NextGen](https://t.me/LampacTalks/13998)
