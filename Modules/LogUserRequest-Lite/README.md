## Установка

1. Скачайте архив с модулем  
2. Распакуйте папку `LogUserRequest-Lite` в `/lampac/mods/`
3. Отредактируйте init.conf
4. Разрешите доступ к странице админки LogUserRequest-Lite:  
  "accsdb": {  
    "enable": true,  
    "whitepattern": "^/lite/logrequest"  
  },
5. Перезапустите Lampac
6. Админка: http://ваш_сервер/lite/logrequest  

## Настройка модуля:  
1. Конфигурация в manifest.json:  

{  
  "enable": true,  
  "version": 4,  
  "namespace": "LogUserRequest",  
  "logDay": 90  
}  
| Параметр	 | Описание | Значение по умолчанию |
|---|---|---|
| enable | Включить модуль | true |
| logDay | Сколько дней хранить логи | 90 |

Пароль администратора:  
1. Переменная окружения (LOGUSER_ADMIN_PASSWORD=my-super-super-secret-key)  
2. Пароль генерируется автоматически при первом запуске и сохраняется в файл: /lampac/database/LogUserRequest/passlogreg  
(Посмотреть пароль: docker exec lampac cat /lampac/database/LogUserRequest/passlogreg)

Что произойдёт при первом запуске  

| Ситуация | Результат |
|---|---|
| ENV задан | Используется ENV, файл не создаётся |
| ENV пуст, файла нет | Создаётся /lampac/database/LogUserRequest/passlogreg со случайным паролем (36 символов) |
| ENV пуст, файл есть | Используется пароль из файла |

⚠️ Важно:  
Если переменная окружения задана - она имеет наивысший приоритет, и остальные источники игнорируются.  

Блокировка при входе в LogUserRequest  
Как работает блокировка:  

| Параметр | Значение |
|---|---|
| Максимум попыток | 5 неверных паролей в день |
| Блокировка | После 5 неудачных попыток |
| Сброс счётчика | В полночь (по UTC) |
| Блокируется | Только IP нарушителя (остальные пользователи не страдают) |

Что делать при блокировке  
1. Подождать до полуночи  
Счётчик сбрасывается автоматически в 00:00 UTC. (в коде `DateTime.Today.AddDays(1)` использует локальное время сервера, убедитесь что часовой пояс UTC)  
2. Сменить IP (если нужно срочно)  
Переподключиться к VPN  
Использовать другой интернет  
Перезагрузить роутер  

3. Проверить правильность пароля  
docker exec lampac cat /lampac/database/LogUserRequest/passlogreg  

4. Сбросить блокировку вручную (требуется root доступ)  
Перезапустить контейнер (сбросит кэш попыток)  
docker restart lampac  

Сброс счётчика в полночь:  
Изменения можно внести в Controllers/ApiController.cs  
// В коде используется  
AbsoluteExpiration = DateTime.Today.AddDays(1)  

## Инструкция по настройке фильтрации в Lite-версии  
📁 Где находятся фильтры  
Все фильтры находятся в файле LogUserRequestMiddleware.cs в самом начале класса.  

### 🔧 Способы фильтрации  
1. Чёрный список путей (_blacklistPaths)  

private static readonly HashSet<string> _blacklistPaths = new(StringComparer.OrdinalIgnoreCase)  
{  
    "/.well-known", "/admin/health", "/admin/ping", "/testaccsdb", "/nws",  
    "/lifeevents", "/proxyimg", "/lite/logrequest",   
    "/lite/events", "/nexthub?plugin", "/externalids",  
    "/lampa-main", "/lite/withsearch", "/lite/mirage/trans/master.m3u8",  
    "/timecode", "/bookmark", "/storage", "/sisi/bookmarks?box_mac"  
};  

Как добавить новый путь:  

private static readonly HashSet<string> _blacklistPaths = new(StringComparer.OrdinalIgnoreCase)  
{  
    // ... существующие ...  
    "/ваш_путь", // Точное совпадение  
    "/ваш_префикс", // Будет блокировать всё что начинается с этого  
    "/путь?параметр" // Блокирует путь с параметром  
};  

2. Чёрный список параметров (_blacklistParams)

private static readonly HashSet<string> _blacklistParams = new(StringComparer.OrdinalIgnoreCase)  
{  
    "box_mac"  
};  

Как добавить новый параметр:  

private static readonly HashSet<string> _blacklistParams = new(StringComparer.OrdinalIgnoreCase)  
{  
    "box_mac",  
    "ваш_параметр"   // Блокирует любой URL с ?параметр= или &параметр=  
};  

3. Быстрый отсев в InvokeAsync  

if (path == "/" || path == "/favicon.ico" ||  
    path.EndsWith(".js") || path.EndsWith(".css") || path.EndsWith(".svg") ||   
    path.EndsWith(".png") || path.EndsWith(".jpg") || path.EndsWith(".woff") ||   
    path.EndsWith(".woff2") || path.EndsWith(".ogg") ||  
    path.StartsWith("/proxy/"))  
{  
    return;  
}  

Как добавить:  

if (path == "/" || path == "/favicon.ico" ||  
    path.StartsWith("/ваш_префикс/") ||   // ← Добавить  
    path.EndsWith(".js") || ...)  
{  
    return;  
}  

📋 Примеры фильтрации  

| Что хотите заблокировать | Куда добавить | Код |
|---|---|---|
| Конкретный путь /api/test | _blacklistPaths | "/api/test" |
| Все пути с префиксом /api/ | _blacklistPaths | "/api" |
| Путь с параметром /video?source= | _blacklistPaths | "/video?source" |
| Любой URL с ?token= | _blacklistParams | "token" |
| Все .php файлы | Быстрый отсев | path.EndsWith(".php") |

⚡ Оптимизация  
Модуль использует кэш _skipPathCache — результат проверки пути сохраняется на 10 минут. Это ускоряет повторные запросы к тому же пути.  

⚠️ Важные замечания  
После изменения файла нужна перекомпиляция модуля  
Синтаксис C# чувствителен к регистру и запятым  
Не удаляйте существующие фильтры без необходимости  
При ошибке в синтаксисе модуль не загрузится  

## База данных  
Путь: /lampac/database/LogUserRequest/userlog.db  

Очистка старых записей  
В Lite-версии реализована **очистка по дням** через параметр logDay в manifest.json.  
{  
  "logDay": 90  
}  

Как работает:  
Записи старше logDay дней удаляются раз в сутки  
Проверка запускается при старте модуля и затем каждые 24 часа  
Код в ModInit.cs:  

static void ClearJurnal(object? state)  
{  
    using var sqlDb = new AppDbContext();  
    var cutoff = DateTime.UtcNow.AddDays(-conf.logDay);  
    var deleted = sqlDb.jurnal.Where(j => j.time < cutoff).ExecuteDelete();  
    // ... также удаляются неиспользуемые unfo и headers  
}  

Рекомендуемые значения  

| Сценарий | logDay: |
|---|---|
| Домашнее использование (1-5 пользователей) | 90 дней |
| Небольшой сервер (5-20 пользователей) | 60 дней |
| Крупный сервер (20+ пользователей) | 30 дней |

Оптимизация SQLite (уже настроено)  
В модуле применены:  
WAL — ускорение записи  
cache_size = -64000 — кэш 64 МБ  
temp_store = MEMORY — временные таблицы в памяти  
mmap_size = 33554432 — memory-mapped I/O (32 МБ)  

### Ручная очистка и редактирование базы данных  
Для редактирования базы данных используйте **DB Browser for SQLite** — бесплатный визуальный редактор.  

🔗 Скачать: [https://sqlitebrowser.org/](https://sqlitebrowser.org/)  

Пошаговая инструкция  

1. **Скопируйте** файл `userlog.db` из папки `lampac-docker/database/LogUserRequest/` в любое удобное место (это будет ваш бэкап)  

2. **Отредактируйте копию** в DB Browser for SQLite:  
   - Откройте скопированный файл  
   - Перейдите на вкладку **"Execute SQL"**  
   - Вставьте и выполните запросы:  

     DELETE FROM jurnal WHERE time < datetime('now', '-30 days');  
     VACUUM;  
	 
Сохраните изменения (Ctrl+S)  

Замените оригинал — скопируйте отредактированный файл обратно в lampac-docker/database/LogUserRequest/ с заменой  
Перезапустите контейнер:  

docker restart lampac  

✅ Готово! База очищена, старые записи удалены.  

📋 Альтернативные запросы  

| Задача | SQL-запрос |
|---|---|
| Удалить старше 7 дней | DELETE FROM jurnal WHERE time < datetime('now', '-7 days'); |
| Удалить старше 90 дней | DELETE FROM jurnal WHERE time < datetime('now', '-90 days'); |
| Удалить ВСЕ записи | DELETE FROM jurnal; |
| Посмотреть количество записей | SELECT COUNT(*) FROM jurnal; |

## 🕐 Время записей не совпадает с местным  
По умолчанию модуль сохраняет время запросов в **UTC**. Если в админке время отличается от вашего локального — это нормально, но можно настроить.  

Варианты решения  
1. Сменить часовой пояс контейнера (рекомендуется)  
Добавьте переменную окружения TZ при запуске контейнера:  
docker run -e TZ=Europe/…  
docker run -e TZ=Asia/…  
Или в docker-compose.yml:  
environment:  
    - TZ=Europe/….  
После изменения — перезапустите контейнер.  
2. Если TZ не поддерживается нужно изменить код модуля (⚠️Не рекомендуется)  
Если нужно хранить локальное время вместо UTC, измените во всех файлах модуля   
// Было  
DateTime.UtcNow  
DateTime.UtcNow.AddDays(-X)  
DateTime.UtcNow.Date  
// Стало  
DateTime.Now  
DateTime.Now.AddDays(-X)  
DateTime.Now.Date  

Не заменяйте:  
DateTime.Now.Ticks  
В именах файлов экспорта ($"export_{DateTime.Now:yyyyMMdd}.zip")  

### ⚠️ Важно: последствия перехода на локальное время
| Проблема | Описание |
|---|---|
| **Переход на летнее/зимнее время** | Дважды в год время сдвигается на час. Записи могут дублироваться или нарушится сортировка |
| **Смена часового пояса сервера** | При переезде на другой сервер все исторические данные будут показывать неверное время |
| **Сравнение логов с разных серверов** | Если у вас несколько серверов в разных часовых поясах — логи невозможно сопоставить |
| **Очистка по `logDay`** | Может удалить на час больше или меньше записей при переходе времени |

#### 💡 Рекомендация: Оставьте UTC. Браузер сам покажет локальное время. Это стандарт для логирования.

## ❓ Частые проблемы

### Модуль не загружается
- Проверьте синтаксис в `manifest.json` (запятые, кавычки)
- Посмотрите логи: `docker logs lampac | grep LogUserRequest`

### Не открывается админка (404)
- Убедитесь, что путь `/lite/logrequest` не заблокирован в чёрном списке
- Проверьте, что модуль включён: `"enable": true`

### Пароль не подходит
- ENV переменная имеет приоритет: `docker exec lampac env | grep LOGUSER`
- Проверьте файл: `docker exec lampac cat /lampac/database/LogUserRequest/passlogreg`

### База данных растёт слишком быстро
- Уменьшите `logDay` до 30
- Добавьте в чёрный список часто вызываемые служебные пути

### После входа в админку белый экран
- Проверьте консоль браузера (F12) на ошибки
- Убедитесь, что файлы `index.html` и `auth.html` не повреждены
- Очистите кэш браузера

### Не работает кнопка "Premium"
- Это не баг 😄 — кнопка открывает пасхалку и информацию о Premium-версии
- Lite-версия полностью бесплатна и функциональна

💡 **Premium-версия** добавляет графики, экспорт, Telegram-уведомления и гибкие фильтры через UI. 
Подробнее — в модальном окне по кнопке "Premium" в админке или у разработчика.

## 📞 Поддержка

По вопросам работы модуля, предложениям и багам:

- Telegram: [@Viacheslav_Sh1](https://t.me/Viacheslav_Sh1)

---

**LogUserRequest Lite** — бесплатный модуль для логирования запросов в Lampac NG.
