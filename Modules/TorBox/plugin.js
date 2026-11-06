(function () {
  'use strict';
  if (window.lampac_torbox_loaded) return;
  window.lampac_torbox_loaded = true;
  var base = '{localhost}';
  var script = document.currentScript && document.currentScript.src || '';
  var tokenMatch = script.match(/[?&]token=([^&]+)/);
  var token = tokenMatch ? decodeURIComponent(tokenMatch[1]) : '';
  var generation = 0;
  var busy = false;
  var originController = 'content';

  function endpoint(path) {
    var url = base + '/torbox/' + path;
    var email = Lampa.Storage.get('account_email', '');
    var uid = Lampa.Storage.get('lampac_unic_id', '');
    if (email) url = Lampa.Utils.addUrlComponent(url, 'account_email=' + encodeURIComponent(email));
    if (uid) url = Lampa.Utils.addUrlComponent(url, 'uid=' + encodeURIComponent(uid));
    if (token) url = Lampa.Utils.addUrlComponent(url, 'token=' + encodeURIComponent(token));
    return url;
  }
  var keyPolicy = null;
  var policyExpires = 0;
  var settingsRows = [];
  var keyCache = {};
  function keyIdentity() {
    return base + '|' + Lampa.Storage.get('account_email', '') + '|' + Lampa.Storage.get('lampac_unic_id', '');
  }
  function keyStore(value, done) {
    var identity = keyIdentity();
    if (value === undefined && Object.prototype.hasOwnProperty.call(keyCache, identity)) return done(null, keyCache[identity]);
    if (!window.indexedDB) return done(true, '');
    var open;
    try { open = indexedDB.open('lampac-torbox-local', 1); } catch (e) { return done(true, ''); }
    var finished = false;
    function finish(error, result) { if (!finished) { finished = true; done(error, result || ''); } }
    open.onupgradeneeded = function () { open.result.createObjectStore('keys'); };
    open.onerror = function () { finish(true, ''); };
    open.onblocked = function () { finish(true, ''); };
    open.onsuccess = function () {
      var db = open.result;
      if (finished) { db.close(); return; }
      try {
        var tx = db.transaction('keys', value === undefined ? 'readonly' : 'readwrite');
        var store = tx.objectStore('keys');
        var req = value === undefined ? store.get(identity) : value ? store.put(value, identity) : store.delete(identity);
        tx.oncomplete = function () {
          var result = value === undefined ? req.result || '' : value;
          keyCache[identity] = result;
          db.close(); finish(null, result);
        };
        tx.onabort = tx.onerror = function () { db.close(); finish(true, ''); };
      } catch (e) { db.close(); finish(true, ''); }
    };
  }
  function torboxAddress(url) {
    var target = document.createElement('a'); target.href = String(url || '');
    var server = document.createElement('a'); server.href = base + '/';
    return target.protocol === server.protocol && target.host === server.host &&
      (target.protocol === 'https:' || /^(localhost|127\.0\.0\.1|\[::1\])$/.test(target.hostname)) &&
      (target.pathname.indexOf('/torbox/') === 0 || /^\/lite\/torbox(?:\/|$)/.test(target.pathname));
  }
  function getPolicy(done, fresh) {
    var identity = keyIdentity();
    if (!fresh && keyPolicy && keyPolicy.identity === identity && policyExpires > Date.now()) return done(keyPolicy);
    $.ajax({ url: endpoint('status'), dataType: 'json', timeout: 10000, cache: false,
      success: function (j) {
        if (!j || !j.success || typeof j.key_editable !== 'boolean') return done(null);
        j.identity = identity; keyPolicy = j; policyExpires = Date.now() + 15000; done(j);
      }, error: function () { done(null); }
    });
  }
  window.lampac_torbox_headers = function (url, done) {
    if (!torboxAddress(url)) return done({});
    var identity = keyIdentity();
    getPolicy(function (policy) {
      if (!policy || !policy.key_editable || identity !== keyIdentity()) return done({});
      keyStore(undefined, function (error, key) { done(!error && key && identity === keyIdentity() ? { 'X-TorBox-Key': key } : {}); });
    });
  };
  function renderKeyPolicy() {
    getPolicy(function (policy) {
      keyStore(undefined, function (error, key) {
        settingsRows.forEach(function (item) {
          var editable = !!(policy && policy.key_editable && !error);
          item.toggleClass('torbox-key-disabled', !editable).attr('aria-disabled', editable ? 'false' : 'true');
          item.find('.settings-param__value').text(!policy ? 'Нет связи с сервером' : policy.server_key ? 'Задан на сервере' : error ? 'Хранилище недоступно' : key ? 'Сохранён на этом устройстве' : 'Не указан');
          item.find('.settings-param__descr').text(policy && policy.server_key ? 'Изменение недоступно: на сервере есть torbox-private.json' : 'Личный ключ хранится только на этом устройстве');
        });
      });
    }, true);
  }
  function editClientKey(clear) {
    getPolicy(function (policy) {
      if (!policy || !policy.key_editable) { renderKeyPolicy(); return Lampa.Noty.show(policy && policy.server_key ? 'Используется ключ, заданный на сервере' : 'Не удалось проверить настройки TorBox'); }
      var editIdentity = policy.identity;
      if (!torboxAddress(endpoint('list'))) return Lampa.Noty.show('Для личного ключа требуется HTTPS');
      function save(value) {
        value = String(value || '').trim();
        if (!clear && !/^[a-zA-Z0-9_.-]{16,256}$/.test(value)) return Lampa.Noty.show('Введите корректный API-ключ TorBox');
        // Recheck after editing: a server file may have appeared while the input was open.
        getPolicy(function (current) {
          if (!current || !current.key_editable || editIdentity !== keyIdentity() || current.identity !== editIdentity) { renderKeyPolicy(); return Lampa.Noty.show('Сохранение ключа сейчас недоступно'); }
          keyStore(clear ? '' : value, function (error) {
            Lampa.Noty.show(error ? 'Не удалось сохранить ключ на устройстве' : clear ? 'Личный ключ удалён с устройства' : 'Ключ TorBox сохранён на устройстве');
            renderKeyPolicy();
          });
        }, true);
      }
      if (clear) save('');
      else Lampa.Input.edit({ title: 'API-ключ TorBox', value: '', free: true, nosave: true }, save);
    }, true);
  }
  function addTorBoxSettings() {
    Lampa.SettingsApi.addComponent({ component: 'torbox', name: 'TorBox', icon: '<svg viewBox="0 0 24 24"><path fill="currentColor" d="M19 10h-.3A7 7 0 0 0 5.2 8.2 5 5 0 0 0 6 18h13a4 4 0 0 0 0-8M10 15V9l5 3z"/></svg>' });
    ['API-ключ TorBox', 'Удалить личный ключ'].forEach(function (title, index) {
      Lampa.SettingsApi.addParam({ component: 'torbox', param: { type: 'button' },
        field: { name: title, description: 'Проверяю настройки сервера…' },
        onChange: function () { editClientKey(index === 1); },
        onRender: function (item) { settingsRows = settingsRows.filter(function (row) { return $.contains(document, row[0]); }); settingsRows.push(item); renderKeyPolicy(); }
      });
    });
    $('<style>').text('.torbox-key-disabled{opacity:.45;pointer-events:none}').appendTo('head');
  }

  function request(path, body, done, failed) {
    window.lampac_torbox_headers(endpoint(path), function (headers) { $.ajax({
      headers: headers, url: endpoint(path), method: body ? 'POST' : 'GET',
      contentType: body ? 'application/json' : undefined,
      data: body ? JSON.stringify(body) : undefined, dataType: 'json', timeout: /^(series|infuse)\//.test(path) ? 90000 : 30000,
      success: function (j) {
        if (j && j.success) done(j);
        else fail(j && j.detail || 'Нет доступа или TorBox вернул ошибку');
      },
      error: function (xhr) { fail(xhr && xhr.responseJSON && xhr.responseJSON.detail || 'Нет связи с TorBox или истёк доступ Lampac'); }
    }); });
    function fail(message) { Lampa.Noty.show(message); if (failed) failed(message); }
  }
  function size(n) {
    n = Number(n) || 0;
    var units = ['Б', 'КБ', 'МБ', 'ГБ', 'ТБ'];
    var i = 0;
    while (n >= 1024 && i < units.length - 1) { n /= 1024; i++; }
    return n.toFixed(i ? 1 : 0) + ' ' + units[i];
  }
  function escaped(s) { return $('<div>').text(String(s || '')).html(); }
  function restore() { generation++; Lampa.Select.hide(); Lampa.Controller.toggle(originController); }
  function begin(controller) { generation++; originController = controller || 'content'; return generation; }
  function choose(title, items, back, onLong) {
    Lampa.Select.show({ title: title, items: items, onLong: onLong,
      onSelect: function (item) { Lampa.Select.hide(); if (item.run) item.run(); },
      onBack: function () { Lampa.Select.hide(); (back || restore)(); }
    });
  }
  function state(t) {
    if (t.ready) return 'Готово';
    var p = Math.max(0, Math.min(100, Number(t.progress || 0) * 100));
    return 'Загрузка ' + p.toFixed(0) + '%' + (t.speed ? ' · ' + size(t.speed) + '/с' : '') + ' · ' + (t.state || 'ожидание');
  }
  function video(f) { return /\.(mkv|mp4|avi|m4v|mov|ts|m2ts|webm|mpg|mpeg|wmv|flv|vob|ogv)$/i.test(f.name || ''); }
  function play(t, file, back, g) {
    Lampa.Noty.show('Получаю ссылку TorBox…');
    request('play', { id: t.id, file: file.id }, function (j) {
      if (g !== generation) return;
      // Lampa selects player_torrent when torrent_hash is present.
      var cell = { url: j.url, title: file.name, timeline: false, torrent_hash: t.hash || ('torbox-' + t.id) };
      Lampa.Player.play(cell);
      Lampa.Player.playlist([cell]);
    }, function () { if (g === generation) back(); });
  }
  var infuseJob = 0;
  var infuseBusy = false;
  function infuseMovie(torrent) {
    var film = torrent.film;
    if (!film) return null;
    var movie = { id: film.tmdb_id || 0, title: film.title, original_title: film.original_title || film.title };
    if (film.media_type === 'tv') {
      movie.name = film.title; movie.original_name = film.original_title || film.title;
      movie.first_air_date = film.year ? film.year + '-01-01' : '0000-01-01';
    } else movie.release_date = film.year ? film.year + '-01-01' : '';
    return movie;
  }
  function ensureInfuse(done) {
    if (window.lampac_infuse_saver && window.lampac_infuse_saver.menu) return done(true);
    Lampa.Utils.putScriptAsync([base + '/its/its.js?v=torbox-20261105'], function () {
      done(!!(window.lampac_infuse_saver && window.lampac_infuse_saver.menu));
    });
  }
  function infuseActions(torrent, files, selected, back, g, bulkOnly) {
    if (infuseBusy) return Lampa.Noty.show('Ссылки Infuse ещё подготавливаются');
    var job = ++infuseJob;
    var activity = Lampa.Activity.active();
    function valid() { return job === infuseJob && g === generation && Lampa.Activity.active() === activity; }
    function finish() { if (job === infuseJob) { infuseBusy = false; Lampa.Loading.stop(); } }
    ensureInfuse(function (ready) {
      if (!valid()) return;
      if (!ready) { Lampa.Noty.show('InfuseSaver недоступен'); return back(); }
      var entries = files.map(function (file) { return { id: file.id, name: file.name, title: file.name, url: '' }; });
      var element = entries.filter(function (entry) { return entry.id === selected.id; })[0];
      var menu = window.lampac_infuse_saver.menu({ element: element, items: entries, params: { movie: infuseMovie(torrent) },
        resolve: function (wanted, done) {
          if (!valid() || infuseBusy) return;
          infuseBusy = true;
          Lampa.Loading.start(function () { if (job !== infuseJob) return; infuseJob++; infuseBusy = false; Lampa.Loading.stop(); back(); });
          var cursor = 0;
          function batch() {
            if (!valid()) { finish(); return; }
            if (cursor >= wanted.length) { finish(); done(); return; }
            var chunk = wanted.slice(cursor, cursor + 4);
            request('infuse/links', { id: torrent.id, files: chunk.map(function (entry) { return entry.id; }) }, function (j) {
              if (!valid()) { finish(); return; }
              var rows = j.data || [];
              if (rows.length !== chunk.length || chunk.some(function (entry) { return !rows.some(function (row) { return row.id === entry.id && /^https:\/\//i.test(row.url || ''); }); })) {
                finish(); Lampa.Noty.show('TorBox вернул неполный список ссылок'); return back();
              }
              chunk.forEach(function (entry) { $.extend(entry, rows.filter(function (row) { return row.id === entry.id; })[0]); });
              cursor += chunk.length; batch();
            }, function () { finish(); if (valid()) back(); });
          }
          batch();
        }
      });
      if (bulkOnly) menu = menu.filter(function (action) { return action.infuse_scope !== 'single'; });
      menu = menu.map(function (action) { return { title: action.title, run: action.onSelect }; });
      Lampa.Select.show({ title: 'Infuse · ' + escaped(selected.name), items: menu,
        onSelect: function (action) { Lampa.Select.hide(); Lampa.Controller.toggle(originController); if (action.run) action.run(); },
        onBack: function () { infuseJob++; Lampa.Select.hide(); back(); }
      });
    });
  }
  function infuseFiles(torrent, back, g) {
    var files = (torrent.files || []).filter(video).sort(function (a, b) { return String(a.name).localeCompare(String(b.name), undefined, { numeric: true }); });
    if (!torrent.ready || !files.length) { Lampa.Noty.show('В TorBox пока нет готовых видеофайлов'); return back(); }
    if (files.length === 1) return infuseActions(torrent, files, files[0], back, g);
    var actions = [{ title: 'Все видеофайлы', run: function () { infuseActions(torrent, files, files[0], back, g, true); } }];
    files.forEach(function (file) { actions.push({ title: escaped(file.name), run: function () { infuseActions(torrent, files, file, back, g); } }); });
    choose('Infuse · ' + escaped(torrent.film && torrent.film.title || torrent.name), actions, back);
  }
  function infuseItem(id, back, g) {
    request('item?id=' + encodeURIComponent(id), null, function (j) { if (g === generation) infuseFiles(j.data, back, g); }, function () { if (g === generation) back(); });
  }

  function item(id, back, g) {
    if (g !== generation) return;
    request('item?id=' + encodeURIComponent(id), null, function (j) {
      if (g !== generation) return;
      var t = j.data;
      var files = (t.files || []).filter(video).sort(function (a, b) {
        return String(a.name).localeCompare(String(b.name), undefined, { numeric: true });
      });
      var reopen = function () { item(id, back, g); };
      var actions = [{ title: 'Обновить · ' + escaped(state(t)), run: reopen }];
      if (t.ready && files.length) {
        files.forEach(function (f) {
          actions.push({ title: escaped(f.name), subtitle: size(f.size), torboxFile: f, run: function () { play(t, f, reopen, g); } });
        });
        actions.push({ title: 'Сохранить в Infuse', run: function () { infuseFiles(t, reopen, g); } });
      } else {
        actions.push({ title: t.ready ? 'В раздаче нет поддерживаемых видеофайлов' : 'Загрузка ещё не готова. Нажмите «Обновить» позднее', run: reopen });
      }
      choose(escaped(t.film && t.film.title || t.name), actions, back, function (action) {
        if (action.torboxFile) infuseActions(t, files, action.torboxFile, reopen, g);
      });
    }, function () { if (g === generation) back(); });
  }
  function posterName(name) {
    var text = String(name || '').replace(/\.(mkv|mp4|avi|m4v|mov|ts|webm)$/i, '');
    var year = /\b((?:19|20)\d{2})\b/.exec(text);
    var serial = /\bS\d{1,2}(?:E\d{1,3})?\b|сезон|season/i.test(text);
    if (year) text = text.slice(0, year.index);
    text = text.replace(/\[[^\]]*\]/g, ' ').replace(/\([^)]*\)/g, ' ')
      .split(/\bS\d{1,2}(?:E\d{1,3})?\b|\b(?:2160p|1080p|720p|480p|4K|WEB[- .]?DL|WEBRip|BluRay|BDRip|REMUX|HDRip|HEVC|x26[45]|H26[45]|сезон|season)\b/i)[0]
      .replace(/[._]/g, ' ').replace(/\s+/g, ' ').replace(/^[\s\-(/]+|[\s\-(/]+$/g, '');
    return { titles: text.split(/\s*\/\s*|\s+\|\s+|\s+aka\s+/i).filter(function (v) { return v.length > 1; }), year: year ? year[1] : '', type: serial ? 'tv' : 'movie' };
  }
  function posterTitle(text) { return String(text || '').toLowerCase().replace(/ё/g, 'е').replace(/[^a-zа-я0-9]/g, ''); }
  var posterQueue = [];
  var posterActive = 0;
  var posterPending = {};
  function posterLookup(name, callback) {
    var parsed = posterName(name);
    if (!parsed.titles.length) return callback(null);
    var key = parsed.titles.map(posterTitle).join('|') + ':' + parsed.year + ':' + parsed.type;
    var cache = Lampa.Storage.get('torbox_posters_v1', {});
    var hit = cache[key];
    if (hit && hit.expires > Date.now()) return callback(hit.poster);
    if (posterPending[key]) return posterPending[key].push(callback);
    posterPending[key] = [callback];
    posterQueue.push(function (release) {
      var finished = false;
      var hadError = false;
      var timer = setTimeout(function () { finish(null, false); }, 25000);
      function finish(poster, save) {
        if (finished) return;
        finished = true;
        clearTimeout(timer);
        if (save) {
          var stored = Lampa.Storage.get('torbox_posters_v1', {});
          stored[key] = { poster: poster, expires: Date.now() + (poster ? 7 * 86400000 : 1800000) };
          var keys = Object.keys(stored).sort(function (a, b) { return stored[b].expires - stored[a].expires; });
          keys.slice(256).forEach(function (old) { delete stored[old]; });
          Lampa.Storage.set('torbox_posters_v1', stored);
        }
        var waiting = posterPending[key] || [];
        delete posterPending[key];
        release();
        waiting.forEach(function (done) { done(poster); });
      }
      var queries = parsed.titles.slice(0, 2);
      var types = [parsed.type, parsed.type === 'tv' ? 'movie' : 'tv'];
      function search(index) {
        if (finished) return;
        if (index >= queries.length * types.length) return finish(null, !hadError);
        var type = types[Math.floor(index / queries.length)];
        Lampa.Api.sources.tmdb.get('search/' + type, { query: encodeURIComponent(queries[index % queries.length]), page: 1 }, function (json) {
          if (finished) return;
          var matches = (json.results || []).filter(function (movie) {
            var names = [movie.title, movie.original_title, movie.name, movie.original_name].map(posterTitle);
            return movie.poster_path && (!parsed.year || String(movie.release_date || movie.first_air_date || '').slice(0, 4) === parsed.year) && parsed.titles.some(function (title) { return names.indexOf(posterTitle(title)) >= 0; });
          });
          // Ambiguous remakes without a year retain the title placeholder.
          if (matches.length === 1) finish(matches[0].poster_path, true);
          else search(index + 1);
        }, function () { hadError = true; search(index + 1); });
      }
      search(0);
    });
    pumpPosters();
  }
  function pumpPosters() {
    while (posterActive < 2 && posterQueue.length) {
      posterActive++;
      posterQueue.shift()(function () { posterActive--; pumpPosters(); });
    }
  }
  function filmFromCard(card) {
    if (!card || !(card.title || card.name)) return null;
    var source = card.source || 'tmdb';
    return { tmdb_id: Number(card.tmdb_id || ((source === 'tmdb' || source === 'cub') && card.id) || 0),
      title: card.title || card.name, original_title: card.original_title || card.original_name || '',
      year: Number(String(card.release_date || card.first_air_date || '').slice(0, 4)) || 0,
      poster_path: /^\/[a-zA-Z0-9_.-]{1,160}$/.test(card.poster_path || '') ? card.poster_path : '', media_type: card.media_type === 'tv' || card.name ? 'tv' : 'movie' };
  }
  function currentFilm(torrent) {
    var active = Lampa.Activity.active() || {};
    return filmFromCard(torrent.movie || active.movie || active.card || active.activity && active.activity.object && active.activity.object.movie);
  }
  function clarifyFilm(torrent, done, back, valid) {
    var parsed = posterName(torrent.name);
    Lampa.Input.edit({ title: 'Название фильма или сериала', value: torrent.film && torrent.film.title || parsed.titles[0] || '', free: true, nosave: true }, function (query) {
      if (!valid()) return;
      if (!String(query || '').trim()) return back();
      Lampa.Noty.show('Ищу фильм…');
      Lampa.Api.search({ query: encodeURIComponent(query.trim()), page: 1 }, function (json) {
        if (!valid()) return;
        var found = [];
        ['movie', 'tv'].forEach(function (type) {
          ((json[type] || {}).results || []).slice(0, 20).forEach(function (movie) {
            var film = filmFromCard(movie);
            if (!film) return;
            film.media_type = type;
            found.push(film);
          });
        });
        found.sort(function (a, b) { return Number(b.year === Number(parsed.year)) - Number(a.year === Number(parsed.year)); });
        var actions = found.map(function (film) {
          return { title: escaped(film.title) + (film.year ? ' (' + film.year + ')' : ''), subtitle: escaped(film.original_title) + ' · ' + (film.media_type === 'tv' ? 'Сериал' : 'Фильм'),
            run: function () {
              request('metadata', { id: torrent.id, film: film }, function () { if (valid()) done(); }, function () { if (valid()) back(); });
            } };
        });
        actions.unshift({ title: found.length ? 'Изменить поиск' : 'Не найдено · изменить поиск', run: function () { clarifyFilm(torrent, done, back, valid); } });
        choose('Выберите фильм', actions, back);
      });
    });
  }
  function Library(object) {
    Lampa.InteractionCategory.call(this, object);
    var self = this;
    var alive = true;
    var more = false;
    var loading = false;
    var deleting = false;
    var g;
    var destroy = this.destroy;
    function reload() { Lampa.Activity.replace({ component: 'my_torbox', title: 'Мой TorBox', page: 1 }); }
    function page(number, done, failed) {
      request('list?offset=' + ((number - 1) * 100), null, function (j) {
        if (!alive) return;
        more = !!j.more;
        done({ results: (j.data || []).map(function (t) {
          var film = t.film || {};
          return { id: 'torbox-' + t.id, title: film.title || t.name, original_title: film.original_title || t.name, release_date: film.year ? film.year + '-01-01' : '', torboxItem: t };
        }), total_pages: number + (j.more ? 1 : 0) });
      }, function () { if (alive && failed) failed(); });
    }
    this.create = function () {
      g = begin('content');
      object.page = 1;
      self.activity.loader(true);
      page(1, function (data) {
        if (!data.results.length) data.results.push({ id: 'torbox-empty', title: 'В TorBox пока нет загрузок', torboxAction: reload });
        self.build(data);
      }, function () {
        self.build({ results: [{ id: 'torbox-retry', title: 'Повторить загрузку', torboxAction: reload }], total_pages: 1 });
      });
    };
    this.nextPageReuest = function (params, done, failed) { page(params.page, done, failed); };
    this.next = function () {
      if (!alive || !more || loading) return;
      loading = true;
      page(object.page + 1, function (data) {
        object.page++;
        self.append(data, true);
        loading = false;
        self.limit();
      }, function () { loading = false; });
    };
    this.cardRender = function (params, data, card) {
      var t = data.torboxItem;
      var node = card.render();
      node.addClass('torbox-library-card');
      node.find('.card__view').append($('<div class="torbox-library-card__caption">').text(data.title));
      node.append($('<div class="torbox-library-card__status">').text(t ? state(t) + ' · ' + size(t.size) : 'Нажмите, чтобы обновить'));
      var posterRequested = false;
      card.onVisible = function () {
        if (!t || posterRequested || !alive) return;
        posterRequested = true;
        function applyPoster(poster) {
          if (!alive || !poster || !card.img) return;
          data.poster_path = poster;
          card.img.onload = function () { if (alive && data.poster_path) node.addClass('torbox-library-card--poster card--loaded'); };
          card.img.onerror = function () {
            node.removeClass('torbox-library-card--poster');
            data.poster_path = '';
            this.src = './img/img_broken.svg';
          };
          card.img.src = Lampa.Api.img(poster, 'w342');
        }
        if (t.film && t.film.poster_path) applyPoster(t.film.poster_path);
        else if (t.film && t.film.tmdb_id) {
          Lampa.Api.sources.tmdb.get((t.film.media_type === 'tv' ? 'tv/' : 'movie/') + t.film.tmdb_id, {}, function (movie) {
            if (!alive || !movie.poster_path) return;
            applyPoster(movie.poster_path);
            var film = $.extend({}, t.film, { poster_path: movie.poster_path });
            request('metadata', { id: t.id, film: film, automatic: true }, function () {});
          });
        } else posterLookup(t.film && t.film.title ? t.film.title + (t.film.year ? ' ' + t.film.year : '') : t.name, applyPoster);
      };
      card.onEnter = function () {
        if (data.torboxAction) return data.torboxAction();
        originController = 'content';
        item(t.id, function () { Lampa.Controller.toggle('content'); }, g);
      };
      card.onMenu = function () {
        originController = 'content';
        var back = function () { Lampa.Controller.toggle('content'); };
        var actions = [{ title: 'Обновить список', run: reload }];
        if (t) {
          if (t.ready) actions.push({ title: 'Сохранить в Infuse', run: function () { infuseItem(t.id, back, g); } });
          actions.push({ title: 'Уточнить фильм', run: function () { clarifyFilm(t, reload, back, function () { return alive; }); } });
          actions.push({ title: 'Удалить из TorBox', run: function () {
            var title = t.film && t.film.title || t.name;
            choose('Удалить «' + escaped(title) + '»?', [
              { title: 'Отмена', run: back },
              { title: 'Удалить из TorBox', subtitle: 'Вся раздача со всеми файлами будет удалена из вашего аккаунта TorBox', run: function () {
                if (!alive || deleting) return;
                deleting = true;
                Lampa.Noty.show('Удаляю из TorBox…');
                function result(j, attempt) {
                  if (!alive) { deleting = false; return; }
                  if (j.deletion_confirmed === false) {
                    if (j.ticket && attempt < 3) return setTimeout(function () {
                      if (alive) request('delete/check', { ticket: j.ticket }, function (next) { result(next, attempt + 1); }, function () { deleting = false; if (alive) back(); });
                    }, 1000);
                    deleting = false; Lampa.Noty.show('TorBox принял удаление. Подтверждение ещё не получено — обновите список'); back(); return;
                  }
                  deleting = false;
                  Lampa.Noty.show(j && j.metadata_removed === false
                    ? 'Раздача удалена из TorBox. Не удалось очистить привязку фильма'
                    : 'Раздача удалена из TorBox');
                  reload();
                }
                request('delete', { id: t.id, confirmed: true }, function (j) { result(j, 0); }, function () { deleting = false; if (alive) back(); });
              } }
            ], back);
          } });
          actions.push({ title: 'Сведения о раздаче', run: function () {
            choose('Раздача', [{ title: escaped(t.name), subtitle: escaped(state(t)) + ' · ' + size(t.size), run: back }], back);
          } });
        }
        choose('Мой TorBox', actions, back);
      };
    };
    this.destroy = function () {
      alive = false;
      if (g === generation) generation++;
      destroy.call(self);
    };
  }
  function openLibrary() { Lampa.Activity.push({ component: 'my_torbox', title: 'Мой TorBox', page: 1 }); }
  function torrentLink(t) {
    var candidates = [t.MagnetUri, t.Link, t.magnet, t.link];
    for (var i = 0; i < candidates.length; i++) if (/^magnet:\?/i.test(candidates[i] || '')) return candidates[i];
    for (var k = 0; k < candidates.length; k++) if (/^https?:\/\//i.test(candidates[k] || '')) return candidates[k];
    return '';
  }
  function watch(t, film, allowDownload) {
    if (busy) return Lampa.Noty.show('Запрос TorBox уже выполняется');
    var link = torrentLink(t || {});
    var hash = (t && (t.InfoHash || t.Hash || t.infohash || t.hash)) || '';
    if (!link && !hash) return Lampa.Noty.show('У раздачи нет magnet, torrent-ссылки или хеша');
    var g = begin('content');
    busy = true;
    Lampa.Noty.show('Проверяю раздачу в TorBox…');
    request('add', { link: link, hash: hash, film: film, allow_download: allowDownload === true }, function (j) {
      busy = false;
      if (g !== generation) return;
      if (j.confirmation_required) {
        choose('Раздачи нет в кэше TorBox', [
          { title: 'Загрузить', subtitle: 'TorBox скачает выбранную раздачу', run: function () { watch(t, film, true); } },
          { title: 'Вернуться к выбору', run: restore }
        ], restore);
        return;
      }
      Lampa.Noty.show(j.existing ? 'Раздача уже есть в вашем TorBox' : (j.cached ? 'Раздача есть в кэше TorBox' : 'Раздача добавлена. TorBox начнёт загрузку'));
      item(j.id, restore, g);
    }, function () { busy = false; });
  }
  var activeSeries = null;
  var externalPreparation = false;
  window.lampac_torbox_series_play = function (response, first, movie) {
    if (externalPreparation) return Lampa.Noty.show('Плейлист TorBox ещё подготавливается');
    var info = response.torbox_series;
    if (!info || !info.items || !info.items.length) return;
    var run = { session: info.session, playlist: [], busy: false };
    activeSeries = run;
    function live() { return activeSeries === run && Lampa.Player.opened() && Lampa.Player.playdata().torbox_session === run.session; }
    function stop(message) {
      if (activeSeries !== run) return;
      activeSeries = null;
      Lampa.Player.close();
      if (message) Lampa.Noty.show(message);
    }
    function cell(row) {
      var title = (info.title || '') + ' · ' + row.season + ' сезон · ' + row.episode + ' серия';
      var identity = movie.original_name || movie.original_title || movie.id || info.title;
      return { title: title, season: row.season, episode: row.episode, voice_name: info.selection,
        torbox_session: run.session, isonline: true, quality: {}, hls_manifest_timeout: 60000,
        timeline: Lampa.Timeline.view(Lampa.Utils.hash([row.season, row.season > 10 ? ':' : '', row.episode, identity].join('')),
          Lampa.Timeline.link ? Lampa.Timeline.link(movie, row.season, row.episode) : null) };
    }
    function load(target, advance, done) {
      if (run.busy || !live()) return;
      run.busy = true;
      var path = advance ? 'series/advance?session=' + encodeURIComponent(run.session) + '&after=' + (target - 1)
        : 'series/play?session=' + encodeURIComponent(run.session) + '&index=' + target;
      if (typeof info.version === 'number') path += '&version=' + info.version;
      request(path, null, function (j) {
        run.busy = false;
        if (!live()) return;
        if (j.ended) return stop(j.detail || 'Сериал завершён');
        if (!sync(j)) return;
        if (j.selection_notice) Lampa.Noty.show(j.selection_notice);
        Lampa.Player.playlist(run.playlist);
        done();
      }, function () { run.busy = false; if (live()) stop(); });
    }
    function sync(j) {
      var incoming = j.torbox_series;
      if (!Array.isArray(incoming.items)) {
        if (incoming.session !== info.session || incoming.version !== info.version) { stop('Плейлист изменился. Откройте сериал заново'); return false; }
        info = $.extend({}, info, incoming, { items: info.items });
      } else info = incoming;
      info.items.forEach(function (row) {
        var entry = run.playlist[row.index];
        if (!entry || entry.torbox_advance) {
          var value = cell(row);
          if (!entry) entry = run.playlist[row.index] = value;
          else { Object.keys(entry).forEach(function (key) { delete entry[key]; }); $.extend(entry, value); }
          entry.url = function (done) { load(row.index, false, done); };
        }
        if (row.index === info.selected_index) entry.url = j.url;
      });
      var next = info.next_index;
      run.playlist.length = next;
      run.playlist.push({ title: (info.title || '') + ' · ' + info.next_season + ' сезон',
        season: info.next_season, episode: 1, torbox_session: run.session, torbox_advance: true,
        url: function (done) { load(next, true, done); } });
      // Lampa's native playlist callback keeps the currently selected object by reference.
      // sync therefore mutates the season placeholder instead of replacing that object.
      return true;
    }
    sync(response);
    var selected = run.playlist[info.selected_index];
    $.extend(selected, { headers: first.headers, subtitles: first.subtitles });
    if (Lampa.Storage.field('player') !== 'inner') {
      externalPreparation = true;
      var activity = Lampa.Activity.active();
      function prepare(preparation) {
        if (activeSeries !== run || Lampa.Activity.active() !== activity) { externalPreparation = false; return; }
        request('series/external', { session: run.session, index: info.selected_index, preparation: preparation || '' }, function (j) {
          if (activeSeries !== run || Lampa.Activity.active() !== activity) { externalPreparation = false; return; }
          if (j.pending) {
            Lampa.Noty.show('Подготавливаю плейлист TorBox: ' + j.episodes + ' серий');
            return setTimeout(function () { prepare(j.preparation); }, 0);
          }
          externalPreparation = false;
          if (!j.playlist || !j.playlist.length || !j.playlist[j.selected_index]) return Lampa.Noty.show('TorBox не вернул плейлист');
          var playlist = j.playlist.map(function (row) {
            return $.extend(cell(row), { url: row.url });
          });
          var launch = $.extend({}, playlist[j.selected_index], { playlist: playlist });
          activeSeries = null;
          Lampa.Player.play(launch);
          Lampa.Player.playlist(playlist);
        }, function () { externalPreparation = false; if (activeSeries === run) activeSeries = null; });
      }
      Lampa.Noty.show('Подготавливаю плейлист TorBox…');
      prepare('');
      return;
    }
    Lampa.Player.play($.extend({}, selected, { playlist: run.playlist }));
    Lampa.Player.playlist(run.playlist);
    if (response.selection_notice) Lampa.Noty.show(response.selection_notice);
  };
  // Auto-advance is enabled for this TorBox session without changing global settings.
  function startSeriesControls() {
  Lampa.PlayerVideo.listener.follow('ended', function () {
    if (activeSeries && Lampa.Player.opened() && !Lampa.Storage.field('playlist_next') && !Lampa.Select.opened()) Lampa.PlayerPlaylist.next();
  });
  Lampa.Player.listener.follow('destroy', function () {
    var run = activeSeries;
    if (!run) return;
    setTimeout(function () {
      if (activeSeries === run && (!Lampa.Player.opened() || Lampa.Player.playdata().torbox_session !== run.session)) activeSeries = null;
    }, 0);
  });
  }

  function start() {
    startSeriesControls();
    addTorBoxSettings();
    Lampa.Component.add('my_torbox', Library);
    $('<style>').text('.torbox-library-card .card__img{opacity:.18}.torbox-library-card .card__view{background:rgba(255,255,255,.06)}.torbox-library-card__caption{position:absolute;left:1em;right:1em;top:50%;transform:translateY(-50%);text-align:center;overflow-wrap:anywhere;font-size:1.1em;line-height:1.4;display:-webkit-box;-webkit-line-clamp:7;-webkit-box-orient:vertical;overflow:hidden}.torbox-library-card--poster .card__img{opacity:1}.torbox-library-card--poster .torbox-library-card__caption{display:none}.torbox-library-card__status{font-size:.8em;opacity:.7;margin-top:.4em;line-height:1.4}').appendTo('head');
    Lampa.Listener.follow('torrent', function (e) {
      if (e.type === 'onlong' && e.menu) {
        var film = currentFilm(e.element || {});
        e.menu.push({ title: 'Смотреть через TorBox', onSelect: function () { watch(e.element, film); } });
      }
    });
    var entry = $('<li class="menu__item selector" data-action="my_torbox"><div class="menu__ico"><svg viewBox="0 0 24 24" width="24" height="24"><path fill="currentColor" d="M19 10h-.3A7 7 0 0 0 5.2 8.2 5 5 0 0 0 6 18h13a4 4 0 0 0 0-8M10 15V9l5 3z"/></svg></div><div class="menu__text">Мой TorBox</div></li>');
    entry.on('hover:enter', openLibrary);
    $('.menu .menu__list').eq(0).append(entry);
  }
  if (window.appready) start();
  else Lampa.Listener.follow('app', function (e) { if (e.type === 'ready') start(); });
})();
