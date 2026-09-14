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

        // Закладки приезжают не мгновенно: даём синхронизации куба осесть.
        setTimeout(function() { _this.migrate(); }, 15000);
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
      key: "filename",
      value: function filename() {
        var acc = Lampa.Storage.get('account', '{}');
        var name = 'file_view' + (acc.profile ? '_' + acc.profile.id : '');
        if (window.localStorage.getItem(name) === null && acc.profile) {
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
       * Опознать отметки, которые лежат локально без идентичности.
       *
       * Кубовый дамп приходит одними хешами и оседает в localStorage мимо Timeline.update, так что
       * до сервера не доезжает вовсе. Хеш необратим, но обратимо обратное: у закладок есть карточки
       * с оригинальными названиями, а хеш считается ровно из них. Перебираем закладки, считаем их
       * хеши и смотрим, какие из них лежат в file_view.
       *
       * Претендент принимается, только если он единственный. Один хеш достаётся двум одноимённым
       * тайтлам, а `movie/20993` и `tv/20993` — это вообще разные произведения, поэтому выдуманная
       * идентичность хуже отсутствующей.
       */
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
      key: "resolve",
      value: function resolve(known) {
        var viewed = Lampa.Storage.cache(this.filename(), 10000, {});
        var cards = (Lampa.Storage.get('favorite', {}) || {}).card || [];
        if (!cards.length) return;

        var claims = {};

        function claim(hash, identity, card) {
          if (!viewed[hash]) return;
          if (!claims[hash]) claims[hash] = [];
          for (var i = 0; i < claims[hash].length; i++) {
            if (claims[hash][i].identity === identity) return;
          }
          claims[hash].push({ identity: identity, card: card });
        }

        cards.forEach(function(card) {
          var id = parseInt(card.id, 10);
          if (!id) return;

          // Тип определяем так же, как сама Lampa: у сериала есть name, у фильма — title.
          var serial = !!card.name;
          var original = serial
            ? (card.original_name || card.original_title)
            : (card.original_title || card.original_name);
          if (!original) return;

          if (serial) {
            for (var season = 0; season <= 50; season++) {
              for (var episode = 1; episode <= 200; episode++) {
                claim(
                  Lampa.Utils.hash([season, season > 10 ? ':' : '', episode, original].join('')),
                  'tv-' + id + '-s' + season + 'e' + episode,
                  id + '_tv'
                );
              }
            }
          }
          else claim(Lampa.Utils.hash(original), 'movie-' + id, id + '_movie');
        });

        var rows = [];

        for (var hash in claims) {
          // У сервера уже есть идентичность — чужую версию не навязываем.
          if (known[hash] && known[hash].id) continue;
          if (claims[hash].length !== 1) continue;

          var found = claims[hash][0];
          // Сервер знает строку, но без идентичности: шлём ЕГО значения, иначе запись со старым
          // штампом будет отброшена целиком и идентичности так и не получит.
          var source = known[hash] || {
            position: viewed[hash].time || 0,
            duration: viewed[hash].duration || 0,
            percent: viewed[hash].percent || 0,
            watched_at: viewed[hash].updated || 0
          };

          // Пустая отметка на той стороне прочтётся как сброс таймлайна.
          if (!(source.percent > 0) && !(source.position > 1)) continue;

          rows.push({
            id: found.identity,
            hash: hash,
            card: found.card,
            position: source.position || 0,
            duration: source.duration || 0,
            percent: source.percent || 0,
            watched_at: source.watched_at || 0
          });
        }

        if (!rows.length) return;

        console.log('Lampac TimeCode', 'resolved ' + rows.length + ' of ' + Object.keys(claims).length + ' claimed hashes');

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
      key: "add",
      value: function add(e) {
        var url = this.url('add');
        this.network.silent(url, false, false, {
          id: e.data.hash,
          data: JSON.stringify(e.data.road)
        });
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