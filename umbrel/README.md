# Lampac в umbrelOS

Каталог `lampac/` — готовый пакет приложения для umbrelOS: манифест
`umbrel-app.yml`, `docker-compose.yml` и `exports.sh`. Скопируйте каталог
`lampac/` целиком в свой App Store (имя каталога должно совпадать с `id` из
манифеста) — больше ничего править не нужно.

Пакет повторяет официальный `docker-compose.yaml` из корня репозитория и
страницу документации [«Деплой в Docker»](../docs/deployment/docker.mdx).
Отличия ровно три:

1. состояние хранится в `${APP_DATA_DIR}` приложения, а не в относительных
   каталогах `lampac-docker/*`;
2. фиксированная bridge-сеть `10.10.10.10` не используется — приложение
   работает в основной сети Umbrel, доступ идет через `app_proxy`;
3. `init.conf` и `passwd` создаёт служебный сервис `init`, потому что umbrelOS
   создает цели монтирования, которых еще нет, каталогами.

## Первый запуск

1. Установите приложение в Umbrel.
2. Откройте его — интерфейс Lampac отдается через прокси Umbrel, поэтому нужен
   вход в Umbrel.
3. Конфигурация лежит в каталоге данных приложения:
   `data/config/init.conf` (стартовое содержимое — `config/example.init.conf`
   из образа), пароль root — `data/config/passwd`, кеш и базы — `data/cache` и
   `data/database`.
4. После правки `data/config/init.conf` перезапустите приложение: сервер
   перечитывает конфигурацию по изменению файла, но перезапуск надежнее.

Пароль root сгенерирован при первом запуске (`exports.sh`, `derive_entropy`) и
хранится в `data/config/passwd`. Он нужен модулям WebLog, AdminPanel и другим
служебным функциям; прочитайте файл, когда пароль потребуется.

## Обновление версии

Образ закреплен по digest, версия приложения — в манифесте. Чтобы выпустить
новую версию:

```bash
# 1. Узнайте digest тега-релиза (multi-arch index).
TOKEN=$(curl -s "https://ghcr.io/token?scope=repository:lampac-nextgen/lampac:pull" \
  | python3 -c "import json,sys;print(json.load(sys.stdin)['token'])")
curl -sI -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/vnd.oci.image.index.v1+json, application/vnd.docker.distribution.manifest.list.v2+json" \
  "https://ghcr.io/v2/lampac-nextgen/lampac/manifests/<версия>" \
  | grep -i docker-content-digest
```

2. Подставьте новый тег и digest в **оба** сервиса `docker-compose.yml`.
3. Поднимите `version` в `umbrel-app.yml` и опишите изменения в `releaseNotes`.
4. Проверьте YAML: `npx -y js-yaml umbrel-app.yml` и `npx -y js-yaml docker-compose.yml`.

## Безопасность

- Lampac — медиапрокси без собственной аутентификации для клиентских запросов.
  Не публикуйте порт `9118` в интернет; приложение рассчитано на доверенную
  локальную сеть.
- Интерфейс по умолчанию закрыт логином Umbrel (`app_proxy`). Если вы добавляете
  пути в `PROXY_AUTH_WHITELIST` для внешних клиентов, эти пути становятся
  доступны без аутентификации.
- Каталог `data` содержит пароль root и базы модулей (Sync, TimeCode, SISI) —
  включайте его в резервные копии и храните как секрет.