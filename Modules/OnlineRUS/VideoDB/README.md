# VideoDB

Модуль **VideoDB**: загружает **`OnlinesSettings`** в статический **`VideoDB.conf`** для **`Controller`** (раздача **`lite/videodb`** и манифестов). **`IModuleOnline`** в **`ModInit` не реализован** — регистрация в общем списке онлайна выполняется хостом при обращении к маршрутам модуля.

## Интерфейс

**`IModuleLoaded`** — только **`Loaded`/`Dispose`** и **`EventListener.UpdateInitFile`**.

## Глобальный поиск / качество

Нет **`with_search`** и нет **`OnlineApiQuality`** в этом **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`VideoDB`** (`OnlinesSettings`).

По умолчанию: хост **`https://kinogo.media`**, **`streamproxy = true`**, **`httpversion = 2`**, **`httptimeout = 30`**, **`priorityBrowser = "http"`**, **`imitationHuman = true`**, заголовки страницы и потока под **`kinogo.media`**.

**`httptimeout = 30`**: embed-страница obrut.show отдаёт первый байт через 9–10 с, при общем дефолте 8 с запрос обрывается и выдача пустая (`503 embed`).

## Антибот obrut.show

Embed-страницы (их использует **ZetflixDB**) закрыты WAF: без cookie **`wd_approval`** сервер отвечает **`403`** со страницей **`/include/wsdk.js`**. Cookie выдаётся только настоящему браузеру и привязана к исходящему IP, поэтому её нужно получить в браузере с IP сервера Lampac и прописать в заголовки:

```json
"VideoDB": {
  "headers": {
    "cookie": "wd_approval=<значение>"
  }
}
```

Секция сливается с дефолтами рекурсивно, остальные заголовки сохраняются. Cookie **`wd_trust`** не нужна, без **`wd_approval`** она не работает. Referer **`{host}/`** обязателен: без него obrut отвечает **`404`**.

### Автоматическое обновление cookie (`cdp`)

Вместо ручной cookie можно указать Chrome с открытым DevTools-портом:

```json
"VideoDB": {
  "cdp": "http://chrome:9222"
}
```

Если embed-страница вернула не плеер, **`ChromeCookie`** открывает её во вкладке Chrome (`Target.createTarget`), ждёт до 20 с появления новой **`wd_approval`** (`Storage.getCookies`), закрывает вкладку и повторяет запрос. Cookie хранится в памяти процесса и добавляется к запросам страницы и манифеста. Обращение к Chrome — не чаще раза в минуту. **`Runtime.enable`** и другие домены автоматизации не используются: их детектирует антибот. Ручную cookie в **`headers`** при этом не задавайте.

Требования:

- Chrome **не headless** и выходит в сеть с того же IP, что и Lampac (cookie привязана к IP).
- Chrome 136+ не открывает DevTools-порт на профиле по умолчанию — нужен отдельный **`--user-data-dir`**.
- DevTools принимает только **`Host`** в виде IP или **`localhost`**; имя хоста из **`cdp`** Lampac резолвит в IPv4 сам.
- Порт даёт полный контроль над браузером (все его сессии и cookie). Держите его только во внутренней сети, не публикуйте и не пропускайте через reverse proxy.

Пример для `linuxserver/chrome`. В обычном (не headless) режиме Chrome слушает DevTools только на `127.0.0.1`, поэтому порт выводится в сеть через `socat` в том же сетевом namespace:

```yaml
services:
  chrome:
    image: lscr.io/linuxserver/chrome
    environment:
      CHROME_CLI: --remote-debugging-port=9222 --user-data-dir=/config/cdp-profile
  chrome-cdp:
    image: alpine/socat
    network_mode: service:chrome
    command: tcp-listen:9223,fork,reuseaddr tcp:127.0.0.1:9222
```

В этом случае **`"cdp": "http://chrome:9223"`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/videodb`** | Основная выдача. |
| **`lite/videodb/manifest`**, **`lite/videodb/manifest.m3u8`** | Манифест / HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`Service.cs`**, **`Model.cs`**, **`ChromeCookie.cs`**.
