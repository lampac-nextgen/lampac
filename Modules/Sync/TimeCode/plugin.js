(function() {
  'use strict';

  // Идентичность раздаёт сервер: тогда браузер на телефоне и нативный клиент на приставке
  // попадают в ОДНУ область данных. Пустая строка — сервер её не раздаёт (accsdb уже выдал
  // каждому свою), и тогда работает прежний случайный идентификатор.
  var server_uid = '{uid}';
  var unic_id = server_uid;

  if (!unic_id) {
    unic_id = Lampa.Storage.get('lampac_unic_id', '');
    if (!unic_id) {
      unic_id = Lampa.Utils.uid(8).toLowerCase();
      Lampa.Storage.set('lampac_unic_id', unic_id);
    }
  }

  function timelineExtra(road, card) {
    if (!road || typeof road.card !== 'string') return;
    var link = /^(?:[a-z0-9_]+_)?(movie|tv)_(\d+)$/.exec(road.card);
    if (!link || link[2] + '_' + link[1] !== card) return;
    return JSON.stringify({ card: road.card, season: road.season, episode: road.episode });
  }
	
  function _classCallCheck(instance, Constructor) {
    if (!(instance instanceof Constructor)) {
      throw new TypeError("Cannot call a class as a function");
    }
  }

  function _defineProperties(target, props) {
    for (var i = 0; i < props.length; i++) {
      var descriptor = props[i];
      descriptor.enumerable = descriptor.enumerable || false;
      descriptor.configurable = true;
      if ("value" in descriptor) descriptor.writable = true;
      Object.defineProperty(target, descriptor.key, descriptor);
    }
  }

  function _createClass(Constructor, protoProps, staticProps) {
    if (protoProps) _defineProperties(Constructor.prototype, protoProps);
    if (staticProps) _defineProperties(Constructor, staticProps);
    Object.defineProperty(Constructor, "prototype", {
      writable: false
    });
    return Constructor;
  }

  var Timecode = /*#__PURE__*/ function() {
    function Timecode(field) {
      _classCallCheck(this, Timecode);
      this.localhost = '{localhost}/';
      this.network = new Lampa.Reguest();
    }
    _createClass(Timecode, [{
      key: "init",
      value: function init() {
        var _this = this;
        Lampa.Timeline.listener.follow('update', this.add.bind(this));
        if (Lampa.Player && Lampa.Player.listener) {
          Lampa.Player.listener.follow('create', function(e) { _this.rememberPlayback(e && e.data); });
        }
        Lampa.Listener.follow('full', function(e) {
          if (e.type == 'complite') {
            _this.update();
            _this.flushPending();
          }
        });
		Lampa.Listener.follow('lampac', function(e) {
          if (e.type == 'timecode_pullFromServer') _this.update();
        });
        // Шина синхронизации: сервер рассылает 'timecode' всем, кроме автора записи,
        // поэтому своё же эхо здесь не приходит и цикла записи нет.
        document.addEventListener('lwsEvent', function(e) {
          if (e.detail && e.detail.name == 'timecode') _this.update();
          else if (e.detail && e.detail.name == 'system' && e.detail.data == 'reconnected') {
            _this.update();
            _this.flushPending();
          }
        });

        _this.awaitBookmarks(0);

        // Список закладок доступен только в узком окне сразу после входа и нигде не сохраняется.
        // Поэтому ждём не только на старте: вход может случиться сильно позже.
        Lampa.Storage.listener.follow('change', function(e) {
          if (e.name == 'account' || e.name == 'account_bookmarks') {
            window.lampac_timecode_migrated = false;
            _this.awaitBookmarks(0);
          }
          if (e.name == 'account' || e.name == 'account_bookmarks' || e.name == 'favorite' || e.name == 'lampac_profile_id') {
            _this.flushPending();
          }
        });
        document.addEventListener('visibilitychange', function() {
          if (!document.hidden) _this.flushPending();
        });
        setTimeout(function() { _this.flushPending(); }, 2000);
      }
    }, {
      /**
       * Имя области данных на сервере. Основной профиль куба не шлёт ничего: его область — общая,
       * она уже накоплена, и двигать её нельзя. Остальные шлют номер профиля куба — нативный
       * клиент называет профиль так же, поэтому два клиента сходятся без договорённости.
       */
      key: "profileId",
      value: function profileId() {
        var manual = Lampa.Storage.get('lampac_profile_id', '');
        if (manual !== '' && manual !== 0) return String(manual);

        try {
          var permit = Lampa.Account.Permit;
          if (!permit.sync) return '';
          var profile = permit.account.profile;
          if (!profile || !profile.id || profile.main) return '';
          return String(profile.id);
        } catch (e) { return ''; }
      }
    }, {
      key: "url",
      value: function url(method) {
        var url = this.localhost + 'timecode/' + method;
        var account = Lampa.Storage.get('account', '{}');
        var activity = Lampa.Storage.get('activity', '{}');
        var card = activity.movie || activity.card || {
          id: 0
        };
        var card_id = (card.id || 0) + '_' + (card.name ? 'tv' : 'movie');
        var uid = unic_id;
        var token = '{token}';
		
        if (token != ''){
          if (url.indexOf('token=') == -1) url = Lampa.Utils.addUrlComponent(url, 'token=' + token);
        }
		// account_email стоит в getuid раньше uid, поэтому при розданной сервером идентичности
		// его слать нельзя: вход в куб уводил бы в другую область, а выход возвращал обратно.
		if (account.email && !server_uid){
		  if (url.indexOf('account_email=') == -1) url = Lampa.Utils.addUrlComponent(url, 'account_email=' + encodeURIComponent(account.email));
		}
		if (uid){
		  if (url.indexOf('uid=') == -1) url = Lampa.Utils.addUrlComponent(url, 'uid=' + encodeURIComponent(uid));
		}
		
		var profile_id = this.profileId();
        if (profile_id != '') url = Lampa.Utils.addUrlComponent(url, 'profile_id='+profile_id);
		
        url = Lampa.Utils.addUrlComponent(url, 'card_id=' + encodeURIComponent(card_id));

        // Своё соединение — чтобы сервер не прислал нам обратно нашу же запись.
        var connectionId = window.lwsEvent && window.lwsEvent.connectionId;
        if (connectionId) url = Lampa.Utils.addUrlComponent(url, 'connectionId=' + encodeURIComponent(connectionId));

        return url;
      }
    }, {
      /**
       * Имя ящика с прогрессом спрашиваем у самой лампы: она переезжает в профильный ящик по
       * включённой синхронизации, а не по наличию профиля. Вход без синхронизации разводил нас
       * по разным ящикам, и плагин переставал видеть то, что пишет лампа.
       */
      key: "filename",
      value: function filename() {
        var name = '';

        try { name = Lampa.Timeline.filename(); } catch (e) {}

        if (!name) {
          var acc = Lampa.Storage.get('account', '{}');
          name = 'file_view' + (acc.profile ? '_' + acc.profile.id : '');
        }

        // Лампа копирует накопленное в профильный ящик только в ветке дампа, а мы можем прийти
        // раньше неё.
        if (name != 'file_view' && window.localStorage.getItem(name) === null) {
          Lampa.Storage.set(name, Lampa.Arrays.clone(Lampa.Storage.cache('file_view', 10000, {})));
        }

        return name;
      }
    }, {
      key: "update",
      value: function update() {
        var _this2 = this;
        var url = this.url('all');
        this.network.silent(url, function(result) {
          if (result.accsdb) return;
          var viewed = Lampa.Storage.cache(_this2.filename(), 10000, {});
          for (var i in result) {
            var time = JSON.parse(result[i]);
            if (!Lampa.Arrays.isObject(time)) continue;
            viewed[i] = time;
            Lampa.Arrays.extend(viewed[i], {
              duration: 0,
              time: 0,
              percent: 0
            });
            delete viewed[i].hash;
          }
          Lampa.Storage.set(_this2.filename(), viewed, true);
          if (typeof Lampa.Timeline.read === 'function') Lampa.Timeline.read();

        });
      }

    }, {
      /** Адрес нативного эндпоинта: без card_id, он тут передаётся в самой строке. */
      key: "apiUrl",
      value: function apiUrl(method) {
        var url = this.localhost + 'timecode/' + method;
        var account = Lampa.Storage.get('account', '{}');
        var token = '{token}';

        if (token != '') url = Lampa.Utils.addUrlComponent(url, 'token=' + token);
        else if (account.email && !server_uid) url = Lampa.Utils.addUrlComponent(url, 'account_email=' + encodeURIComponent(account.email));
        else url = Lampa.Utils.addUrlComponent(url, 'uid=' + encodeURIComponent(unic_id));

        var profile_id = this.profileId();
        if (profile_id != '') url = Lampa.Utils.addUrlComponent(url, 'profile_id=' + profile_id);

        var connectionId = window.lwsEvent && window.lwsEvent.connectionId;
        if (connectionId) url = Lampa.Utils.addUrlComponent(url, 'connectionId=' + encodeURIComponent(connectionId));

        return url;
      }
    }, {
      /**
       * Дождаться закладок и только потом разбирать.
       *
       * Таймлайн куба приезжает первым, закладки — отдельным запросом и позже, причём в
       * localStorage они не оседают: `Bookmarks.all()` отдаёт то, что лежит в памяти после
       * `update()`. Поэтому ждём появления, а если их никто не запросил — просим сами.
       */
      key: "awaitBookmarks",
      value: function awaitBookmarks(attempt) {
        var _this5 = this;
        var ready = 0;

        try { ready = (Lampa.Account.Bookmarks.all() || []).length; } catch (e) {}
        if (!ready) ready = ((Lampa.Storage.get('favorite', {}) || {}).card || []).length;

        // Метаданные позволяют восстановить прогресс ещё до загрузки закладок.
        if (ready || attempt === 0) this.migrate();
        if (ready) return;
        if (attempt > 30) return;

        // Никто не запросил — запрашиваем сами, но один раз.
        if (attempt === 2) { try { Lampa.Account.Bookmarks.update(); } catch (e) {} }

        setTimeout(function() { _this5.awaitBookmarks(attempt + 1); }, 2000);
      }
    }, {
      key: "migrate",
      value: function migrate() {
        var _this4 = this;
        var revision = JSON.stringify(this.cards());
        if (window.lampac_timecode_migrated && this.migrationRevision === revision) return;
        window.lampac_timecode_migrated = true;
        this.migrationRevision = revision;
        var scope = this.exportScope();
        this.network.silent(this.apiUrl('dump'), function(dump) {
          if (_this4.exportScope() !== scope) return;
          var known = {};
          ((dump && dump.rows) || []).forEach(function(row) {
            if (row.hash) known[row.hash] = row;
            if (row.id) known[row.id] = row;
          });
          _this4.resolve(known);
          _this4.flushPending();
        }, function() {});
      }
    }, {
      /**
       * Опознать отметки, которые лежат локально без идентичности.
       *
       * Дамп оседает в localStorage мимо Timeline.update. Сначала используем card/season/episode,
       * а старые записи узнаём по закладкам. Хеш необратим, но обратимо обратное: у закладок есть карточки
       * с оригинальными названиями, а хеш считается ровно из них. Перебираем закладки, считаем их
       * хеши и смотрим, какие из них лежат в file_view.
       */
      key: "resolve",
      value: function resolve(known) {
        var viewed = Lampa.Storage.cache(this.filename(), 10000, {});
        var index = this.cards();
        var claims = {};
        var linked = {};
        var knownById = {};
        var legacy = false;

        for (var key in known) {
          if (known[key].id) knownById[known[key].id] = known[key];
        }
        for (var localHash in viewed) {
          var direct = this.identifyMetadata(viewed[localHash]);
          if (direct) {
            linked[localHash] = direct;
            claims[localHash] = [direct];
          } else legacy = true;
        }

        function claim(hash, identity, card) {
          if (!viewed[hash] || linked[hash]) return;
          if (!claims[hash]) claims[hash] = [];
          for (var i = 0; i < claims[hash].length; i++) {
            if (claims[hash][i].identity === identity) return;
          }
          claims[hash].push({ identity: identity, card: card });
        }

        if (legacy) index.forEach(function(entry) {
          if (entry.t === 'tv') {
            // Настоящее число сезонов, когда закладка его несёт: перебор уже точный.
            var seasons = entry.s > 0 ? Math.min(entry.s + 1, 60) : 50;
            for (var season = 0; season <= seasons; season++) {
              for (var episode = 1; episode <= 200; episode++) {
                claim(
                  Lampa.Utils.hash([season, season > 10 ? ':' : '', episode, entry.o].join('')),
                  'tv-' + entry.i + '-s' + season + 'e' + episode,
                  entry.i + '_tv'
                );
              }
            }
          }
          // Только оригинальное название: локализованное даст чужой хеш.
          else claim(Lampa.Utils.hash(entry.o), 'movie-' + entry.i, entry.i + '_movie');
        });

        var rows = [];
        var ambiguous = 0;

        for (var hash in claims) {
          // У сервера уже есть идентичность — чужую версию не навязываем.
          // После смены названия хеш может измениться, а идентичность остаётся прежней.
          var remote = (linked[hash] && knownById[linked[hash].identity]) || known[hash];
          if (remote && remote.id) {
            var local = viewed[hash];
            var identified = linked[hash] || this.identify(hash, local);
            if (!remote.deleted && Number(local.updated) > Number(remote.watched_at || 0) &&
                identified && identified.identity === remote.id && identified.card === remote.card) {
              // Восстанавливаем свежий локальный прогресс, оставшийся от клиента без очереди.
              this.add({data: {hash: hash, road: local}});
            }
            continue;
          }

          // Один хеш достаётся двум одноимённым работам, и куб, ключующийся только хешем, хранит
          // на них ОДНУ строку. У лампака ключ — (пользователь, карточка, хеш), поэтому каждой
          // найдётся своя: закладка на обе и есть свидетельство, что смотрели обе.
          var winners = claims[hash];
          if (winners.length > 1) ambiguous++;

          var source = known[hash] || {
            position: viewed[hash].time || 0,
            duration: viewed[hash].duration || 0,
            percent: viewed[hash].percent || 0,
            watched_at: viewed[hash].updated || 0
          };

          // Пустая отметка на той стороне прочтётся как сброс таймлайна.
          if (!(source.percent > 0) && !(source.position > 1)) continue;

          winners.forEach(function(found) {
            rows.push({
              id: found.identity,
              hash: hash,
              card: found.card,
              position: source.position || 0,
              duration: source.duration || 0,
              percent: source.percent || 0,
              watched_at: source.watched_at || 0,
              extra: timelineExtra(viewed[hash], found.card) || (source.card === found.card ? source.extra : undefined)
            });
          });
        }

        console.log('Lampac TimeCode', 'cards=' + index.length,
          'local=' + Object.keys(viewed).length, 'claimed=' + Object.keys(claims).length,
          'metadata=' + Object.keys(linked).length, 'shared=' + ambiguous, 'rows=' + rows.length);

        if (!rows.length) return;

        var url = this.apiUrl('set');
        // Пачками: у /timecode/ стоит лимит 10 запросов в секунду.
        (function send(offset) {
          if (offset >= rows.length) return;
          $.ajax({
            url: url,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ rows: rows.slice(offset, offset + 200) }),
            complete: function() { setTimeout(function() { send(offset + 200); }, 200); }
          });
        })(0);
      }
    }, {
      /** Сохранить исходные привязки до передачи плейлиста нативному плееру. */
      key: "rememberPlayback",
      value: function rememberPlayback(data) {
        if (!data) return;
        var links = {};
        var items = [data].concat(Array.isArray(data.playlist) ? data.playlist : []);
        for (var i = 0; i < items.length; i++) {
          var road = items[i] && items[i].timeline;
          if (!road || !road.hash || typeof road.card !== 'string') continue;
          var card = /^(?:[a-z0-9_]+_)?(movie|tv)_(\d+)$/.exec(road.card);
          if (!card) continue;
          var found = this.identifyMetadata({ card: card[1] + '_' + card[2], season: road.season, episode: road.episode });
          if (!found) continue;
          if (!Object.prototype.hasOwnProperty.call(links, String(road.hash))) {
            links[String(road.hash)] = JSON.parse(timelineExtra(road, found.card));
          }
        }
        // Старый APK возвращает Timeline.update без метаданных. Храним привязки текущего
        // плейлиста отдельно от прогресса, чтобы пережить пересоздание WebView без сброса позиции.
        var playback = Lampa.Storage.get('lampac_timecode_playback_v1', {});
        playback[this.exportScope()] = links;
        Lampa.Storage.set('lampac_timecode_playback_v1', playback, true);
      }
    }, {
      /**
       * Сохраняем снимок до HTTP-запроса. Карточку определяем по метаданным Timeline, а для
       * старых записей проверяем хеш по открытой карточке и закладкам. Неопознанные отметки
       * остаются в очереди до появления данных для распознавания.
       */
      key: "add",
      value: function add(e) {
        if (!e || !e.data || !e.data.hash) return;
        var hash = String(e.data.hash);
        var road = e.data.road || {};
        var saved = {};
        ['time', 'duration', 'percent', 'profile', 'updated', 'card', 'season', 'episode'].forEach(function(key) {
          if (typeof road[key] == 'number' || typeof road[key] == 'string') saved[key] = road[key];
        });
        var scope = this.exportScope();
        if (!saved.card) {
          var playback = Lampa.Storage.get('lampac_timecode_playback_v1', {});
          var link = (playback[scope] || {})[hash];
          if (link) {
            saved.card = link.card;
            if (link.season !== undefined) saved.season = link.season;
            if (link.episode !== undefined) saved.episode = link.episode;
          }
        }
        var outbox = Lampa.Storage.get('lampac_timecode_outbox_v1', {});
        if (!outbox[scope]) outbox[scope] = {};
        var previous = outbox[scope][hash];
        if (!previous || !(Number(previous.updated) > Number(saved.updated || 0))) outbox[scope][hash] = saved;
        // Сохраняем до отправки: закрытие WebView может прервать последний запрос.
        Lampa.Storage.set('lampac_timecode_outbox_v1', outbox, true);
        this.exportRetries = 0;
        this.flushPending();
      }
    }, {
      key: "exportScope",
      value: function exportScope() {
        var account = Lampa.Storage.get('account', '{}');
        return JSON.stringify([server_uid || account.email || unic_id, this.profileId()]);
      }
    }, {
      key: "flushPending",
      value: function flushPending() {
        if (this.exportBusy) return;
        clearTimeout(this.exportRetryTimer);
        var _this6 = this;
        var scope = this.exportScope();
        var outbox = Lampa.Storage.get('lampac_timecode_outbox_v1', {});
        var pending = outbox[scope] || {};
        var snapshots = {};
        var rows = [];
        for (var hash in pending) {
          if (!Object.prototype.hasOwnProperty.call(pending, hash)) continue;
          var road = pending[hash];
          var found = this.identify(hash, road);
          if (!found) {
            if (!this.exportUnknown) this.exportUnknown = {};
            if (!this.exportUnknown[hash]) console.warn('Lampac TimeCode', 'export waiting for card', 'hash=' + hash);
            this.exportUnknown[hash] = true;
            continue;
          }
          snapshots[hash] = JSON.stringify(road);
          rows.push({
            id: found.identity, hash: hash, card: found.card,
            position: road.time || 0, duration: road.duration || 0,
            percent: road.percent || 0, profile: road.profile || 0,
            watched_at: road.updated || 0, extra: timelineExtra(road, found.card)
          });
          if (rows.length >= 200) break;
        }
        if (!rows.length) return;
        var url = this.apiUrl('set');
        this.exportBusy = true;
        var finished = false;
        function finish(ok, status) {
          if (finished) return;
          finished = true;
          _this6.exportBusy = false;
          if (ok) {
            // Пока пакет отправлялся, мог появиться более свежий прогресс.
            var latest = Lampa.Storage.get('lampac_timecode_outbox_v1', {});
            var remaining = latest[scope] || {};
            for (var key in snapshots) {
              if (JSON.stringify(remaining[key]) === snapshots[key]) delete remaining[key];
            }
            if (!Object.keys(remaining).length) delete latest[scope];
            Lampa.Storage.set('lampac_timecode_outbox_v1', latest, true);
            _this6.exportRetries = 0;
            _this6.exportRetryTimer = setTimeout(function() { _this6.flushPending(); }, 200);
          } else {
            console.warn('Lampac TimeCode', 'export failed; retained locally', 'status=' + (status || 0), 'rows=' + rows.length);
            _this6.exportRetries = (_this6.exportRetries || 0) + 1;
            if (_this6.exportScope() !== scope) {
              _this6.exportRetries = 0;
              _this6.exportRetryTimer = setTimeout(function() { _this6.flushPending(); }, 200);
            } else if (_this6.exportRetries <= 2) {
              _this6.exportRetryTimer = setTimeout(function() { _this6.flushPending(); }, _this6.exportRetries * 2000);
            }
          }
        }
        try {
          $.ajax({
            url: url, type: 'POST', contentType: 'application/json', timeout: 10000,
            data: JSON.stringify({ rows: rows }),
            success: function(result) {
              // Сериализатор сервера пропускает нулевые счётчики.
              finish(!!(result && result.success === true && Number(result.accepted || 0) + Number(result.skipped || 0) === rows.length), 200);
            },
            error: function(xhr) { finish(false, xhr && xhr.status); }
          });
        } catch (error) { finish(false, 0); }
      }
    }, {
      /**
       * Карточки, по которым опознаются хеши.
       *
       * `Bookmarks.all()` живёт только в памяти и на свежей загрузке чаще всего пуст: его
       * наполняет `update()`, а зовёт его не каждый экран. Поэтому при первом же непустом ответе
       * складываем компактный индекс в localStorage и дальше опираемся на него — иначе опознание
       * работало бы через раз, в зависимости от того, успели ли приехать закладки.
       */
      key: "cards",
      value: function cards() {
        var pool = [];
        try { pool = pool.concat(Lampa.Account.Bookmarks.all() || []); } catch (e) {}
        pool = pool.concat((Lampa.Storage.get('favorite', {}) || {}).card || []);

        if (pool.length) {
          var typesByID = {};
          pool.forEach(function(card) {
            var id = parseInt(card.id, 10);
            if (!id) return;
            var type = (card.name || card.original_name || card.first_air_date || card.number_of_seasons)
              ? 'tv' : (card.release_date ? 'movie' : null);
            if (!type) return;
            if (!typesByID[id]) typesByID[id] = {};
            typesByID[id][type] = true;
          });

          var index = [];
          var seen = {};
          pool.forEach(function(card) {
            var id = parseInt(card.id, 10);
            if (!id) return;
            var own = (card.name || card.original_name || card.first_air_date || card.number_of_seasons)
              ? 'tv' : (card.release_date ? 'movie' : null);
            // Урезанная закладка несёт только original_title даже у сериала — тип берём из тех
            // записей того же id, где признаки есть.
            var types = own ? [own] : Object.keys(typesByID[id] || {});
            types.forEach(function(type) {
              var original = type === 'tv'
                ? (card.original_name || card.original_title)
                : card.original_title;
              if (!original) return;
              var key = type + id + original;
              if (seen[key]) return;
              seen[key] = true;
              index.push({ i: id, t: type, o: original, s: card.number_of_seasons || 0 });
            });
          });

          if (index.length) Lampa.Storage.set('lampac_timecode_cards', index);
          return index;
        }

        return Lampa.Storage.get('lampac_timecode_cards', []) || [];
      }
    }, {
      /**
       * Чей это хеш.
       *
       * Сначала метаданные Timeline: они не зависят от названия, хеша и загрузки закладок.
       * Затем открытая карточка — она объясняет ЛОКАЛЬНЫЙ просмотр и стоит почти ничего.
       * Если не объяснила, значит отметка пришла извне: кто-то посмотрел на другом устройстве,
       * куб прислал дельту, и к открытой странице она отношения не имеет. Тогда ищем среди
       * закладок — тем же перебором, что и разовый разбор.
       */
      key: "identifyMetadata",
      value: function identifyMetadata(road) {
        if (!road || typeof road.card !== 'string') return null;
        // У других каталогов свои ID: не смешиваем их с общей областью TMDB/CUB.
        var link = /^(?:(?:tmdb|cub)_)?(movie|tv)_([1-9]\d*)$/.exec(road.card);
        if (!link || Number(link[2]) > 9007199254740991) return null;
        var identity = link[1] + '-' + link[2];
        if (link[1] === 'tv') {
          var season = road.season;
          var episode = road.episode;
          if ((typeof season !== 'number' && typeof season !== 'string') ||
              (typeof episode !== 'number' && typeof episode !== 'string')) return null;
          if (!/^\d+$/.test(String(season)) || !/^\d+$/.test(String(episode))) return null;
          season = Number(season);
          episode = Number(episode);
          if (season > 9007199254740991 || episode < 1 || episode > 9007199254740991) return null;
          identity += '-s' + season + 'e' + episode;
        }
        return { identity: identity, card: link[2] + '_' + link[1] };
      }
    }, {
      key: "identify",
      value: function identify(hash, road) {
        var metadata = this.identifyMetadata(road);
        if (metadata) return metadata;
        hash = String(hash);
        // Открытую карточку можно смотреть до её появления в закладках.
        var activity = Lampa.Storage.get('activity', '{}');
        var card = activity.movie || activity.card;
        if (card && /^\d+$/.test(String(card.id || ''))) {
          var type = card.name || card.original_name ? 'tv' : 'movie';
          var original = type === 'tv' ? (card.original_name || card.original_title) : card.original_title;
          if (original) {
            var active = this.match(hash, {i: card.id, t: type, o: original, s: card.number_of_seasons || 0});
            if (active) return active;
          }
        }
        var index = this.cards();
        var revision = JSON.stringify(index);
        if (revision !== this.cardsRevision) {
          this.cardsRevision = revision;
          this.misses = {};
        }
        if (this.misses && this.misses[hash]) return null;
        for (var i = 0; i < index.length; i++) {
          var found = this.match(hash, index[i]);
          if (found) return found;
        }

        // Запоминаем промах: дельты по неизвестному тайтлу иначе перебирали бы закладки заново.
        if (!this.misses) this.misses = {};
        this.misses[hash] = true;
        return null;
      }
    }, {
      /** Объясняет ли эта карточка данный хеш. Какая это серия, знает только сам хеш. */
      key: "match",
      value: function match(hash, entry) {
        var id = entry.i;
        if (!id) return null;

        if (entry.t !== 'tv') {
          return Lampa.Utils.hash(entry.o) === hash
            ? { identity: 'movie-' + id, card: id + '_movie' }
            : null;
        }

        var original = entry.o;
        var seasons = entry.s > 0 ? Math.min(entry.s + 1, 60) : 50;
        for (var season = 0; season <= seasons; season++) {
          for (var episode = 1; episode <= 200; episode++) {
            if (Lampa.Utils.hash([season, season > 10 ? ':' : '', episode, original].join('')) === hash) {
              return { identity: 'tv-' + id + '-s' + season + 'e' + episode, card: id + '_tv' };
            }
          }
        }
        return null;
      }
    }]);
    return Timecode;
  }();

  /**
   * Переезд со случайной идентичности на розданную сервером.
   *
   * До того как сервер начал раздавать uid, браузер придумывал его сам, и накопленное лежит в
   * области, про которую больше никто не спросит. Сервер не знает, какой случайный uid чей, —
   * знает только сам браузер, поэтому переливает он.
   *
   * Один раз: после успеха старый ключ заменяется новым, и условие ниже больше не выполняется.
   */
  function migrateLegacyIdentity() {
    if (!server_uid) return;

    var legacy = Lampa.Storage.get('lampac_unic_id', '');
    if (!legacy || legacy === server_uid) return;

    var token = '{token}';
    var auth = token !== '' ? '&token=' + token : '';
    var base = '{localhost}/timecode/';

    new Lampa.Reguest().silent(base + 'dump?uid=' + encodeURIComponent(legacy) + auth, function(result) {
      var rows = result && result.rows;

      if (!rows || !rows.length) {
        Lampa.Storage.set('lampac_unic_id', server_uid);
        return;
      }

      $.ajax({
        url: base + 'set?uid=' + encodeURIComponent(server_uid) + auth,
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify({ rows: rows }),
        success: function() {
          Lampa.Storage.set('lampac_unic_id', server_uid);
          console.log('Lampac TimeCode', 'migrated ' + rows.length + ' row(s) from ' + legacy);
        }
      });
    }, function() {});
  }

  function startPlugin() {
    window.lampac_timecode_plugin = true;
    migrateLegacyIdentity();
    if (Lampa.Timeline.listener) {
      var code = new Timecode();
      code.init();
    }
  }
  if (!window.lampac_timecode_plugin) startPlugin();

})();
