# iOS Embedded Lock Screen Seek

Документ фиксирует эксперимент с перемоткой во встроенном плеере Lampa на iPhone. Это относится к режиму `currentExternalPlayer() === 'inner'`, где музыка играет через обычный `Lampa.PlayerVideo`, а не к standalone iOS-плееру модуля Music.

## Что хотели получить

- На заблокированном экране iOS должны работать play/pause, next/prev и скраббер перемотки.
- Встроенный плеер внутри Lampa должен оставаться обычным: звук, прогресс и кнопки не должны расходиться между интерфейсом Lampa и lock screen.

## Что уже работает

- Media Session metadata: название, артист, обложка.
- Play/pause на lock screen.
- Next/prev на lock screen, когда очередь Lampa доступна.
- Keep-alive для iOS-аудиосессии.

## Что пробовали

### 1. Прямой `seekto` на stock media

Регистрировали `navigator.mediaSession.setActionHandler('seekto', ...)`, заранее вызывали `setPositionState`, прогревали Media Session в пользовательском жесте.

Результат:

- В видимом плеере данные duration/currentTime были корректные.
- `seekable` у media-элемента был заполнен, значит серверный stream/range был рабочий.
- На lock screen iOS либо не активировал скраббер, либо двигал его без стабильного события `seekto`.

Вывод: для встроенного video/audio Lampa одного Media Session API недостаточно. iOS не гарантирует, что lock-screen scrubber будет вызывать наш JS-handler.

### 2. `seekforward` / `seekbackward`

Проверяли обработчики `seekforward` и `seekbackward`, чтобы вместо скраббера хотя бы работали шаги.

Результат:

- iOS заменял кнопки next/prev на `+10/-10`.
- Это ломало более важный сценарий переключения треков.

Вывод: в embedded-режиме эти handlers держать нельзя, пока next/prev важнее перемотки.

### 3. Скрытый real audio driver

Добавляли отдельный `<audio>` как настоящий audio owner, а stock video Lampa пытались держать как UI-оболочку.

Результат:

- Скраббер становился активнее.
- Появились гонки двух media-элементов: stock video и hidden audio спорили за play/pause/currentTime/ended.
- На переключениях иногда играло несколько треков или терялось состояние.

Вывод: два независимых media owner в одном плеере не подходят. Это не локальный фикс, а архитектурный конфликт.

### 4. Muted/audio shim

Пробовали держать audio shim рядом со stock video, в том числе muted и audible-варианты, синхронизировать timeupdate/seeking/seeked обратно в video.

Результат:

- Иногда lock screen начинал показывать скраббер.
- Muted shim не давал стабильного seek.
- Audible shim мог забирать аудиосессию, но тогда stock video нужно было глушить, а при сбое shim появлялся риск тишины.
- Обратная синхронизация seek ломала встроенный плеер: перемотка то работала, то зависала, то возвращалась назад.

Вывод: shim улучшал видимость controls, но не давал стабильной модели владения временем.

### 5. Hidden audio lead while locked

Пробовали делать hidden audio главным только на время блокировки, а при возврате отдавать управление stock video.

Результат:

- Поведение стало нестабильным и в плеере, и на lock screen.
- Переходы foreground/background создавали новые гонки.

Вывод: переключать owner в зависимости от visibility нельзя без глубокой переделки плеера.

## Что реально помогло

- Регистрировать только `play`, `pause`, `previoustrack`, `nexttrack`.
- Не регистрировать `seekforward`/`seekbackward`, иначе iOS показывает шаги `+10/-10` вместо next/prev.
- Keep-alive оставлять отдельно от seek: он помогает удерживать аудиосессию, но не решает ownership скраббера.
- Если когда-нибудь возвращаться к seek, нужен один главный media owner, а не stock video плюс hidden audio.

## Текущее решение

Экспериментальная перемотка встроенного iOS-плеера удалена из production-кода. В embedded lock-screen режиме остаются:

- play/pause;
- next/prev;
- metadata/artwork;
- без `seekto` handler и без embedded `setPositionState`, чтобы iOS не показывал полумёртвый скраббер.

Это лучше текущего состояния с нестабильным seek, потому что не ломает звук и не создаёт несколько одновременно играющих треков.

## Как правильно вернуться к задаче

Нужен не патч поверх `plugin.js`, а один из двух вариантов:

1. Сделать музыку во встроенном режиме настоящим `<audio>`-плеером на уровне создания `Lampa.PlayerVideo`, чтобы у iOS был один media owner.
2. Вынести music playback в отдельный player shell, где UI Lampa управляет тем же `<audio>`, который видит Media Session.

После этого можно вернуть:

- `seekto` handler;
- `setPositionState`;
- lock-screen scrubber;
- синхронизацию прогресса без hidden shim.

До такой переделки перемотку на lock screen лучше считать неподдержанной для embedded-плеера.
