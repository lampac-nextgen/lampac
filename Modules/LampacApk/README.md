# Lampac APK Generator

## Русский

`LampacApk` - модуль для Lampac, который генерирует Android APK под адрес конкретного сервера Lampac.

Пользователь открывает:

```text
http://your-lampac-host:9118/lampac.apk
```

или:

```text
http://your-lampac-host:9118/android.apk
```

Lampac берет unsigned template APK, записывает адрес сервера в `assets/lampac.json`, выполняет `zipalign`, подписывает APK локальным ключом и отдает файл `lampac.apk`.

APK основан на full Android-клиенте LAMPA/Lampac с native-слоем, Crosswalk/WebView, WebSocket/localStorage и поддержкой выбора внешних плееров. Иконки приложения взяты из стандартных LAMPA assets из `yumata/lampa-source`, без кастомного брендинга.

### Системные зависимости

На сервере без Docker нужны:

- Java/JDK с `keytool`
- `apksigner`
- `zipalign`

Для Debian/Ubuntu:

```bash
apt update
apt install -y default-jdk-headless apksigner zipalign
```

Проверка:

```bash
java -version
keytool -help | head -n 1
apksigner version || apksigner --version
zipalign -h | head -n 2
```

### Docker

Если Lampac запущен в Docker, зависимости должны быть установлены внутри контейнера или кастомного Docker-образа. Установка `default-jdk-headless`, `apksigner` и `zipalign` на хосте поможет только для установки без Docker.

Для Debian/Ubuntu-based образа пример Dockerfile-слоя:

```dockerfile
RUN apt-get update \
    && apt-get install -y --no-install-recommends default-jdk-headless apksigner zipalign \
    && rm -rf /var/lib/apt/lists/*
```

Если образ основан не на Debian/Ubuntu, используйте соответствующий пакетный менеджер или добавьте Android SDK build-tools с `apksigner` и `zipalign` вручную.

### Установка

В репозитории Lampac модуль находится в:

```text
Modules/LampacApk
```

При публикации релиза Lampac этот каталог копируется в runtime-папку рядом с `Core.dll`:

```text
module/LampacApk
```

Если модуль уже входит в ваш релиз Lampac, отдельно распаковывать его не нужно. Установите системные зависимости, перезапустите Lampac и откройте `/lampac.apk`.

Для ручной установки вне релиза используйте путь:

```text
/opt/lampac/module/LampacApk
```

Старый путь с пользовательскими модами тоже поддерживается:

```text
/opt/lampac/mods/LampacApk
```

Не держите две копии одновременно. Если модуль есть и в `mods`, и в `module`, Lampac загрузит первую найденную копию, а другая может быть проигнорирована.

После установки перезапустите Lampac:

```bash
systemctl restart lampac
```

### Проверка

```bash
curl -L -D /tmp/lampac-apk.headers \
  -o /tmp/lampac.apk \
  http://127.0.0.1:9118/lampac.apk

cat /tmp/lampac-apk.headers
file /tmp/lampac.apk
apksigner verify --verbose /tmp/lampac.apk
unzip -p /tmp/lampac.apk assets/lampac.json
```

Ожидаемые заголовки:

```text
Content-Type: application/vnd.android.package-archive
Content-Disposition: attachment; filename="lampac.apk"
```

`assets/lampac.json` должен содержать адрес вашего Lampac:

```json
{
  "startUrl": "http://your-lampac-host:9118/"
}
```

Если адрес нужно задать явно:

```text
http://your-lampac-host:9118/lampac.apk?overwritehost=http://192.168.1.10:9118
```

### Важно

APK использует стандартный package:

```text
app.lampac.webview
```

Если на устройстве уже установлен старый тестовый APK с таким же package, перед установкой нового APK удалите старое приложение `Lampac`.

Сгенерированные APK кешируются в:

```text
/opt/lampac/cache/widgets/android
```

Модуль автоматически чистит кеш: оставляет актуальные сгенерированные APK, ограничивает количество кешированных APK и удаляет старые временные директории `tmp-*`.

Keystore создается автоматически при первой генерации APK:

```text
/opt/lampac/database/lampac-apk/lampac-apk.jks
```

Для новых установок пароль создается локально:

```text
/opt/lampac/database/lampac-apk/lampac-apk.pass
```

Храните `lampac-apk.jks` и `lampac-apk.pass` как приватный ключ. Если их потерять или удалить, новые APK будут подписаны другим ключом, и Android не сможет обновить уже установленное приложение без удаления старого.

### Безопасность

APK открывает адрес, записанный в `assets/lampac.json`, и имеет широкий Android JavaScript bridge, как у full LAMPA Android-клиента. Генерируйте APK только под свой доверенный Lampac.

HTTP и локальные сертификаты поддерживаются для домашних/локальных установок. Для публичного сервера лучше использовать HTTPS.

---

## English

`LampacApk` is a Lampac module that generates an Android APK for a specific Lampac server address.

The user opens:

```text
http://your-lampac-host:9118/lampac.apk
```

or:

```text
http://your-lampac-host:9118/android.apk
```

Lampac takes the unsigned template APK, writes the server URL to `assets/lampac.json`, runs `zipalign`, signs the APK with a local key, and returns it as `lampac.apk`.

The APK is based on the full LAMPA/Lampac Android client with the native layer, Crosswalk/WebView, WebSocket/localStorage, and external player selection support. The app icons are taken from the standard LAMPA assets in `yumata/lampa-source`, without custom branding.

### System Dependencies

On a non-Docker server, install:

- Java/JDK with `keytool`
- `apksigner`
- `zipalign`

For Debian/Ubuntu:

```bash
apt update
apt install -y default-jdk-headless apksigner zipalign
```

Check:

```bash
java -version
keytool -help | head -n 1
apksigner version || apksigner --version
zipalign -h | head -n 2
```

### Docker

If Lampac runs in Docker, the dependencies must be installed inside the container or a custom Docker image. Installing `default-jdk-headless`, `apksigner`, and `zipalign` on the host only helps non-Docker installations.

For a Debian/Ubuntu-based image, an example Dockerfile layer is:

```dockerfile
RUN apt-get update \
    && apt-get install -y --no-install-recommends default-jdk-headless apksigner zipalign \
    && rm -rf /var/lib/apt/lists/*
```

If the image is not based on Debian/Ubuntu, use the matching package manager or add Android SDK build-tools with `apksigner` and `zipalign` manually.

### Installation

In the Lampac repository, the module is located at:

```text
Modules/LampacApk
```

When a Lampac release is published, this directory is copied to the runtime folder next to `Core.dll`:

```text
module/LampacApk
```

If the module is already included in your Lampac release, you do not need to extract it manually. Install the system dependencies, restart Lampac, and open `/lampac.apk`.

For manual installation outside a bundled release, use:

```text
/opt/lampac/module/LampacApk
```

The old user-mod path is also supported:

```text
/opt/lampac/mods/LampacApk
```

Do not keep two copies at the same time. If the module exists in both `mods` and `module`, Lampac will load the first matching copy and another one may be ignored.

Then restart Lampac:

```bash
systemctl restart lampac
```

### Verification

```bash
curl -L -D /tmp/lampac-apk.headers \
  -o /tmp/lampac.apk \
  http://127.0.0.1:9118/lampac.apk

cat /tmp/lampac-apk.headers
file /tmp/lampac.apk
apksigner verify --verbose /tmp/lampac.apk
unzip -p /tmp/lampac.apk assets/lampac.json
```

Expected headers:

```text
Content-Type: application/vnd.android.package-archive
Content-Disposition: attachment; filename="lampac.apk"
```

`assets/lampac.json` should contain your Lampac URL:

```json
{
  "startUrl": "http://your-lampac-host:9118/"
}
```

To force a specific URL:

```text
http://your-lampac-host:9118/lampac.apk?overwritehost=http://192.168.1.10:9118
```

### Important

The APK uses the standard package:

```text
app.lampac.webview
```

If an older test APK with the same package is already installed on the device, uninstall the old `Lampac` app before installing the new APK.

Generated APK files are cached in:

```text
/opt/lampac/cache/widgets/android
```

The module cleans the cache automatically: it keeps the active generated APK files, limits the number of cached APK files, and removes old temporary `tmp-*` build directories.

The signing keystore is created automatically on the first APK generation:

```text
/opt/lampac/database/lampac-apk/lampac-apk.jks
```

For new installations, the password is generated locally:

```text
/opt/lampac/database/lampac-apk/lampac-apk.pass
```

Keep `lampac-apk.jks` and `lampac-apk.pass` as a private key. If they are lost or deleted, newly generated APK files will be signed with a different key, and Android will not update the already installed app unless the old app is removed first.

### Security

The APK opens the URL stored in `assets/lampac.json` and has a broad Android JavaScript bridge, as in the full LAMPA Android client. Generate APK files only for your own trusted Lampac server.

HTTP and local certificates are supported for home/local installations. HTTPS is recommended for public servers.

---

## Українською

`LampacApk` - модуль для Lampac, який генерує Android APK під адресу конкретного сервера Lampac.

Користувач відкриває:

```text
http://your-lampac-host:9118/lampac.apk
```

або:

```text
http://your-lampac-host:9118/android.apk
```

Lampac бере unsigned template APK, підставляє адресу сервера в `assets/lampac.json`, виконує `zipalign`, підписує APK локальним ключем і віддає файл `lampac.apk`.

APK базується на full Android-клієнті LAMPA/Lampac з native-шаром, Crosswalk/WebView, WebSocket/localStorage і підтримкою вибору зовнішніх плеєрів. Іконки додатка взяті зі стандартних LAMPA assets з `yumata/lampa-source`, без кастомного брендингу.

### Системні залежності

На сервері без Docker потрібні:

- Java/JDK з `keytool`
- `apksigner`
- `zipalign`

Для Debian/Ubuntu:

```bash
apt update
apt install -y default-jdk-headless apksigner zipalign
```

Перевірка:

```bash
java -version
keytool -help | head -n 1
apksigner version || apksigner --version
zipalign -h | head -n 2
```

### Docker

Якщо Lampac запущений у Docker, залежності мають бути встановлені всередині контейнера або кастомного Docker-образу. Встановлення `default-jdk-headless`, `apksigner` і `zipalign` на хості допоможе тільки для інсталяції без Docker.

Для Debian/Ubuntu-based образу приклад Dockerfile-шару:

```dockerfile
RUN apt-get update \
    && apt-get install -y --no-install-recommends default-jdk-headless apksigner zipalign \
    && rm -rf /var/lib/apt/lists/*
```

Якщо образ не базується на Debian/Ubuntu, використовуйте відповідний пакетний менеджер або додайте Android SDK build-tools з `apksigner` і `zipalign` вручну.

### Встановлення

У репозиторії Lampac модуль знаходиться в:

```text
Modules/LampacApk
```

Під час публікації релізу Lampac цей каталог копіюється в runtime-папку поруч із `Core.dll`:

```text
module/LampacApk
```

Якщо модуль уже входить у ваш реліз Lampac, окремо розпаковувати його не потрібно. Встановіть системні залежності, перезапустіть Lampac і відкрийте `/lampac.apk`.

Для ручного встановлення поза релізом використовуйте шлях:

```text
/opt/lampac/module/LampacApk
```

Старий шлях з користувацькими модами теж підтримується:

```text
/opt/lampac/mods/LampacApk
```

Не тримайте дві копії одночасно. Якщо модуль є і в `mods`, і в `module`, Lampac завантажить першу знайдену копію, а інша може бути проігнорована.

Після встановлення перезапустіть Lampac:

```bash
systemctl restart lampac
```

### Перевірка

```bash
curl -L -D /tmp/lampac-apk.headers \
  -o /tmp/lampac.apk \
  http://127.0.0.1:9118/lampac.apk

cat /tmp/lampac-apk.headers
file /tmp/lampac.apk
apksigner verify --verbose /tmp/lampac.apk
unzip -p /tmp/lampac.apk assets/lampac.json
```

Очікувані заголовки:

```text
Content-Type: application/vnd.android.package-archive
Content-Disposition: attachment; filename="lampac.apk"
```

`assets/lampac.json` має містити адресу вашого Lampac:

```json
{
  "startUrl": "http://your-lampac-host:9118/"
}
```

Якщо адресу треба задати явно:

```text
http://your-lampac-host:9118/lampac.apk?overwritehost=http://192.168.1.10:9118
```

### Важливо

APK має стандартний package:

```text
app.lampac.webview
```

Якщо на пристрої вже встановлений старий тестовий APK з таким самим package, перед встановленням нового APK видаліть старий додаток `Lampac`.

Згенеровані APK кешуються в:

```text
/opt/lampac/cache/widgets/android
```

Модуль автоматично чистить кеш: залишає актуальні згенеровані APK, обмежує кількість кешованих APK і видаляє старі тимчасові `tmp-*` директорії.

Keystore створюється автоматично при першій генерації APK:

```text
/opt/lampac/database/lampac-apk/lampac-apk.jks
```

Для нових інсталяцій пароль створюється локально:

```text
/opt/lampac/database/lampac-apk/lampac-apk.pass
```

Зберігайте `lampac-apk.jks` і `lampac-apk.pass` як приватний ключ. Якщо їх втратити або видалити, нові APK будуть підписані іншим ключем, і Android не зможе оновити вже встановлений додаток без видалення старого.

### Безпека

APK відкриває адресу, записану в `assets/lampac.json`, і має широкий Android JavaScript bridge, як у full LAMPA Android-клієнті. Генеруйте APK тільки під власний довірений Lampac.

HTTP і локальні сертифікати підтримуються для домашніх/локальних інсталяцій. Для публічного сервера краще використовувати HTTPS.
