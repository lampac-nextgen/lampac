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