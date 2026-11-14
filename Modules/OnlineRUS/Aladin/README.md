# Aladin

Онлайн-источник **Aladin** (`ModuleConf`): балансер на базе источника Alloha.

- Вычисление сигнатуры `Borth` из `viewporti` плеера.
- Получение потоков через `/bnsi/movies/{id}` по вебмастерскому токену.
- Поддержка WebSocket-телеметрии и токенов защиты (`Accepts-Controls`).
- Поддержка качеств до 2160p (`m4s`), нескольких озвучек и субтитров.
- Native-путь (единственный): страница плеера, балансер, телеметрия и CDN
  запрашиваются с TLS/HTTP2-отпечатком Chrome через нативный `libcurl-impersonate`.

## Маршруты

| Маршрут | Описание |
|---|---|
| `lite/aladin` | Выдача переводов и серий |
| `lite/aladin/video` | Плеер и ссылки на видео |
| `lite/aladin/video.m3u8` | Плейлист HLS |
| `lite/aladin/hls/{id}/v/{voice}/{quality}.m3u8` | HLS-плейлист качества |
| `lite/aladin/hls/{id}/item/{key}` | Сегмент или вложенный плейлист |
| `lite/aladin-search` | Поиск по названию |

## Конфигурация в `init.conf`

```jsonc
"Aladin": {
  "enable": true,
  "displayname": "Aladin",
  "displayindex": 512,
  "token": "22c8122334d050de1bfc97bd08aa5e",
  "linkhost": "https://scalp-as.stloadi.live",
  "apihost": "https://apbugall.org/v2",
  "m4s": true,
  "spider": true,
  "httpversion": 2,
  "tlsProfile": "chrome_133",
  "nativeLibrary": "",
  "referer": "https://kinogo.media/",
  "acceptLanguage": "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7",
  "linkhosts": [
    "https://scalp-as.stloadi.live",
    "https://quadrillion-as.allarknow.online",
    "https://Aport-as.allarknow.online"
  ],
  "nativeProxy": "",
  "httpRetries": 3,
  "resolveTimeout": 8,
  "urlTtl": 7200,
  "wsReconnectAttempts": 3,
  "wsMaxSessions": 12,
  "wsHandshakeConcurrency": 2,
  "liveMaxStreams": 256,
  "liveStreamTtl": 14400
}
```

## Параметры

- **`token`**: вебмастерский токен балансера.
- **`linkhost`**: адрес плеера / CDN (`https://scalp-as.stloadi.live`).
- **`apihost`**: адрес API каталога (`https://apbugall.org/v2`).
- **`m4s`**: `true` — включение качеств 1440p и 2160p (UHD), `false` — до 1080p.
- **`spider`**: сквозной поиск по названию в Lampa.
- **`tlsProfile`**: профиль отпечатка (`chrome_133` по умолчанию).
- **`authorizations`**: статический bearer CDN; по умолчанию публичная константа плеера.
- **`fingerprintHash`**: стабильный fingerprint hash для `Borth` и `Accepts-Controls`.
- **`nativeLibrary`**: путь к `libcurl-impersonate.so` / `.dylib`; пусто — поиск в `native/`.
- **`referer`**: Referer страницы плеера.
- **`acceptLanguage`**: Accept-Language нативного клиента.
- **`linkhosts`**: пул плеерных хостов для резолва.
- **`nativeProxy`**: необязательный HTTP/SOCKS-прокси для нативного клиента.
- **`httpRetries`**: попыток на страницу плеера.
- **`resolveTimeout`**: жёсткий дедлайн резолва, секунды.
- **`urlTtl`**: время жизни закэшированного резолва, секунды (7200).
- **`wsReconnectAttempts`**: попыток переподключения телеметрии.
- **`wsMaxSessions`**: одновременно живых WS-сессий (по умолчанию 12); старые закрываются.
- **`wsHandshakeConcurrency`**: одновременных WS-handshake (по умолчанию 2).
- **`liveMaxStreams`**, **`liveStreamTtl`**: лимиты реестра живых потоков.

`authorizations` и `fingerprintHash` имеют рабочие значения по умолчанию.
`authorizations` — публичная константа плеера; её можно переопределить в
`init.conf`, не коммитя значение в репозиторий.

## Нативная библиотека

Используется `libcurl-impersonate` `2.3.2` (C/BoringSSL, MIT). Установщик и Docker
кладут её в `native/libcurl-impersonate.so.4.8.0`; локально можно задать
`ALADIN_CURL_LIBRARY` (или `nativeLibrary`). Без библиотеки native-резолв не
запустится, в лог пишется предупреждение.

`libcurl-impersonate` — обычная C-библиотека без Go runtime и его обработчиков
сигналов, поэтому она работает в одном процессе с Chrome/Playwright.
Профиль `chrome_133` отображается на `chrome133a`.
