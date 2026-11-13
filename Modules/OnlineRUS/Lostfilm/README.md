# LostFilm

Опциональный торрент-источник **LostFilm.TV** для Lampac NextGen. Модуль выключен от инфраструктуры по умолчанию: источник появляется в выдаче только если оператор настроил внешний TorrServer.

## Возможности

- Поиск и получение сезонов/эпизодов через LostFilm.TV.
- Получение torrent-файла эпизода и передача его во внешний TorrServer.
- Пользовательская авторизация LostFilm через `/bind/lostfilm` с captcha.
- Маркеры просмотренного, подписки, расписание, новинки и каталог.
- Клиентский плагин `plugins/lostfilm.js` для Lampa.

## Конфигурация

Секция `Lostfilm` в операторском `init.conf`:

```json
{
  "Lostfilm": {
    "host": "https://www.lostfilm.tv",
    "imgHost": "https://static.lostfilm.top",
    "torrs": ["http://127.0.0.1:8090"]
  }
}
```

`host`, `imgHost`, `ua`, `cookie`, `login`, `passwd`, `torrs` и `auth_torrs` являются операторскими настройками. Не добавляйте в репозиторий реальные cookie, логины, пароли, URL приватных TorrServer или другие секреты.

Без `torrs` или `auth_torrs` источник намеренно не регистрируется в online API.

## Плагин Lampa

После публикации файл доступен как `/plugins/lostfilm.js`. Подключите его в своей конфигурации LampaWeb, например:

```json
{
  "LampaWeb": {
    "customPlugins": [
      {
        "url": "{localhost}/plugins/lostfilm.js",
        "name": "LostFilm",
        "status": 1
      }
    ]
  }
}
```

`{localhost}` заменяется вашим адресом Lampac. Плагин использует публичные Lampa API и передаёт пользовательскую LostFilm-сессию только в запросах к маршрутам этого модуля.

## Маршруты

- `/lite/lostfilm` — основная выдача.
- `/lite/lostfilm/status` — состояние привязки.
- `/lite/lostfilm/my`, `/new`, `/schedule`, `/catalog` — данные для плагина.
- `/bind/lostfilm` — привязка аккаунта и captcha.
