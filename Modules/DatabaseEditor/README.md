# DatabaseEditor

Защищённый root-паролем WebLog редактор баз `database/TimeCode.sql` и
`database/Sync.sql`.

## Маршруты

- `/database-editor` — интерфейс редактора.
- `/database-editor/api/summary` — состояние и размер баз.
- `/database-editor/api/records` — поиск и постраничный список.
- `/database-editor/api/record` — чтение одной записи.
- `/database-editor/api/sync-user` — карточки и категории одного Sync-пользователя.
- `/database-editor/api/sync-item/save` — точечное изменение категорий карточки Sync.
- `/database-editor/api/sync-item/delete` — удаление одной карточки из Sync.
- `/database-editor/api/save` — создание или изменение записи.
- `/database-editor/api/delete` — удаление записи.
- `/database-editor/api/rename-user` — атомарное переименование пользователя во всех строках `bookmarks` (Sync) и `timecodes` (TimeCode).
- `/database-editor/api/delete-user` — атомарное удаление пользователя и всех его строк из `bookmarks` (Sync) и `timecodes` (TimeCode).
- `/database-editor/api/backup` — online-backup одной базы или обеих баз при
  `database: "all"` в `database/backup/database-editor`.
- `/database-editor/api/backups` — список доступных резервных копий выбранной базы.
- `/database-editor/api/restore` — проверка и восстановление выбранной SQLite-копии; перед заменой автоматически создаётся страховочная копия текущей базы с маркером `before-restore`.

Изменяющие запросы требуют заголовок `X-Database-Editor: 1`. Все SQL-запросы
параметризованы; запись синхронизируется штатными семафорами `TimeCode` и
`Sync`. В корне поля `data` допускается только JSON-объект. Список TimeCode
дополняется названиями и постерами из карточек Sync того же пользователя.

Редактор Sync повторяет категории Lampa: `book` (Закладки), `like` (Нравится),
`wath` (Позже), `history` (История просмотров). Поле `wath` является актуальным
именем Lampa, несмотря на опечатку в английском ключе. Статусы `look` (Смотрю),
`viewed`, `scheduled`, `continued`, `thrown` взаимоисключающие. Служебное поле
`watch` сохраняется в поддерживаемой схеме Lampac, но в редакторе не выводится.

Мод также добавляет вкладку `Базы` в `/weblog` на лету, не изменяя файлы
штатного модуля WebLog.
