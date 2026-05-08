# CorsMedia

Прокси для **медиа-ссылок** (изображения, HLS, mp4 и др.): редиректы на внутренние хосты прокси, массовая генерация URL, авторизация по списку токенов из **`CorsMedia.tokens`** в `init.conf`.

Ниже — детальное описание маршрутов и параметров (источник правды — `Controller.cs` модуля).

---

## Требования авторизации

Каждый запрос должен содержать валидный `auth_token`. Список допустимых токенов задаётся в конфигурации `CorsMedia.tokens`. При отсутствии или неверном токене контроллер возвращает `401 Unauthorized` с сообщением об ошибке.

## Маршруты

### GET `/media/rsize/{token}/{width}/{height}/{*url}`

- **Назначение:** Получение прокси-ссылки на изображение с изменением размеров.
- **Параметры пути:**
  - `token` — токен авторизации.
  - `width`, `height` — желаемые размеры. Если одно из значений равно 0, масштабирование происходит пропорционально по другому параметру.
  - `url` — исходная ссылка на изображение, должна быть URL-энкодирована.
- **Результат:** Редирект на внутренний прокси-хост `/proxyimg:{width}:{height}/…` или исходный URL, если прокси отключён.

### GET `/media/{type}/{token}/{*url}`

- **Назначение:** Получение прокси-ссылки на медиа ресурс.
- **Параметры пути:**
  - `type` — тип ресурса (`img`, `hls`, `m3u8`, `mp4` и т.д.).
  - `token` — токен авторизации.
  - `url` — исходная ссылка на ресурс, URL-энкодированная.
- **Результат:** Редирект на прокси или прямой поток.

### GET `/media`

- **Назначение:** Универсальный способ получить прокси-ссылку через query-параметры без жёсткого формата пути.
- **Параметры:**
  - `url` — исходный адрес ресурса.
  - `headers` — JSON-строка с дополнительными заголовками (опционально).
  - Прочие поля (`type`, `auth_token`, `width`, `height`, `proxy`, `proxy_name`, `apnstream`, `useproxystream`) передаются через query-параметры и привязываются к `MediaRequestBase`.
- **Результат:** HTTP редирект на проксированный ресурс.

### POST `/media`

- **Назначение:** Массовая генерация прокси-ссылок.
- **Тело запроса:** JSON-массив `urls` и дополнительные параметры, совместимые с `MediaRequest` (`type`, `auth_token`, `width`, `height`, `proxy`, `headers`, и т.д.).
- **Результат:** JSON-ответ с массивом `urls`, содержащим прокси-ссылки.

## Примеры использования

### Прямой переход из браузера

```text
https://lampac.example.com/media/img/YOUR_TOKEN/https%3A%2F%2Fremote.host%2Fpath%2Fposter.jpg
```

Для изменения размеров изображения:

```text
https://lampac.example.com/media/rsize/YOUR_TOKEN/320/480/https%3A%2F%2Fremote.host%2Fpath%2Fposter.jpg
```

### JavaScript (Fetch API)

```js
const params = new URLSearchParams({
  url: 'https://remote.host/video.m3u8',
  type: 'hls',
  auth_token: 'YOUR_TOKEN',
  useproxystream: true
});

fetch(`https://lampac.example.com/media?${params.toString()}`, {
  redirect: 'follow'
})
  .then(response => {
    if (response.redirected) {
      return response.url;
    }
    throw new Error('Не удалось получить прокси-ссылку');
  })
  .then(proxyUrl => console.log('Проксированная ссылка:', proxyUrl))
  .catch(console.error);
```

### JavaScript (POST несколько ссылок)

```js
fetch('https://lampac.example.com/media', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json'
  },
  body: JSON.stringify({
    auth_token: 'YOUR_TOKEN',
    type: 'img',
    width: 320,
    height: 480,
    urls: [
      'https://remote.host/path/poster1.jpg',
      'https://remote.host/path/poster2.jpg'
    ]
  })
})
  .then(res => res.json())
  .then(({ urls }) => console.log('Проксированные изображения:', urls))
  .catch(console.error);
```

## Обработка заголовков и прокси

- Для передачи дополнительных заголовков используйте JSON-объект в поле `headers` (GET — строка, POST — массив объектов `HeadersModel`).
- Для работы через именованный прокси укажите `proxy_name`, который должен существовать в `AppInit.conf.globalproxy`.
- При прямом указании `proxy` контроллер создаёт временную конфигурацию и применяет её к запросу.

## Обработка ошибок

- `400 Bad Request` возвращается при отсутствии обязательных параметров (`url`, `urls`).
- `401 Unauthorized` — при некорректном токене.
- Ответы об ошибках имеют формат:

```json
{
  "success": false,
  "error": "сообщение"
}
```
