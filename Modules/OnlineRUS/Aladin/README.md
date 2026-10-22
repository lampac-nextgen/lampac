# Aladin

Онлайн-источник **Aladin** (`ModuleConf`): балансер на базе источника Alloha.

- Вычисление сигнатуры `Borth` из `viewporti` плеера.
- Получение потоков через `/bnsi/movies/{id}` по вебмастерскому токену.
- Поддержка WebSocket-телеметрии и токенов защиты (`Accepts-Controls`).
- Поддержка качеств до 2160p (`m4s`), нескольких озвучек и субтитров.

## Маршруты

| Маршрут | Описание |
|---|---|
| `lite/aladin` | Выдача переводов и серий |
| `lite/aladin/video` | Плеер и ссылки на видео |
| `lite/aladin/video.m3u8` | Плейлист HLS |
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
  "reserve": true,
  "spider": true,
  "httpversion": 2
}
```

## Параметры

- **`token`**: вебмастерский токен балансера.
- **`linkhost`**: адрес плеера / CDN (`https://scalp-as.stloadi.live`).
- **`apihost`**: адрес API каталога (`https://apbugall.org/v2`).
- **`m4s`**: `true` — включение качеств 1440p и 2160p (UHD), `false` — до 1080p.
- **`reserve`**: использовать резервные CDN-потоки при наличии.
- **`spider`**: сквозной поиск по названию в Lampa.
