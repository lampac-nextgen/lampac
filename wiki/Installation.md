# Установка и запуск (Installation)

В этом разделе описаны различные способы запуска сервера **Lampac Next Generation**. Поддерживаются системы на архитектурах `amd64` и `arm64`.

## 1. Запуск через Docker (Рекомендуемый способ)

Использование Docker — самый быстрый и безопасный способ запуска Lampac, так как все зависимости (включая .NET и Playwright браузеры) уже упакованы в образ.

### 1.1 Основной запуск (Production)

Для основного использования сервера применяйте `docker-compose.yaml`, который запускает Lampac на порту **9118**.

**Пошаговая инструкция:**

1. Клонируйте репозиторий:
   ```bash
   git clone https://github.com/lampac-nextgen/lampac.git
   cd lampac
   ```

2. Подготовьте конфигурационные файлы на хосте (чтобы они не стирались при обновлении контейнера):
   ```bash
   mkdir -p lampac-docker/config lampac-docker/plugins
   cp config/example.init.conf lampac-docker/config/init.conf
   ```
   *При необходимости, сразу отредактируйте `init.conf` под свои нужды.*

3. Создайте файл с паролем пользователя `root` (потребуется для доступа к админке и WebLog):
   ```bash
   printf '%s' 'ваш_надёжный_пароль_root' > lampac-docker/config/passwd
   ```

4. Отредактируйте файл `docker-compose.yaml`:
   - Откройте `docker-compose.yaml`.
   - **Раскомментируйте блок `volumes`**, чтобы смонтировать ваши локальные конфиги в контейнер:
     ```yaml
     volumes:
       - ./lampac-docker/config/passwd:/lampac/passwd
       - ./lampac-docker/config/init.conf:/lampac/init.conf
     ```

5. Запустите контейнер в фоновом режиме:
   ```bash
   docker compose up -d
   ```

Сервер будет доступен по адресу: `http://<ip-вашего-сервера>:9118`.

### 1.2 Запуск для разработки (Dev-версия)

Если вы хотите вносить изменения в код или тестировать конфиги, не затрагивая основной процесс, используйте `docker-compose.dev.yaml`. Он запускает отдельный инстанс на порту **29118**.

1. Подготовьте файлы:
   ```bash
   mkdir -p lampac-docker/config lampac-docker/plugins
   cp config/example.init.conf lampac-docker/config/development.init.conf
   ```
   **Важно:** В файле `development.init.conf` обязательно измените параметр `"listen"."port"` на `29118`.

2. Задайте пароль и плагин инициализации:
   ```bash
   printf '%s' 'ваш_надёжный_пароль_root' > lampac-docker/config/passwd
   cp Modules/LampaWeb/plugins/lampainit.js lampac-docker/plugins/lampainit.js
   ```

3. Запустите dev-композ:
   ```bash
   docker compose -f docker-compose.dev.yaml up -d
   ```

> **Внимание:** В обоих `docker-compose` файлах задано одинаковое имя контейнера (`container_name: lampac`). Одновременный их запуск без изменения имени контейнера приведет к конфликту.

---

## 2. Нативная установка (Linux)

Для прямой установки на хост (без Docker) доступен установочный скрипт. Он автоматически установит .NET 10, создаст системного пользователя `lampac` и зарегистрирует systemd-сервис для автозапуска.

### Установка

Выполните команду от имени root или с помощью sudo:
```bash
curl -fsSL https://raw.githubusercontent.com/lampac-nextgen/lampac/main/install.sh | sudo bash
```

### Обновление до последней версии

```bash
curl -fsSL https://raw.githubusercontent.com/lampac-nextgen/lampac/main/install.sh | sudo bash -s -- --update
```

### Установка предрелизной (Pre-release) версии

```bash
curl -fsSL https://raw.githubusercontent.com/lampac-nextgen/lampac/main/install.sh | sudo bash -s -- --pre-release
```

### Удаление службы

```bash
curl -fsSL https://raw.githubusercontent.com/lampac-nextgen/lampac/main/install.sh | sudo bash -s -- --remove
```

### Переменные окружения для скрипта установки

Скрипт поддерживает кастомизацию через переменные окружения, которые можно передать перед запуском:

| Переменная | Значение по умолчанию | Описание |
| --- | --- | --- |
| `LAMPAC_INSTALL_ROOT` | `/opt/lampac` | Директория, в которую будет установлен сервер. |
| `LAMPAC_USER` | `lampac` | Имя системного пользователя, под которым будет работать процесс. |
| `LAMPAC_PORT` | `9118` | Порт, который будет слушать приложение. |

### Управление службой (systemd)

После нативной установки управление сервером осуществляется через стандартные команды systemctl:

```bash
sudo systemctl start lampac   # Запустить
sudo systemctl stop lampac    # Остановить
sudo systemctl status lampac  # Проверить статус
sudo systemctl restart lampac # Перезапустить
sudo journalctl -u lampac -f  # Просмотр логов в реальном времени
```
Конфигурационный файл при таком типе установки находится по пути `/opt/lampac/init.conf`.

---

## 3. Ручная сборка из исходников

Если вы хотите собрать проект самостоятельно, убедитесь, что у вас установлен **.NET SDK 10.0+**.

### Сборка и запуск

1. Склонируйте репозиторий:
   ```bash
   git clone https://github.com/lampac-nextgen/lampac.git
   cd lampac
   ```

2. Выполните сборку с помощью скрипта (обертка над `dotnet publish`):
   ```bash
   ./build.sh
   ```
   *(Или напрямую: `dotnet publish Core/Core.csproj -c Release -o publish`)*

3. Запустите собранное приложение:
   ```bash
   cd publish
   dotnet Core.dll
   ```

### Проверка компиляции решения

Если вы разрабатываете модули, вы можете проверить сборку всего решения (`NextGen.slnx`):
```bash
dotnet build NextGen.slnx
```

### Кросс-компиляция

Для сборки под конкретную архитектуру (например, для ARM-серверов), укажите переменную `RUNTIME_ID`:
```bash
RUNTIME_ID=linux-arm64 ./build.sh
```