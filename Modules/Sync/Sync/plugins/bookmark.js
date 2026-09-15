(function(){
    'use strict';

    function whenReady(callback) {
      if (typeof window === 'undefined') return;

      if (Lampa && Lampa.Favorite && Lampa.Storage && Lampa.Arrays && Lampa.Utils) {
        callback();
      } else {
        setTimeout(function() {
          whenReady(callback);
        }, 500);
      }
    }

    whenReady(function(){
        if (window.lampacBookmarkSyncInitialized) return;
        window.lampacBookmarkSyncInitialized = true;

        var host = '{localhost}';
        var syncInProgress = false;

        function buildUrl(path) {
          var url = host + '/bookmark' + path;
          var email = Lampa.Storage.get('account_email');
          if (email) url = Lampa.Utils.addUrlComponent(url, 'account_email=' + encodeURIComponent(email));
          var uid = Lampa.Storage.get('lampac_unic_id', '');
          if (uid) url = Lampa.Utils.addUrlComponent(url, 'uid=' + encodeURIComponent(uid));
          var token = '{token}';
          if (token != '') url = Lampa.Utils.addUrlComponent(url, 'token={token}');
		  var profile_id = Lampa.Storage.get('lampac_profile_id', '');
		  if (profile_id != '') url = Lampa.Utils.addUrlComponent(url, 'profile_id='+profile_id);
          if (window.lwsEvent && window.lwsEvent.connectionId != '') url = Lampa.Utils.addUrlComponent(url, 'connectionId=' + encodeURIComponent(window.lwsEvent.connectionId));
          return url;
        }

        function ajax(method, path, body, callback){
            try{
                var xhr = new XMLHttpRequest();
                xhr.open(method, buildUrl(path), true);
                if (method !== 'GET'){
                    xhr.setRequestHeader('Content-Type', 'application/json;charset=UTF-8');
                }
                xhr.onreadystatechange = function(){
                    if (xhr.readyState === 4){
                        if (callback) callback(xhr.status, xhr.responseText);
						Lampa.Listener.send('lampac', {name: 'bookmark_importСompleted', value: body, path: path});
                    }
                };
                xhr.onerror = function(){
                    if (callback) callback(0, null);
                };
                xhr.send(body ? JSON.stringify(body) : null);
            }
            catch(e){
                if (callback) callback(0, null);
            }
        }

        function sanitizeCard(card){
            if (!card) return null;
            var prepared = card;

            if (Lampa && Lampa.Arrays && Lampa.Utils && Lampa.Utils.clearCard)
                prepared = Lampa.Utils.clearCard(Lampa.Arrays.clone(card));

            return prepared;
        }

        function extractId(card, fallback){
            if (card && typeof card.id !== 'undefined' && card.id !== null) return card.id;
            return typeof fallback !== 'undefined' ? fallback : null;
        }


        function buildLocalBookmarkSetPayload() {
          var raw = localStorage.getItem('favorite');
          if (!raw) return [];

          var fav;
          try {
            fav = JSON.parse(raw) || {};
          } catch (e) {
            fav = {};
          }

          var payload = [];

          Object.keys(fav).forEach(function(key) {
            var val = fav[key];
            if (val == null) return;

            var isArray = Array.isArray(val);
            var isObject = !isArray && typeof val === 'object';

            if ((isArray && val.length > 0) || (isObject && Object.keys(val).length > 0)) {
              payload.push({
                where: key,
                data: val
              });
            }
          });

          return payload;
        }

        var CATEGORIES = ['history','like','watch','wath','book','look','viewed','scheduled','continued','thrown'];

        function readFavorite() {
          var raw = localStorage.getItem('favorite');
          if (!raw) return {};
          try { return JSON.parse(raw) || {}; } catch (e) { return {}; }
        }

        function ensureArrays(fav) {
          if (!Array.isArray(fav.card)) fav.card = [];
          CATEGORIES.forEach(function(category) {
            if (!Array.isArray(fav[category])) fav[category] = [];
          });
        }

        function indexOfId(list, id) {
          for (var i = 0; i < list.length; i++) {
            if (String(list[i]) === String(id)) return i;
          }
          return -1;
        }

        function cursor() { return Lampa.Storage.get('lampac_bookmark_version', '0'); }
        function setCursor(value) { Lampa.Storage.set('lampac_bookmark_version', String(value || 0)); }

        /**
         * Сверка идёт дельтами: сервер держит строку на карточку и курсор, и присылает только то,
         * что менялось. Полный список тянем лишь когда курсора ещё нет.
         */
        function pullFromServer() {
          if (syncInProgress) return;
          syncInProgress = true;

          var since = cursor();
          var full = !since || since === '0';

          ajax('GET', full ? '/dump' : '/changelog?since=' + encodeURIComponent(since), null, function(status, response) {
            if (status != 200 || !response) { syncInProgress = false; return; }

            var data;
            try { data = JSON.parse(response); } catch (e) { syncInProgress = false; return; }

            var rows = (data && data.rows) || [];

            // Пустой сервер при первой сверке — наполняем его тем, что накопилось локально.
            if (full && rows.length === 0) {
              var seed = buildLocalBookmarkSetPayload();
              if (seed.length > 0) ajax('POST', '/set', seed);
              if (data && data.version) setCursor(data.version);
              syncInProgress = false;
              return;
            }

            applyRows(rows, full);
            if (data && data.version) setCursor(data.version);
            syncInProgress = false;
          });
        }

        /**
         * Строки сервера → локальный `favorite` СЛИЯНИЕМ, а не заменой.
         *
         * Замена целиком была причиной, по которой плагин молчал при включённом кубе: он затирал
         * бы кубовый список своим. Слияние трогает только те карточки, о которых сервер сказал,
         * поэтому оба источника уживаются.
         */
        function applyRows(rows, full) {
          if (!rows.length) return;

          var fav = readFavorite();
          ensureArrays(fav);

          var order = {};
          var changed = false;

          rows.forEach(function(row) {
            var id = row.id;
            if (!id) return;

            var categories = row.categories || {};
            var names = Object.keys(categories);

            if (row.card && names.length) {
              var at = indexOfId(fav.card.map(function(c) { return c && c.id; }), id);
              if (at < 0) { fav.card.unshift(row.card); changed = true; }
              else if (JSON.stringify(fav.card[at]) !== JSON.stringify(row.card)) { fav.card[at] = row.card; changed = true; }
            }

            CATEGORIES.forEach(function(category) {
              var at = indexOfId(fav[category], id);
              var wanted = categories.hasOwnProperty(category);

              if (wanted && at < 0) {
                fav[category].unshift(isNaN(id) ? id : Number(id));
                changed = true;
              }
              else if (!wanted && at >= 0) {
                fav[category].splice(at, 1);
                changed = true;
              }

              if (wanted) {
                if (!order[category]) order[category] = {};
                order[category][String(id)] = categories[category];
              }
            });

            // Пустые категории — карточку убрали отовсюду, держать её описание больше незачем.
            if (!names.length) {
              var card = indexOfId(fav.card.map(function(c) { return c && c.id; }), id);
              if (card >= 0) { fav.card.splice(card, 1); changed = true; }
            }
          });

          // Порядок сервер знает точно только когда прислал всё: у дельты на руках лишь часть
          // списка, и сортировать по ней значит перемешать остальное.
          if (full) {
            CATEGORIES.forEach(function(category) {
              var keys = order[category];
              if (!keys) return;
              fav[category].sort(function(a, b) { return (keys[String(b)] || 0) - (keys[String(a)] || 0); });
            });
            changed = true;
          }

          if (!changed) return;

          // Через Storage, а не напрямую в localStorage: у Lampa есть свой кеш прочитанного,
          // и запись мимо него оставляет интерфейс на старых данных до перезагрузки.
          Lampa.Storage.set('favorite', fav);
          if (Lampa.Favorite.read) Lampa.Favorite.read(true);
          else Lampa.Favorite.init();
        }

        function sendAdd(action, event){
            if (!event || !event.card) return;

            var id = extractId(event.card, event.id);
            if (id === null || typeof id === 'undefined') return;

            var payload = {
                where: event.where || '',
                card: sanitizeCard(event.card),
                card_id: id,
                id: id
            };

            ajax('POST', action, payload);
        }

        function sendAdded(event){
            sendAdd('/added', event);
        }

        function sendNew(event){
            sendAdd('/add', event);
        }

        function sendRemove(event){
            if (!event) return;

            var card = event.card || null;
            var id = extractId(card, event.id);
            if (id === null || typeof id === 'undefined') return;

            var payload = {
                where: event.where || '',
                method: event.method || 'card',
                card_id: id,
                id: id
            };

            if (card) payload.card = sanitizeCard(card);

            ajax('POST', '/remove', payload);
        }

        function bindEvents(){
            var favorite = Lampa && Lampa.Favorite ? Lampa.Favorite : null;
            if (!favorite || !favorite.listener || !favorite.listener.follow) return;

            favorite.listener.follow('add', function(event){
                if (event.card.received != true) sendNew(event);
            });

            favorite.listener.follow('added', function(event){
                if (event.card.received != true) sendAdded(event);
            });

            favorite.listener.follow('remove', function(event){
                if (event.card.received != true) sendRemove(event);
            });
        }

        bindEvents();
        pullFromServer();
		
        document.addEventListener('lwsEvent', function(evnt) {
          if (evnt.detail.name == 'bookmark'){
            var ob = JSON.parse(evnt.detail.data);
			if (ob.profile_id && ob.profile_id != '' && Lampa.Storage.get('lampac_profile_id', '') != ob.profile_id)
				return;
			if (ob.type == 'set') {
				setFavoriteField(ob.data);
				return;
			}
			// Нативный клиент прислал строки: что именно — знает только сервер, идём за дельтой.
			if (ob.type == 'sync') {
				pullFromServer();
				return;
			}
			if (ob.type != 'add' && ob.type != 'added' && ob.type != 'remove')
				return;
			var applyMethod = (ob.type == 'remove') ? 'remove' : 'add';
			var data = ob.data;
			if (Array.isArray(data)) {
			  data.forEach(function(item) {
			    if (item && item.card) {
			      item.card.received = true;
			      Lampa.Favorite[applyMethod](item.where, item.card);
			    }
			  });
			} else if (data && data.card) {
			  data.card.received = true;
			  Lampa.Favorite[applyMethod](data.where, data.card);
			}
          }
        });
		
        var open_time = Date.now();
        var minutes = 10;
        document.addEventListener('visibilitychange', function() {
          if (Date.now() - open_time > (1000 * 60 * minutes)) {
            pullFromServer();
          }
          open_time = Date.now();
        });
		
        Lampa.Listener.follow('lampac', function(e) {
          if (e.name == 'bookmark_set') {
            setFavoriteField(e.value);
			ajax('POST', '/set', e.value);
          }
          else if (e.name == 'bookmark_pullFromServer'){
            pullFromServer();
          }
        });


        function setFavoriteField(ob) {
          // Прочитать текущее значение
          var raw = localStorage.getItem('favorite');
          var fav = {};
          if (raw) {
            try {
              fav = JSON.parse(raw) || {};
            } catch (e) {
              fav = {};
            }
          }

          // Нормализуем во входной массив
          var isArray = Object.prototype.toString.call(ob) === '[object Array]';
          var items = isArray ? ob : [ob];

          for (var i = 0; i < items.length; i++) {
            var it = items[i] || {};
            var where = it.where;

            if (typeof where !== 'string' || !where) continue; // пропуск некорректных ключей

            var data = it.data;

            // Если data строка и это JSON — распарсим, чтобы не хранить строковый JSON
            if (typeof data === 'string') {
              var trimmed = data.replace(/^\s+|\s+$/g, '');
              if ((trimmed.charAt(0) === '{' && trimmed.charAt(trimmed.length - 1) === '}') ||
                (trimmed.charAt(0) === '[' && trimmed.charAt(trimmed.length - 1) === ']')) {
                try {
                  data = JSON.parse(trimmed);
                } catch (e) {
                  /* оставим как есть */ }
              }
            }

            fav[where] = data;
          }

          try {
            localStorage.setItem('favorite', JSON.stringify(fav));
          } catch (e) { }
        }

    });
})();