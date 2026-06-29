# Транскодинг видео и аудио

Включите модуль в `init.conf`/`init.yaml`:

```json
"gst": {
  "enable": true
}
```

Добавьте адрес плагина на устройстве, где требуется транскодинг:

```text
http://IP:9118/gst.js
```

## Настройки

| Параметр | По умолчанию | Описание |
|---|---:|---|
| `enable` | `false` | Включает модуль. |
| `allowed_uids` | не задано | Список разрешённых UID или токенов. Если не задан, модуль доступен всем пользователям. |
| `conf_uids` | не задано | Индивидуальные настройки pipeline для конкретных UID. |
| `inactiveMinutes` | `10` | Через сколько минут без активности остановить задачу транскодинга. |
| `gst_version` | `1.28` | Версия установленного GStreamer. Для версии ниже 1.28 укажите фактическое значение, например `1.26`. |
| `PATH` | `C:\Program Files\gstreamer\1.0\mingw_x86_64` | Корневой каталог GStreamer в Windows. |
| `tempfs` | `true` | Использовать дисковый кольцевой буфер для входного HTTP-потока в `cache/gstranscoding`. |
| `tempfs_ring` | `0` | Каждая единица ~30s буфера (10 = ~300s). |
| `segment_seconds` | `6` | Целевая длительность HLS/fMP4-сегмента в секундах. |
| `aac_bitrate` | `256` | Битрейт AAC в кбит/с. |
| `aac_samplerate` | авто | Частота дискретизации AAC в Гц. По умолчанию берётся из исходной дорожки. |
| `aac_channels` | авто | Количество каналов AAC. По умолчанию берётся из исходной дорожки (до 7.1 / 8 каналов). |
| `video_bitrate` | `10000` | Битрейт H.264 в кбит/с при перекодировании видео. |
| `transcodeH264` | `false` | Перекодировать входной H.264 в H.264. |
| `transcodeH265` | `false` | Перекодировать H.265 в H.264. |
| `transcodeAV1` | `false` | Перекодировать AV1 в H.264. |
| `transcodeVP9` | `false` | Перекодировать VP9 в H.264. |
| `pipeline_downloadRate` | `0` без ограничений | Максимальная скорость загрузки в Мбит/c. |

Полный пример:

```json
"gst": {
  "enable": true,
  "allowed_uids": [
    "device-uid-1",
    "device-uid-2"
  ],
  "inactiveMinutes": 5,
  "gst_version": 1.28,
  "PATH": "C:\\Program Files\\gstreamer\\1.0\\mingw_x86_64",

  "tempfs": true,
  "tempfs_ring": 1,

  "segment_seconds": 6,
  "aac_bitrate": 256,
  "video_bitrate": 5000,

  "transcodeH264": false,
  "transcodeH265": true,
  "transcodeAV1": true,
  "transcodeVP9": true
}
```

В этом примере H.264 передаётся без перекодирования, а H.265, AV1 и VP9 преобразуются в H.264. Доступ разрешён только двум указанным UID.

### Настройки для отдельных UID

`conf_uids` позволяет задать другой pipeline для конкретного устройства. Верхнеуровневые `enable`, `allowed_uids`, `inactiveMinutes`, `gst_version` и `PATH` остаются общими.

```json
"gst": {
  "enable": true,
  "allowed_uids": [
    "tv-uid",
    "mobile-uid"
  ],

  "tempfs": true,
  "tempfs_ring": 1,
  "segment_seconds": 6,
  "aac_bitrate": 256,
  "video_bitrate": 5000,
  "transcodeH264": false,
  "transcodeH265": true,
  "transcodeAV1": true,
  "transcodeVP9": true

  "conf_uids": {
    "mobile-uid": {
      "tempfs": true,
      "tempfs_ring": 1,
      "segment_seconds": 4,
      "aac_bitrate": 192,
      "video_bitrate": 3000,
      "transcodeH264": true,
      "transcodeH265": true,
      "transcodeAV1": true,
      "transcodeVP9": true
    }
  }
}
```

Для `mobile-uid` будут использоваться сегменты по 4 секунды, видео 3000 кбит/с и AAC 192 кбит/с. Остальные UID используют основные настройки.

## Linux (Debian/Ubuntu)

```bash
apt-get update

apt-get install -y --no-install-recommends \
    libgstreamer1.0-0 \
    libgstreamer-plugins-base1.0-0 \
    gstreamer1.0-plugins-base \
    gstreamer1.0-plugins-good \
    gstreamer1.0-plugins-bad \
    gstreamer1.0-plugins-base-apps \
    gstreamer1.0-plugins-ugly \
    gstreamer1.0-libav \
    gstreamer1.0-tools \
    ca-certificates
```

Проверка версии:

```bash
gst-inspect-1.0 --version
```

Если версия ниже 1.28, укажите её в настройках:

```json
"gst": {
  "gst_version": 1.26
}
```

## Windows portable (MinGW)

Уже включена в модуль и не требует установки MinGW

## Или Windows installer (MinGW)

Скачайте и установите:

https://gstreamer.freedesktop.org/data/pkg/windows/1.28.3/mingw/gstreamer-1.0-mingw-x86_64-1.28.3.exe

Во время установки выберите:

```text
Install mode: Only runtime
```

Путь по умолчанию:

```text
C:\Program Files\gstreamer\1.0\mingw_x86_64
```

Если MinGW установлен в другое место, укажите каталог в настройке `PATH` модуля.

## macOS

Скачайте и установите GStreamer 1.28.3 Runtime Installer:

https://gstreamer.freedesktop.org/download/#macos
