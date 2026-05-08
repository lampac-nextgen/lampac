# TmdbProxy

Прокси к **The Movie Database (TMDB)** по префиксу **`/tmdb/`**: HTTP/2 клиенты для API и изображений, агрессивное кеширование, плагин **`tmdbproxy.js`** для Lampa.

## Назначение

- Единая точка доступа к TMDB с вашего домена (обход лимитов и смешанного контента в браузере).
- Кеш ответов API и постеров настраивается в секции **`tmdb`**.

## HTTP

| Маршрут | Описание |
|---------|----------|
| `GET tmdbproxy.js`, `tmdbproxy/js/{token}` | Клиентский плагин с подстановкой хоста и токена. |
| `GET tmdb/{*suffix}` | Прокси на api.themoviedb.org и CDN изображений (логика rewrite и заголовков — в `Controller.cs`). |

## Конфигурация

Секция в `init.conf`: **`tmdb`** (`ModuleInvoke.Init("tmdb", …)`).

Поля по умолчанию в `ModInit`:

- **`httpversion`** = **2** (HTTP/2);
- **`cache_api`** — TTL JSON API (минуты);
- **`cache_img`** — TTL изображений;
- **`responseContentLength`**;
- **`limit_map`** — WAF для **`^/tmdb/`**.

**`CacheFileWatcher`** с ключом **`"tmdb"`** синхронизирует инвалидацию кеша с файловыми триггерами.

## Зависимости

- Рабочий доступ к TMDB из среды, где запущен Lampac.

## Файлы

| Файл | Роль |
|------|------|
| `Controller.cs` | HTTP/2 клиенты, прокси-путь, кеш. |
| `ModInit.cs` | Конфиг и WAF. |
