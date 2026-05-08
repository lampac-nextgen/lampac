# CubProxy

Прокси к **CUB** (внешнему API каталога): запросы под **`/cub/…`** пересылаются на домен из **`CoreInit.conf.cub`**, с кешированием ответов и изображений через **`CacheFileWatcher`** и **`HybridCache`**.

## Назначение

- Позволяет клиенту Lampa ходить к CUB через ваш сервер (единый origin, меньше блокировок).
- Выдаёт плагин **`cubproxy.js`** для подстановки `{localhost}` и `{token}`.

## HTTP

| Маршрут | Описание |
|---------|----------|
| `GET cubproxy.js`, `cubproxy/js/{token}` | JS-плагин прокси CUB. |
| `GET/POST cub/{*suffix}` | Catch-all прокси: суффикс дополняется к базовому URL CUB (scheme/domain/mirror из корневого конфига **cub**). |

## Конфигурация

Секция в `init.conf`: **`cub`** (ключ в `ModuleInvoke.Init("cub", …)`).

В `ModInit` дополнительно задаются:

- **`viewru`**, **`responseContentLength`**;
- **`scheme`**, **`domain`**, **`mirror`** — подтягиваются из **`CoreInit.conf.cub`** при загрузке;
- **`cache_api`**, **`cache_img`** — TTL кеша API и картинок (минуты);
- **`limit_map`** — WAF для **`^/cub/`**.

Файловый вотчер: **`CacheFileWatcher.Configure("cub", conf.cache_img)`** — инвалидация кеша при изменении связанных файлов.

## Зависимости

- В корневом **`init.conf`** должна быть заполнена секция глобального **`cub`** (хост зеркала и т.д.).

## Файлы

| Файл | Роль |
|------|------|
| `Controller.cs` | Проксирование и кеш. |
| `ModInit.cs` | Конфиг, WAF, вотчер. |
