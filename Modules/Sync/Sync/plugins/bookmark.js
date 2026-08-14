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

        function pullFromServer() {
          if (syncInProgress || Lampa.Storage.field('account_use')) return;
          syncInProgress = true;

          ajax('GET', '/list', null, function(status, response) {
            if (status == 200 && response) {
              try {
                var data = JSON.parse(response);

                // Проверяем флаг инициализации базы
                if (data && data.dbInNotInitialization === true) {
                  var seed = buildLocalBookmarkSetPayload();

                  if (seed.length > 0) {
                    ajax('POST', '/set', seed, function(pstStatus) {
                      // После отправки — повторный GET /list
                      ajax('GET', '/list', null, function(st2, resp2) {
                        if (st2 == 200 && resp2) {
                          try {
                            var data2 = JSON.parse(resp2);
                            if (data2 && typeof data2 === 'object') {
                              applyBookmarks(data2);
                            }
                          } catch (e) {}
                        }
                        syncInProgress = false;
                      });
                    });
                    return; // ждём повторного GET
                  }

                  // Если нечего отправлять
				  syncInProgress = false;
                  return;
                }

                // Обычная загрузка
                if (data && typeof data === 'object') {
                  applyBookmarks(data);
                }
              } catch (e) {}
            }

            syncInProgress = false;
          });
        }


        function applyBookmarks(data) {
          Lampa.Storage.set('favorite', data);
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
          if (Lampa.Storage.field('account_use')) return;
          if (evnt.detail.name == 'bookmark'){
            var ob = JSON.parse(evnt.detail.data);
			if (ob.profile_id && ob.profile_id != '' && Lampa.Storage.get('lampac_profile_id', '') != ob.profile_id)
				return;
			if (ob.type == 'set') {
				setFavoriteField(ob.data);
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