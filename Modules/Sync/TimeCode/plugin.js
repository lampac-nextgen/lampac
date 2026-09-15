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
        Lampa.Listener.follow('full', function(e) {
          if (e.type == 'complite') _this.update();
        });
		Lampa.Listener.follow('lampac', function(e) {
          if (e.type == 'timecode_pullFromServer') _this.update();
        });
        // Шина синхронизации: сервер рассылает 'timecode' всем, кроме автора записи,
        // поэтому своё же эхо здесь не приходит и цикла записи нет.
        document.addEventListener('lwsEvent', function(e) {
          if (e.detail && e.detail.name == 'timecode') _this.update();
        });

        _this.awaitBookmarks(0);

        // Список закладок доступен только в узком окне сразу после входа и нигде не сохраняется.
        // Поэтому ждём не только на старте: вход может случиться сильно позже.
        Lampa.Storage.listener.follow('change', function(e) {
          if (e.name == 'account' || e.name == 'account_bookmarks') {
            window.lampac_timecode_migrated = false;
            _this.awaitBookmarks(0);
          }
        });
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
		
		var profile_id = Lampa.Storage.get('lampac_profile_id', '');
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

        var profile_id = Lampa.Storage.get('lampac_profile_id', '');
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

        if (ready) return this.migrate();
        if (attempt > 30) return;

        // Никто не запросил — запрашиваем сами, но один раз.
        if (attempt === 2) { try { Lampa.Account.Bookmarks.update(); } catch (e) {} }

        setTimeout(function() { _this5.awaitBookmarks(attempt + 1); }, 2000);
      }
    }, {
      key: "migrate",
      value: function migrate() {
        var _this4 = this;
        if (window.lampac_timecode_migrated) return;
        window.lampac_timecode_migrated = true;

        this.network.silent(this.apiUrl('dump'), function(dump) {
          var known = {};
          ((dump && dump.rows) || []).forEach(function(row) {
            if (row.hash) known[row.hash] = row;
          });
          _this4.resolve(known);
        }, function() {});
      }
    }, {
      /**
       * Опознать отметки, которые лежат локально без идентичности.
       *
       * Кубовый дамп приходит одними хешами и оседает в localStorage мимо Timeline.update, так что
       * до сервера не доезжает вовсе. Хеш необратим, но обратимо обратное: у закладок есть карточки
       * с оригинальными названиями, а хеш считается ровно из них. Перебираем закладки, считаем их
       * хеши и смотрим, какие из них лежат в file_view.
       */
      key: "resolve",
      value: function resolve(known) {
        var viewed = Lampa.Storage.cache(this.filename(), 10000, {});
        var index = this.cards();
        if (!index.length) return;

        var claims = {};

        function claim(hash, identity, card) {
          if (!viewed[hash]) return;
          if (!claims[hash]) claims[hash] = [];
          for (var i = 0; i < claims[hash].length; i++) {
            if (claims[hash][i].identity === identity) return;
          }
          claims[hash].push({ identity: identity, card: card });
        }

        index.forEach(function(entry) {
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
          if (known[hash] && known[hash].id) continue;

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
              watched_at: source.watched_at || 0
            });
          });
        }

        console.log('Lampac TimeCode', 'cards=' + index.length,
          'local=' + Object.keys(viewed).length, 'claimed=' + Object.keys(claims).length,
          'shared=' + ambiguous, 'rows=' + rows.length);

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
      /**
       * Записать отметку.
       *
       * Карточку берём не из текущей активности: дельта из куба прилетает фоном, когда открыта
       * Главная, и строка легла бы под `0_movie`. Пишем только тогда, когда карточка установлена —
       * либо открытая страница объясняет этот хеш, либо он уже опознан разбором. Всё остальное
       * лежит в localStorage и доедет следующим разбором, так что ничего не теряется.
       */
      key: "add",
      value: function add(e) {
        var hash = e.data.hash;
        var road = e.data.road || {};
        var found = this.identify(hash);
        if (!found) return;

        $.ajax({
          url: this.apiUrl('set'),
          type: 'POST',
          contentType: 'application/json',
          data: JSON.stringify({ rows: [{
            id: found.identity,
            hash: hash,
            card: found.card,
            position: road.time || 0,
            duration: road.duration || 0,
            percent: road.percent || 0,
            watched_at: road.updated || 0
          }] })
        });
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
       * Сначала открытая карточка — она объясняет ЛОКАЛЬНЫЙ просмотр и стоит почти ничего.
       * Если не объяснила, значит отметка пришла извне: кто-то посмотрел на другом устройстве,
       * куб прислал дельту, и к открытой странице она отношения не имеет. Тогда ищем среди
       * закладок — тем же перебором, что и разовый разбор.
       */
      key: "identify",
      value: function identify(hash) {
        if (this.misses && this.misses[hash]) return null;

        var index = this.cards();
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