# Lampac MCP: самостоятельный сервер управления

Переносимый MCP-мост для администратора собственного Lampac NextGen. Консольное
приложение на .NET 10 использует существующий защищённый AdminPanel API, не изменяет
ядро и не делает чужие экземпляры Lampac доступными.

## Возможности

- `lampac_status` — доступность, число профилей, модулей и плагинов.
- `lampac_plugins` — список пользовательских плагинов без секретных URL (`{"count", "plugins"}`).
- `lampac_plugin_add` / `lampac_plugin_set_enabled` / `lampac_plugin_delete` — управление `LampaWeb.customPlugins`.
- `lampac_modules` / `lampac_module_read` / `lampac_module_write` — просмотр модулей (`{"count", "modules"}`), чтение и защищённая запись их текстовых файлов.
- `lampac_profiles_count` — число профилей без имён и учётных данных.
- `lampac_logs` — ограниченное чтение журнала; отдельно разрешается владельцем.
- `lampac_restart_safe` — вызов установленного скрипта безопасного перезапуска.

Инструменты с объектным результатом объявляют output schema и возвращают structured
content; `lampac_module_read` и `lampac_logs` возвращают обычный текст.

## Сборка и запуск

Нужны .NET SDK 10 (для сборки), включённый модуль AdminPanel и локальный доступ к Lampac.
Исходный manifest AdminPanel в репозитории по умолчанию выключен.

```bash
dotnet build tools/lampac-mcp/LampacMcp.csproj --configuration Release
export LAMPAC_URL=http://127.0.0.1:9118
export LAMPAC_PASSWORD_FILE=/path/to/private/passwd
export LAMPAC_ROOT=/path/to/lampac-runtime
dotnet tools/lampac-mcp/bin/Release/net10.0/lampac-mcp.dll
```

Для однофайловой сборки без установленного рантайма:

```bash
dotnet publish tools/lampac-mcp/LampacMcp.csproj --configuration Release \
  --runtime linux-x64 --self-contained -p:PublishSingleFile=true
```

Пароль берётся из закрытого локального файла, не помещается в репозиторий и не
возвращается MCP-клиенту. Можно использовать `LAMPAC_ADMIN_PASSWORD` вместо
`LAMPAC_PASSWORD_FILE`, но отдельный защищённый файл предпочтительнее. Административные
запросы по HTTP разрешены лишь на loopback, перенаправления не выполняются, cookie
`accspasswd` не уходит на другой origin.

По умолчанию сервер работает по stdio (без сетевого порта); MCP-клиент должен
запускать его как локальный процесс. Это приложение устанавливается отдельно на каждый
сервер Lampac, поэтому учётные данные владельцев остаются изолированными.

## Переменные окружения

| Переменная | Назначение |
|---|---|
| `LAMPAC_URL` | origin AdminPanel, по умолчанию `http://127.0.0.1:9118` |
| `LAMPAC_PASSWORD_FILE` | файл с root-паролем AdminPanel (рекомендуется, chmod 600) |
| `LAMPAC_ADMIN_PASSWORD` | альтернатива файлу; не используйте в общих профилях |
| `LAMPAC_ROOT` | каталог Lampac для инструментов модулей |
| `LAMPAC_MCP_TRANSPORT` | `stdio` (по умолчанию) или `streamable-http` |
| `LAMPAC_MCP_PORT` | порт HTTP-режима, по умолчанию `8765` |
| `LAMPAC_MCP_ALLOW_WRITE` | `1` разрешает изменение плагинов и файлов модулей |
| `LAMPAC_MCP_ALLOW_LOGS` | `1` разрешает `lampac_logs` |
| `LAMPAC_MCP_ALLOW_RESTART` | `1` разрешает `lampac_restart_safe` |
| `LAMPAC_MCP_RESTART_SCRIPT` | путь к скрипту безопасного перезапуска |

## Опциональная запись и перезапуск

По умолчанию изменение конфигурации, запись модулей, журнал и перезапуск запрещены.
Владелец включает строго необходимые разрешения:

```bash
export LAMPAC_MCP_ALLOW_WRITE=1
export LAMPAC_MCP_ALLOW_LOGS=1
export LAMPAC_MCP_ALLOW_RESTART=1
```

Плагины изменяются только в уже существующей секции LampaWeb пользовательского
`init.conf` через `POST /adminpanel/api/init/section/LampaWeb`. Другие корневые секции
конфигурации не перезаписываются. Порядок ключей сохраняется (`JsonNode`), поэтому
после добавления и удаления тестового плагина `init.conf` остаётся побайтово прежним.
При одновременной работе других редакторов этой же секции возможен конфликт записи —
согласуйте административные изменения.

Редактировать можно только существующие разрешённые текстовые файлы внутри
`module/<имя>` размером не более 512 КиБ; путь проходит через `realpath` и не может
выйти за каталог модуля (symlink и `..` отклоняются). Создаётся резервная копия с
суффиксом `.bak-mcp`. Для JSON проверяется синтаксис, для JS обязателен установленный
Node.js (`node --check`). C# автоматически не компилируется: потребуется штатная
пересборка Lampac. Скрипт безопасного перезапуска устанавливается отдельно; путь
задаётся через `LAMPAC_MCP_RESTART_SCRIPT` (по умолчанию
`/usr/local/bin/lampac-restart-safe.sh`). Скрипт сам решает, можно ли перезапускать
сервис: форсированный рестарт в мосте не реализован. Для `lampac_logs` пользователь,
от которого запущен сервер, должен читать журнал systemd (например, входить в группу
`systemd-journal`) — иначе `journalctl` вернёт ошибку.

## Доступ по сети

```bash
LAMPAC_MCP_TRANSPORT=streamable-http LAMPAC_MCP_PORT=8765 \
  dotnet tools/lampac-mcp/bin/Release/net10.0/lampac-mcp.dll
```

Этот режим слушает только `127.0.0.1:8765`, конечная точка — `/mcp`, транспорт
stateless; Kestrel принимает только заголовки `Host` из `localhost;127.0.0.1;[::1]`.
В коде нет встроенной OAuth-авторизации. Для публикации через публичный домен
необходим внешний шлюз с TLS и полноценной OAuth 2.1-аутентификацией, который
проверяет доступ до проксирования MCP-запросов. Не открывайте порт и не проксируйте
endpoint без такой защиты. Один root-пароль AdminPanel не заменяет внешнюю
аутентификацию MCP-клиентов. Для удалённых клиентов ChatGPT нужна совместимая
настройка MCP-приложения и авторизации.

## Проверка

```bash
dotnet build tools/lampac-mcp/LampacMcp.csproj --configuration Release
dotnet format tools/lampac-mcp/LampacMcp.csproj --verify-no-changes
```

Smoke-тест stdio (stdin держим открытым, иначе сервер завершится до ответа):

```bash
{ printf '%s\n' \
    '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"smoke","version":"1"}}}' \
    '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
    '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'; sleep 5; } \
| LAMPAC_URL=http://127.0.0.1:1 LAMPAC_ADMIN_PASSWORD=smoke \
  dotnet tools/lampac-mcp/bin/Release/net10.0/lampac-mcp.dll
```

Тот же сценарий выполняется в CI (`.github/workflows/test-tools-lampac-mcp.yml`).

Для приёмочной проверки на собственной установке:

1. Запустите сервер по stdio и любым MCP-клиентом вызовите `lampac_status`,
   `lampac_plugins` и `lampac_modules` — это только чтение.
2. Проверьте отказы: с выключенным `LAMPAC_MCP_ALLOW_WRITE` вызов `lampac_plugin_add`
   должен вернуть понятную ошибку, а не общий сбой.
3. Включите `LAMPAC_MCP_ALLOW_WRITE=1` и выполните полный цикл на тестовом плагине:
   `lampac_plugin_add` → `lampac_plugins` → `lampac_plugin_set_enabled` →
   `lampac_plugin_delete`; число плагинов должно вернуться к исходному, а `init.conf` —
   к прежнему содержимому.
4. Для модулей укажите `LAMPAC_ROOT` и проверьте `lampac_module_read` /
   `lampac_module_write` на копии каталога `module/` — запись создаёт `.bak-mcp`.
5. Перезапуск и журнал остаются выключенными, пока явно не заданы
   `LAMPAC_MCP_ALLOW_RESTART=1` / `LAMPAC_MCP_ALLOW_LOGS=1`.

## Файлы

| Что | Где |
|---|---|
| Точка входа, транспорты | `tools/lampac-mcp/Program.cs` |
| Настройки из окружения | `tools/lampac-mcp/LampacConfig.cs` |
| Клиент AdminPanel и плагины | `tools/lampac-mcp/PanelClient.cs` |
| Файлы модулей и защита путей | `tools/lampac-mcp/ModuleStore.cs` |
| MCP-инструменты | `tools/lampac-mcp/LampacTools.cs` |
| Модели ответов | `tools/lampac-mcp/Contracts.cs` |
| Пакет MCP SDK | `Directory.Packages.props` (`ModelContextProtocol.AspNetCore`) |
