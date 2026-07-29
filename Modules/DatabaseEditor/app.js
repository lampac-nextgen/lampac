(function () {
  'use strict';

  var categoryLabels = {
    book: 'Закладки', like: 'Нравится', wath: 'Позже', history: 'История просмотров',
    look: 'Смотрю', viewed: 'Просмотрено', scheduled: 'Запланировано',
    continued: 'Продолжение следует', thrown: 'Брошено'
  };
  var state = {
    database: 'timecode', page: 1, pageSize: 25, pages: 1, total: 0, query: '', selectedUser: '', loading: false,
    editingId: 0, editingDatabase: 'timecode', syncUser: null, syncItems: [], syncCategories: [], syncStatuses: [],
    syncPage: 1, syncPageSize: 48, syncQuery: '', syncCategory: '', syncItem: null
  };
  var $ = function (id) { return document.getElementById(id); };
  var els = {
    listView: $('listView'), summary: $('summary'), loading: $('loading'), head: $('tableHead'), body: $('tableBody'),
    empty: $('empty'), pagerInfo: $('pagerInfo'), pageLabel: $('pageLabel'), prev: $('prevBtn'), next: $('nextBtn'),
    search: $('searchInput'), userFilter: $('timecodeUserFilter'), pageSize: $('pageSize'), recordModal: $('recordModal'), recordTitle: $('recordModalTitle'),
    recordId: $('recordModalId'), user: $('userField'), card: $('cardField'), item: $('itemField'), data: $('dataField'),
    jsonStatus: $('jsonStatus'), saveRecord: $('saveRecordBtn'), deleteRecord: $('deleteRecordBtn'), toasts: $('toasts'),
    syncDetail: $('syncDetail'), syncUserName: $('syncUserName'), syncUserMeta: $('syncUserMeta'), syncSearch: $('syncSearch'),
    syncCategory: $('syncCategory'), syncResultCount: $('syncResultCount'), syncGrid: $('syncGrid'), syncEmpty: $('syncEmpty'),
    syncPrev: $('syncPrev'), syncNext: $('syncNext'), syncPageLabel: $('syncPageLabel'), categoryModal: $('categoryModal'),
    categoryHead: $('categoryCardHead'), categoryOptions: $('categoryOptions'), backupModal: $('backupModal'),
    backupResults: $('backupResults'), renameUserModal: $('renameUserModal'), oldUser: $('oldUserField'), newUser: $('newUserField'),
    confirmRenameUser: $('confirmRenameUserBtn'), deleteUserModal: $('deleteUserModal'), deleteUser: $('deleteUserField'),
    confirmDeleteUser: $('confirmDeleteUserBtn'), restoreModal: $('restoreModal'), restoreResults: $('restoreResults'), restoreEmpty: $('restoreEmpty')
  };
  var errorMessages = {
    unauthorized: 'Требуется повторный вход', unknown_database: 'Неизвестная база', database_not_found: 'Файл базы не найден',
    database_busy: 'База занята, повторите попытку', invalid_json: 'JSON содержит ошибку',
    data_must_be_json_object: 'В корне JSON должен быть объект', data_required: 'JSON не может быть пустым',
    data_too_large: 'JSON превышает 8 МБ', user_required: 'Укажите пользователя', card_required: 'Укажите карточку',
    item_required: 'Укажите элемент', duplicate_record_key: 'Запись с таким уникальным ключом уже существует',
    record_not_found: 'Запись уже удалена', card_not_found: 'Карточка не найдена', unknown_category: 'Неизвестная категория',
    multiple_statuses: 'Для карточки можно выбрать только один статус', old_user_required: 'Укажите текущее имя пользователя',
    new_user_required: 'Укажите новое имя пользователя', user_name_unchanged: 'Новое имя совпадает с текущим',
    user_not_found: 'Пользователь не найден ни в Sync, ни в TimeCode', rename_user_conflict: 'Новое имя уже занято или создаёт конфликт записей',
    invalid_backup_file: 'Недопустимое имя файла резервной копии', backup_not_found: 'Резервная копия не найдена',
    backup_integrity_failed: 'Проверка целостности резервной копии не пройдена', backup_schema_mismatch: 'Копия относится к другой базе',
    invalid_backup_database: 'Файл не является исправной SQLite-базой',
    internal_error: 'Внутренняя ошибка сервера', invalid_response: 'Сервер вернул неожиданный ответ'
  };

  function api(path, options) {
    options = options || {};
    options.credentials = 'same-origin';
    options.headers = Object.assign({ Accept: 'application/json' }, options.headers || {});
    if (options.body) {
      options.headers['Content-Type'] = 'application/json';
      options.headers['X-Database-Editor'] = '1';
    }
    return fetch('/database-editor/api/' + path, options).then(function (response) {
      var type = response.headers.get('content-type') || '';
      if (response.redirected && response.url.indexOf('/weblog/auth') >= 0) {
        location.href = response.url;
        throw new Error('unauthorized');
      }
      if (type.indexOf('application/json') < 0) throw new Error(response.status === 401 ? 'unauthorized' : 'invalid_response');
      return response.json().then(function (data) {
        if (!response.ok || data.success === false) throw new Error(data.error || 'request_failed');
        return data;
      });
    });
  }

  function el(tag, className, text) {
    var node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined && text !== null) node.textContent = text;
    return node;
  }
  function toast(message, bad) {
    var node = el('div', 'toast ' + (bad ? 'bad' : 'good'), message);
    els.toasts.appendChild(node);
    setTimeout(function () { node.remove(); }, 4500);
  }
  function showError(error) {
    var key = error && error.message || 'internal_error';
    toast(errorMessages[key] || key, true);
  }
  function setLoading(value) {
    state.loading = value;
    els.loading.classList.toggle('active', value);
    els.prev.disabled = value || state.page <= 1;
    els.next.disabled = value || state.page >= state.pages;
  }
  function formatBytes(bytes) {
    if (!bytes) return '0 Б';
    var units = ['Б', 'КБ', 'МБ', 'ГБ'];
    var index = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), 3);
    return (bytes / Math.pow(1024, index)).toLocaleString('ru-RU', { maximumFractionDigits: index ? 1 : 0 }) + ' ' + units[index];
  }
  function formatDate(value) {
    if (!value) return '—';
    var normalized = String(value).trim();
    var sqliteDateWithoutZone = /^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?$/;
    if (sqliteDateWithoutZone.test(normalized)) {
      normalized = normalized.replace(' ', 'T').replace(/(\.\d{3})\d+$/, '$1') + 'Z';
    }
    var date = new Date(normalized);
    return isNaN(date.getTime()) ? value : date.toLocaleString('ru-RU');
  }
  function formatTime(seconds) {
    seconds = Math.max(0, Math.round(Number(seconds) || 0));
    var hours = Math.floor(seconds / 3600);
    var minutes = Math.floor((seconds % 3600) / 60);
    var rest = seconds % 60;
    return (hours ? hours + ':' + String(minutes).padStart(2, '0') : minutes) + ':' + String(rest).padStart(2, '0');
  }
  function posterUrl(value) {
    if (!value) return '';
    if (/^https?:\/\//i.test(value)) return value;
    return '/tmdb/img/t/p/w300' + (value.charAt(0) === '/' ? value : '/' + value);
  }
  function posterNode(value, title) {
    if (!value) return el('div', 'poster-placeholder', '◇');
    var image = el('img', 'poster');
    image.loading = 'lazy';
    image.alt = title || '';
    image.src = posterUrl(value);
    image.addEventListener('error', function () { image.replaceWith(el('div', 'poster-placeholder', '◇')); }, { once: true });
    return image;
  }
  function typeLabel(type) { return type === 'tv' ? 'Сериал' : type === 'movie' ? 'Фильм' : ''; }
  function fallbackTitle(record) {
    if (record.title) return record.title;
    return typeLabel(record.mediaType) || 'Карточка';
  }

  function loadSummary() {
    return api('summary').then(function (data) {
      els.summary.textContent = '';
      (data.databases || []).forEach(function (db) {
        var card = el('article', 'summary-card');
        var head = el('div', 'summary-head');
        head.appendChild(el('span', '', db.title));
        head.appendChild(el('span', db.available ? 'available' : 'missing', db.available ? 'доступна' : 'нет файла'));
        card.appendChild(head);
        card.appendChild(el('div', 'summary-value', Number(db.records || 0).toLocaleString('ru-RU') + ' записей'));
        var foot = el('div', 'summary-foot');
        foot.appendChild(el('div', 'summary-meta', formatBytes(db.bytes) + ' · ' + db.file + ' · ' + formatDate(db.updated)));
        var backup = el('button', 'summary-backup', 'Backup этой базы');
        backup.addEventListener('click', function () { createBackup(db.database); });
        foot.appendChild(backup);
        card.appendChild(foot);
        els.summary.appendChild(card);
      });
    }).catch(showError);
  }

  function renderHead() {
    els.head.textContent = '';
    var table = els.head.closest('table');
    table.classList.toggle('timecode-table', state.database === 'timecode');
    table.classList.toggle('sync-table', state.database === 'sync');
    var columns = state.database === 'timecode'
      ? [['ID', 'col-id'], ['Пользователь', 'col-user'], ['Карточка', 'col-media'], ['Позиция', 'col-progress'], ['Обновлено', 'col-date'], ['', 'col-actions']]
      : [['ID', 'col-id'], ['Пользователь', 'col-user'], ['Обновлено', 'col-date'], ['', 'col-actions']];
    columns.forEach(function (column) { els.head.appendChild(el('th', column[1], column[0])); });
  }

  function renderRows(records) {
    els.body.textContent = '';
    els.empty.classList.toggle('show', !records.length);
    records.forEach(function (record) {
      var row = document.createElement('tr');
      row.appendChild(el('td', 'mono', '#' + record.id));
      var userCell = el('td');
      userCell.appendChild(el('div', 'record-user', record.user || '—'));
      userCell.appendChild(el('div', 'record-sub', formatBytes(record.dataLength || 0)));
      row.appendChild(userCell);

      if (state.database === 'timecode') {
        var mediaCell = el('td');
        var media = el('div', 'media-cell');
        var title = fallbackTitle(record);
        media.appendChild(posterNode(record.poster, title));
        var copy = el('div', 'media-copy');
        copy.appendChild(el('div', 'media-title', title));
        var meta = el('div', 'media-meta');
        if (record.mediaType) meta.appendChild(el('span', 'type-badge', typeLabel(record.mediaType)));
        var mediaDetails = [];
        if (record.mediaType === 'tv' && record.season != null && record.episode != null) {
          mediaDetails.push('Сезон ' + record.season, 'Серия ' + record.episode);
        }
        if (record.year) mediaDetails.push(record.year);
        if (mediaDetails.length) meta.appendChild(document.createTextNode(mediaDetails.join(' · ')));
        copy.appendChild(meta);
        media.appendChild(copy);
        mediaCell.appendChild(media);
        row.appendChild(mediaCell);

        var progressCell = el('td');
        var percent = Number(record.percent);
        if (!isFinite(percent) && Number(record.duration) > 0) percent = Number(record.position) / Number(record.duration) * 100;
        percent = Math.max(0, Math.min(100, isFinite(percent) ? percent : 0));
        progressCell.appendChild(el('div', 'record-user', Math.round(percent) + '%'));
        var track = el('div', 'progress-track');
        var fill = el('div', 'progress-fill');
        fill.style.width = percent + '%';
        track.appendChild(fill);
        progressCell.appendChild(track);
        var progressCopy = el('div', 'progress-copy');
        progressCopy.appendChild(el('span', '', formatTime(record.position)));
        progressCopy.appendChild(el('span', '', formatTime(record.duration)));
        progressCell.appendChild(progressCopy);
        row.appendChild(progressCell);
      }

      row.appendChild(el('td', '', formatDate(record.updated)));
      var actions = el('td', 'row-actions');
      if (state.database === 'sync') {
        var open = el('button', 'mini-btn', 'Открыть');
        open.addEventListener('click', function () { openSyncUser(record.id); });
        actions.appendChild(open);
      }
      var edit = el('button', 'mini-btn', 'JSON');
      edit.addEventListener('click', function () { openRecord(record.id, state.database); });
      var remove = el('button', 'mini-btn delete', '×');
      remove.title = 'Удалить всю запись';
      remove.addEventListener('click', function () { deleteRecord(record.id, state.database); });
      actions.appendChild(edit);
      actions.appendChild(remove);
      row.appendChild(actions);
      els.body.appendChild(row);
    });
  }

  function loadRecords() {
    if (state.loading) return;
    setLoading(true);
    var params = new URLSearchParams({ database: state.database, page: String(state.page), pageSize: String(state.pageSize) });
    if (state.query) params.set('query', state.query);
    if (state.database === 'timecode' && state.selectedUser) params.set('user', state.selectedUser);
    api('records?' + params.toString()).then(function (data) {
      state.page = Number(data.page || 1); state.pages = Number(data.pages || 1); state.total = Number(data.total || 0);
      renderHead(); renderRows(data.records || []);
      var from = state.total ? (state.page - 1) * state.pageSize + 1 : 0;
      var to = Math.min(state.page * state.pageSize, state.total);
      els.pagerInfo.textContent = from + '–' + to + ' из ' + state.total.toLocaleString('ru-RU');
      els.pageLabel.textContent = state.page + ' / ' + state.pages;
    }).catch(showError).finally(function () { setLoading(false); });
  }

  function loadTimeCodeUsers() {
    return api('users?database=timecode').then(function (data) {
      var selected = state.selectedUser;
      els.userFilter.textContent = '';
      var all = document.createElement('option');
      all.value = '';
      all.textContent = 'Все пользователи TimeCode (' + (data.users || []).length.toLocaleString('ru-RU') + ')';
      els.userFilter.appendChild(all);
      (data.users || []).forEach(function (entry) {
        var option = document.createElement('option');
        option.value = entry.user;
        option.textContent = entry.user + ' (' + Number(entry.records || 0).toLocaleString('ru-RU') + ')';
        els.userFilter.appendChild(option);
      });
      if (selected && Array.from(els.userFilter.options).some(function (option) { return option.value === selected; })) {
        els.userFilter.value = selected;
      } else {
        state.selectedUser = '';
        if (selected && state.database === 'timecode') { state.page = 1; loadRecords(); }
      }
    }).catch(showError);
  }

  function switchDatabase(database) {
    if (database === state.database) return;
    state.database = database; state.page = 1;
    document.querySelectorAll('.db-tab').forEach(function (tab) { tab.classList.toggle('active', tab.dataset.db === database); });
    els.userFilter.hidden = database !== 'timecode';
    loadRecords();
  }

  function openSyncUser(id, preserveFilters) {
    var previousQuery = preserveFilters ? state.syncQuery : '';
    var previousCategory = preserveFilters ? state.syncCategory : '';
    api('sync-user?id=' + encodeURIComponent(id)).then(function (data) {
      state.syncUser = data.user;
      state.syncItems = data.user.items || [];
      state.syncCategories = data.categories || [];
      state.syncStatuses = data.statuses || [];
      state.syncPage = 1; state.syncQuery = previousQuery; state.syncCategory = previousCategory;
      els.syncSearch.value = previousQuery;
      buildCategorySelect();
      if (Array.from(els.syncCategory.options).some(function (option) { return option.value === previousCategory; })) {
        els.syncCategory.value = previousCategory;
      } else {
        state.syncCategory = '';
      }
      els.syncUserName.textContent = data.user.user || '—';
      els.syncUserMeta.textContent = Number(data.user.total || 0).toLocaleString('ru-RU') + ' карточек · обновлено ' + formatDate(data.user.updated);
      els.listView.hidden = true;
      els.syncDetail.hidden = false;
      renderSyncItems();
      window.scrollTo(0, 0);
    }).catch(showError);
  }

  function buildCategorySelect() {
    els.syncCategory.textContent = '';
    var all = document.createElement('option'); all.value = ''; all.textContent = 'Все категории'; els.syncCategory.appendChild(all);
    var generalGroup = document.createElement('optgroup'); generalGroup.label = 'Категории';
    var statusGroup = document.createElement('optgroup'); statusGroup.label = 'Статус';
    state.syncCategories.forEach(function (category) {
      var option = document.createElement('option'); option.value = category; option.textContent = categoryLabels[category] || category;
      (state.syncStatuses.indexOf(category) >= 0 ? statusGroup : generalGroup).appendChild(option);
    });
    els.syncCategory.appendChild(generalGroup);
    els.syncCategory.appendChild(statusGroup);
  }

  function syncItemOrder(item, category) {
    var value = item.categoryOrder && item.categoryOrder[category];
    return value == null ? Number.MAX_SAFE_INTEGER : Number(value);
  }

  function filteredSyncItems() {
    var query = state.syncQuery.toLowerCase();
    var filtered = state.syncItems.filter(function (item) {
      var matchesText = !query || String(item.title || '').toLowerCase().indexOf(query) >= 0 || String(item.cardId || '').toLowerCase().indexOf(query) >= 0;
      var matchesCategory = !state.syncCategory || (item.categories || []).indexOf(state.syncCategory) >= 0;
      return matchesText && matchesCategory;
    });
    return filtered.sort(function (left, right) {
      if (state.syncCategory) return syncItemOrder(left, state.syncCategory) - syncItemOrder(right, state.syncCategory);
      var leftHistory = syncItemOrder(left, 'history');
      var rightHistory = syncItemOrder(right, 'history');
      if (leftHistory !== rightHistory) return leftHistory - rightHistory;
      return Number(left.order || 0) - Number(right.order || 0);
    });
  }

  function renderSyncItems() {
    var filtered = filteredSyncItems();
    var pages = Math.max(1, Math.ceil(filtered.length / state.syncPageSize));
    state.syncPage = Math.min(state.syncPage, pages);
    var start = (state.syncPage - 1) * state.syncPageSize;
    var visible = filtered.slice(start, start + state.syncPageSize);
    els.syncGrid.textContent = '';
    els.syncEmpty.classList.toggle('show', !visible.length);
    els.syncResultCount.textContent = filtered.length.toLocaleString('ru-RU') + ' из ' + state.syncItems.length.toLocaleString('ru-RU');
    els.syncPageLabel.textContent = state.syncPage + ' / ' + pages;
    els.syncPrev.disabled = state.syncPage <= 1;
    els.syncNext.disabled = state.syncPage >= pages;

    visible.forEach(function (item) {
      var card = el('article', 'sync-card');
      card.appendChild(posterNode(item.poster, item.title));
      var body = el('div', 'sync-card-body');
      body.appendChild(el('div', 'sync-card-title', item.title || 'Карточка #' + item.cardId));
      body.appendChild(el('div', 'sync-card-meta', [typeLabel(item.mediaType), item.year, '#' + item.cardId].filter(Boolean).join(' · ')));
      var tags = el('div', 'category-tags');
      if (item.categories && item.categories.length) item.categories.forEach(function (category) { tags.appendChild(el('span', 'category-tag', categoryLabels[category] || category)); });
      else tags.appendChild(el('span', 'category-tag empty-tag', 'Без категории'));
      body.appendChild(tags);
      var actions = el('div', 'sync-card-actions');
      var edit = el('button', 'btn', 'Изменить категории');
      edit.addEventListener('click', function () { openCategoryModal(item); });
      actions.appendChild(edit); body.appendChild(actions); card.appendChild(body); els.syncGrid.appendChild(card);
    });
  }

  function openCategoryModal(item) {
    state.syncItem = item;
    els.categoryHead.textContent = '';
    els.categoryHead.appendChild(posterNode(item.poster, item.title));
    var copy = el('div');
    copy.appendChild(el('div', 'media-title', item.title || 'Карточка #' + item.cardId));
    copy.appendChild(el('div', 'media-meta mono', '#' + item.cardId));
    els.categoryHead.appendChild(copy);
    els.categoryOptions.textContent = '';
    state.syncCategories.forEach(function (category, index) {
      var isStatus = state.syncStatuses.indexOf(category) >= 0;
      if (isStatus && category === state.syncStatuses[0]) {
        els.categoryOptions.appendChild(el('div', 'category-section-title', 'Статус'));
      }
      var wrap = el('div', 'category-option' + (isStatus ? ' status-option' : ''));
      var input = document.createElement('input');
      input.type = 'checkbox'; input.id = 'sync-category-' + index; input.value = category;
      if (isStatus) input.dataset.status = '1';
      input.checked = (item.categories || []).indexOf(category) >= 0;
      if (isStatus) input.addEventListener('change', function () {
        if (!input.checked) return;
        els.categoryOptions.querySelectorAll('input[data-status="1"]').forEach(function (other) {
          if (other !== input) other.checked = false;
        });
      });
      var label = document.createElement('label'); label.htmlFor = input.id; label.textContent = categoryLabels[category] || category;
      wrap.appendChild(input); wrap.appendChild(label); els.categoryOptions.appendChild(wrap);
    });
    openModal('categoryModal');
  }

  function saveSyncItem() {
    if (!state.syncUser || !state.syncItem) return;
    var categories = Array.from(els.categoryOptions.querySelectorAll('input:checked')).map(function (input) { return input.value; });
    $('saveSyncItemBtn').disabled = true;
    api('sync-item/save', { method: 'POST', body: JSON.stringify({ recordId: state.syncUser.id, cardId: state.syncItem.cardId, categories: categories }) })
      .then(function (data) {
        closeModal('categoryModal'); toast('Категории и статус карточки сохранены'); loadSummary();
        openSyncUser(state.syncUser.id, true);
      }).catch(showError).finally(function () { $('saveSyncItemBtn').disabled = false; });
  }

  function deleteSyncItem() {
    if (!state.syncUser || !state.syncItem) return;
    if (!confirm('Удалить карточку «' + (state.syncItem.title || state.syncItem.cardId) + '» у этого пользователя?')) return;
    api('sync-item/delete', { method: 'POST', body: JSON.stringify({ recordId: state.syncUser.id, cardId: state.syncItem.cardId }) })
      .then(function () {
        state.syncItems = state.syncItems.filter(function (item) { return item.cardId !== state.syncItem.cardId; });
        state.syncUser.total = state.syncItems.length;
        closeModal('categoryModal'); renderSyncItems(); toast('Карточка удалена'); loadSummary();
      }).catch(showError);
  }

  function prettyJson(value) { return JSON.stringify(JSON.parse(value), null, 2); }
  function validateJson() {
    try {
      var parsed = JSON.parse(els.data.value);
      if (!parsed || Array.isArray(parsed) || typeof parsed !== 'object') throw new Error('object');
      els.jsonStatus.textContent = 'JSON корректен'; els.jsonStatus.className = 'json-status ok'; return true;
    } catch (_) {
      els.jsonStatus.textContent = 'Ошибка JSON'; els.jsonStatus.className = 'json-status bad'; return false;
    }
  }
  function setRecordDatabase(database) {
    document.querySelectorAll('.timecode-field').forEach(function (node) { node.style.display = database === 'timecode' ? 'flex' : 'none'; });
  }
  function openRecord(id, database) {
    api('record?database=' + encodeURIComponent(database) + '&id=' + id).then(function (data) {
      var record = data.record; state.editingId = record.id; state.editingDatabase = database; setRecordDatabase(database);
      els.recordTitle.textContent = 'Редактирование ' + (database === 'sync' ? 'Sync' : 'TimeCode'); els.recordId.textContent = '#' + record.id;
      els.user.value = record.user || ''; els.card.value = record.card || ''; els.item.value = record.item || '';
      try { els.data.value = prettyJson(record.data || '{}'); } catch (_) { els.data.value = record.data || ''; }
      els.deleteRecord.hidden = false; validateJson(); openModal('recordModal');
    }).catch(showError);
  }
  function saveRecord() {
    if (!validateJson()) return;
    var payload = { database: state.editingDatabase, id: state.editingId || null, user: els.user.value, card: els.card.value, item: els.item.value, data: els.data.value };
    els.saveRecord.disabled = true;
    api('save', { method: 'POST', body: JSON.stringify(payload) }).then(function () {
      toast('Запись сохранена'); closeModal('recordModal'); state.page = 1; loadRecords(); loadSummary();
      if (state.editingDatabase === 'timecode') loadTimeCodeUsers();
      if (state.syncUser && state.editingDatabase === 'sync') openSyncUser(state.syncUser.id);
    }).catch(showError).finally(function () { els.saveRecord.disabled = false; });
  }
  function deleteRecord(id, database) {
    if (!confirm('Удалить всю запись #' + id + ' из ' + database + '?')) return;
    api('delete', { method: 'POST', body: JSON.stringify({ database: database, id: id }) }).then(function () {
      toast('Запись удалена'); closeModal('recordModal'); loadRecords(); loadSummary();
      if (database === 'timecode') loadTimeCodeUsers();
    }).catch(showError);
  }

  function createBackup(database) {
    $('backupAllBtn').disabled = true;
    api('backup', { method: 'POST', body: JSON.stringify({ database: database }) }).then(function (data) {
      var backups = data.backups || [{ database: data.database || database, path: data.path }];
      els.backupResults.textContent = '';
      backups.forEach(function (backup) {
        var result = el('div', 'backup-result');
        result.appendChild(el('strong', '', backup.database === 'timecode' ? 'TimeCode.sql' : 'Sync.sql'));
        result.appendChild(el('div', 'backup-path', backup.path));
        els.backupResults.appendChild(result);
      });
      openModal('backupModal'); toast(backups.length === 2 ? 'Созданы копии TimeCode и Sync' : 'Резервная копия создана');
    }).catch(showError).finally(function () { $('backupAllBtn').disabled = false; });
  }
  var restoreDatabase = 'sync';
  function openRestore() {
    restoreDatabase = 'sync';
    document.querySelectorAll('[data-restore-db]').forEach(function (tab) { tab.classList.toggle('active', tab.dataset.restoreDb === restoreDatabase); });
    openModal('restoreModal'); loadBackups();
  }
  function loadBackups() {
    els.restoreResults.textContent = ''; els.restoreEmpty.classList.remove('show');
    els.restoreResults.appendChild(el('div', 'muted', 'Загрузка…'));
    api('backups?database=' + encodeURIComponent(restoreDatabase)).then(function (data) {
      els.restoreResults.textContent = '';
      var backups = data.backups || [];
      els.restoreEmpty.classList.toggle('show', !backups.length);
      backups.forEach(function (backup) {
        var row = el('div', 'backup-result restore-result');
        var info = el('div', 'restore-info');
        info.appendChild(el('strong', '', backup.file));
        info.appendChild(el('div', 'backup-path', formatBytes(backup.bytes) + ' · ' + formatDate(backup.created)));
        var button = el('button', 'btn danger', 'Восстановить');
        button.addEventListener('click', function () { restoreBackup(backup, button); });
        row.appendChild(info); row.appendChild(button); els.restoreResults.appendChild(row);
      });
    }).catch(showError);
  }
  function restoreBackup(backup, button) {
    var title = backup.database === 'sync' ? 'Sync' : 'TimeCode';
    if (!confirm('Восстановить базу ' + title + ' из файла «' + backup.file + '»?\n\nТекущее состояние сначала будет сохранено автоматически. Данные, записанные после даты этой копии, будут заменены.')) return;
    button.disabled = true;
    api('restore', { method: 'POST', body: JSON.stringify({ database: backup.database, file: backup.file }) }).then(function (data) {
      closeModal('restoreModal');
      toast(title + ' восстановлена. Страховочная копия: ' + data.result.safetyBackup);
      setTimeout(function () { location.reload(); }, 1600);
    }).catch(showError).finally(function () { button.disabled = false; });
  }
  function openRenameUser() {
    var selected = state.syncUser ? state.syncUser.user || '' : state.selectedUser || '';
    els.oldUser.textContent = '';
    var loadingOption = document.createElement('option'); loadingOption.value = ''; loadingOption.textContent = 'Загрузка пользователей…';
    els.oldUser.appendChild(loadingOption);
    els.newUser.value = '';
    els.confirmRenameUser.disabled = true;
    openModal('renameUserModal');
    Promise.all([api('users?database=sync'), api('users?database=timecode')]).then(function (responses) {
      var users = new Map();
      responses.forEach(function (response, databaseIndex) {
        (response.users || []).forEach(function (entry) {
          var key = (entry.user || '').toLowerCase();
          if (!key) return;
          var current = users.get(key) || { user: entry.user, sync: 0, timecode: 0 };
          current[databaseIndex === 0 ? 'sync' : 'timecode'] += Number(entry.records || 0);
          users.set(key, current);
        });
      });
      var list = Array.from(users.values()).sort(function (left, right) { return left.user.localeCompare(right.user, 'ru', { sensitivity: 'base' }); });
      els.oldUser.textContent = '';
      var placeholder = document.createElement('option'); placeholder.value = ''; placeholder.textContent = list.length ? 'Выберите пользователя' : 'Пользователей нет';
      els.oldUser.appendChild(placeholder);
      list.forEach(function (entry) {
        var option = document.createElement('option'); option.value = entry.user;
        option.textContent = entry.user + ' · Sync: ' + entry.sync + ', TimeCode: ' + entry.timecode;
        els.oldUser.appendChild(option);
      });
      var match = list.find(function (entry) { return entry.user.toLowerCase() === selected.toLowerCase(); });
      if (match) els.oldUser.value = match.user;
      els.confirmRenameUser.disabled = !list.length;
      setTimeout(function () { (els.oldUser.value ? els.newUser : els.oldUser).focus(); }, 0);
    }).catch(function (error) {
      els.oldUser.textContent = '';
      var failed = document.createElement('option'); failed.value = ''; failed.textContent = 'Не удалось загрузить пользователей'; els.oldUser.appendChild(failed);
      showError(error);
    });
  }
  function openDeleteUser() {
    var selected = state.syncUser ? state.syncUser.user || '' : state.selectedUser || '';
    els.deleteUser.textContent = '';
    var loading = document.createElement('option'); loading.value = ''; loading.textContent = 'Загрузка пользователей…'; els.deleteUser.appendChild(loading);
    els.confirmDeleteUser.disabled = true;
    openModal('deleteUserModal');
    Promise.all([api('users?database=sync'), api('users?database=timecode')]).then(function (responses) {
      var users = new Map();
      responses.forEach(function (response, databaseIndex) {
        (response.users || []).forEach(function (entry) {
          var key = (entry.user || '').toLowerCase();
          if (!key) return;
          var current = users.get(key) || { user: entry.user, sync: 0, timecode: 0 };
          current[databaseIndex === 0 ? 'sync' : 'timecode'] += Number(entry.records || 0); users.set(key, current);
        });
      });
      var list = Array.from(users.values()).sort(function (left, right) { return left.user.localeCompare(right.user, 'ru', { sensitivity: 'base' }); });
      els.deleteUser.textContent = '';
      var placeholder = document.createElement('option'); placeholder.value = ''; placeholder.textContent = list.length ? 'Выберите пользователя' : 'Пользователей нет'; els.deleteUser.appendChild(placeholder);
      list.forEach(function (entry) { var option = document.createElement('option'); option.value = entry.user; option.textContent = entry.user + ' · Sync: ' + entry.sync + ', TimeCode: ' + entry.timecode; els.deleteUser.appendChild(option); });
      var match = list.find(function (entry) { return entry.user.toLowerCase() === selected.toLowerCase(); });
      if (match) els.deleteUser.value = match.user;
      els.confirmDeleteUser.disabled = !list.length;
      setTimeout(function () { els.deleteUser.focus(); }, 0);
    }).catch(function (error) {
      els.deleteUser.textContent = ''; var failed = document.createElement('option'); failed.value = ''; failed.textContent = 'Не удалось загрузить пользователей'; els.deleteUser.appendChild(failed); showError(error);
    });
  }
  function deleteUser() {
    var user = els.deleteUser.value.trim();
    if (!user) return showError(new Error('user_required'));
    if (!confirm('Безвозвратно удалить пользователя «' + user + '», все его закладки Sync и позиции TimeCode?')) return;
    els.confirmDeleteUser.disabled = true;
    api('delete-user', { method: 'POST', body: JSON.stringify({ user: user }) }).then(function (data) {
      var result = data.result; closeModal('deleteUserModal'); state.selectedUser = '';
      if (state.syncUser && state.syncUser.user.toLowerCase() === user.toLowerCase()) state.syncUser = null;
      els.syncDetail.hidden = true; els.listView.hidden = false;
      loadRecords(); loadSummary(); loadTimeCodeUsers();
      toast('Пользователь удалён: Sync — ' + result.syncRecords + ', TimeCode — ' + result.timecodeRecords);
    }).catch(showError).finally(function () { els.confirmDeleteUser.disabled = false; });
  }
  function renameUser() {
    var oldUser = els.oldUser.value.trim();
    var newUser = els.newUser.value.trim();
    if (!oldUser) return showError(new Error('old_user_required'));
    if (!newUser) return showError(new Error('new_user_required'));
    if (!confirm('Перед продолжением выйдите из учётной записи «' + oldUser + '» на всех устройствах или измените её логин/account_email в Lampa и init/AccsDB.\n\nАктивная сессия со старым именем создаст пользователя повторно.\n\nПереименовать «' + oldUser + '» в «' + newUser + '»?')) return;
    els.confirmRenameUser.disabled = true;
    api('rename-user', { method: 'POST', body: JSON.stringify({ oldUser: oldUser, newUser: newUser }) }).then(function (data) {
      var result = data.result;
      closeModal('renameUserModal');
      state.selectedUser = '';
      if (state.syncUser && state.syncUser.user.toLowerCase() === oldUser.toLowerCase()) state.syncUser = null;
      els.syncDetail.hidden = true; els.listView.hidden = false;
      loadRecords(); loadSummary(); loadTimeCodeUsers();
      toast('Пользователь переименован: Sync — ' + result.syncRecords + ', TimeCode — ' + result.timecodeRecords);
    }).catch(showError).finally(function () { els.confirmRenameUser.disabled = false; });
  }
  function openModal(id) { $(id).classList.add('open'); }
  function closeModal(id) { $(id).classList.remove('open'); }
  function applyTheme(theme) {
    document.documentElement.dataset.theme = theme;
    var metaTheme = document.getElementById('metaTheme');
    if (metaTheme) metaTheme.setAttribute('content', theme === 'dark' ? '#0c0e12' : '#f4f5f8');
    localStorage.setItem('lampac-theme', theme);
  }

  document.querySelectorAll('.db-tab').forEach(function (tab) { tab.addEventListener('click', function () { switchDatabase(tab.dataset.db); }); });
  var searchTimer;
  els.search.addEventListener('input', function () { clearTimeout(searchTimer); searchTimer = setTimeout(function () { state.query = els.search.value.trim(); state.page = 1; loadRecords(); }, 280); });
  els.userFilter.addEventListener('change', function () { state.selectedUser = els.userFilter.value; state.page = 1; loadRecords(); });
  els.pageSize.addEventListener('change', function () { state.pageSize = Number(els.pageSize.value); state.page = 1; loadRecords(); });
  els.prev.addEventListener('click', function () { if (state.page > 1) { state.page--; loadRecords(); } });
  els.next.addEventListener('click', function () { if (state.page < state.pages) { state.page++; loadRecords(); } });
  $('refreshBtn').addEventListener('click', function () { loadRecords(); loadSummary(); if (state.database === 'timecode') loadTimeCodeUsers(); });
  $('backupAllBtn').addEventListener('click', function () { createBackup('all'); });
  $('restoreBtn').addEventListener('click', openRestore);
  document.querySelectorAll('[data-restore-db]').forEach(function (tab) { tab.addEventListener('click', function () { restoreDatabase = tab.dataset.restoreDb; document.querySelectorAll('[data-restore-db]').forEach(function (item) { item.classList.toggle('active', item === tab); }); loadBackups(); }); });
  $('renameUserBtn').addEventListener('click', openRenameUser);
  els.confirmRenameUser.addEventListener('click', renameUser);
  $('deleteUserBtn').addEventListener('click', openDeleteUser);
  els.confirmDeleteUser.addEventListener('click', deleteUser);
  $('backToUsers').addEventListener('click', function () { els.syncDetail.hidden = true; els.listView.hidden = false; state.syncUser = null; loadRecords(); loadSummary(); });
  $('rawSyncBtn').addEventListener('click', function () { if (state.syncUser) openRecord(state.syncUser.id, 'sync'); });
  $('refreshSyncBtn').addEventListener('click', function () { if (state.syncUser) openSyncUser(state.syncUser.id, true); });
  els.syncSearch.addEventListener('input', function () { state.syncQuery = els.syncSearch.value.trim(); state.syncPage = 1; renderSyncItems(); });
  els.syncCategory.addEventListener('change', function () { state.syncCategory = els.syncCategory.value; state.syncPage = 1; renderSyncItems(); });
  els.syncPrev.addEventListener('click', function () { if (state.syncPage > 1) { state.syncPage--; renderSyncItems(); window.scrollTo(0, 0); } });
  els.syncNext.addEventListener('click', function () { state.syncPage++; renderSyncItems(); window.scrollTo(0, 0); });
  $('saveSyncItemBtn').addEventListener('click', saveSyncItem);
  $('deleteSyncItemBtn').addEventListener('click', deleteSyncItem);
  $('formatBtn').addEventListener('click', function () { try { els.data.value = prettyJson(els.data.value); } catch (_) { } validateJson(); });
  els.data.addEventListener('input', validateJson);
  els.saveRecord.addEventListener('click', saveRecord);
  els.deleteRecord.addEventListener('click', function () { deleteRecord(state.editingId, state.editingDatabase); });
  document.querySelectorAll('[data-close]').forEach(function (button) { button.addEventListener('click', function () { closeModal(button.dataset.close); }); });
  document.querySelectorAll('.modal-backdrop').forEach(function (modal) { modal.addEventListener('click', function (event) { if (event.target === modal) closeModal(modal.id); }); });
  document.addEventListener('keydown', function (event) {
    if (event.key === 'Escape') document.querySelectorAll('.modal-backdrop.open').forEach(function (modal) { closeModal(modal.id); });
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's' && els.recordModal.classList.contains('open')) { event.preventDefault(); saveRecord(); }
  });
  $('themeBtn').addEventListener('click', function () { applyTheme(document.documentElement.dataset.theme === 'light' ? 'dark' : 'light'); });
  applyTheme(localStorage.getItem('lampac-theme') || 'dark');
  loadSummary(); loadTimeCodeUsers(); renderHead(); loadRecords();
})();
