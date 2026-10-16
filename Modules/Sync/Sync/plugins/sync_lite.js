(function() {
  'use strict';

  var suid = '';
  var rchtype;
  var hubConnection;
  var network = new Lampa.Reguest();

  function account(url) {
    url = url + '';
    if (url.indexOf('account_email=') == -1) {
      var email = Lampa.Storage.get('account_email');
      if (email) url = Lampa.Utils.addUrlComponent(url, 'account_email=' + encodeURIComponent(email));
    }
    if (url.indexOf('uid=') == -1) {
      var uid = Lampa.Storage.get('device_uid', '');
      if (uid) url = Lampa.Utils.addUrlComponent(url, 'uid=' + encodeURIComponent(uid));
    }
    if (url.indexOf('token=') == -1) {
      var token = '{token}';
      if (token != '') url = Lampa.Utils.addUrlComponent(url, 'token={token}');
    }
    return url;
  }

  
  function rchtypeInvoke(call) {
    if (rchtype == undefined) {
      var check = function check(good) {
        rchtype = Lampa.Platform.is('android') ? 'apk' : good ? 'cors' : 'web';
		window.rchtype = rchtype;
        call();
      };

      if (Lampa.Platform.is('android') || Lampa.Platform.is('tizen')) check(true);
      else {
        var net = new Lampa.Reguest();
        net.silent('{localhost}'.indexOf(location.host) >= 0 ? 'https://github.com/' : '{localhost}/cors/check', function() {
          check(true);
        }, function() {
          check(false);
        }, false, {
          dataType: 'text'
        });
      }
    } else call();
  }

  function goExport(path) {
    var value = {};

    if (path == 'sync_view') {
      ['file_view', 'torrents_view'].forEach(function(field) {
        value[field] = localStorage.getItem(field) || '';
      });
    } else {
      value.favorite = localStorage.getItem('favorite') || '';
    }

    $.ajax({
      url: account('{localhost}/storage/set?path='+path),
      type: 'POST',
      data: JSON.stringify(value),
      async: true,
      cache: false,
      contentType: false,
      processData: false,
      success: function(j) {
        if (j.success && j.fileInfo) {
          localStorage.setItem('lampac_' + path, j.fileInfo.changeTime);
		  
          if (hubConnection)
            hubConnection.invoke("events", suid, 'sync', path);
        }
      },
      error: function() {
        console.log('Lampac Storage', 'export', 'error');
      }
    });
  }

  function goImport(path) {
    network.silent(account('{localhost}/storage/get?path=' + path), function(j) {
      if (j.success && j.fileInfo && j.data) {
        if (j.fileInfo.changeTime != Lampa.Storage.get('lampac_' + path, '0')) {
          var data = JSON.parse(j.data);
          for (var i in data) {
            Lampa.Storage.set(i, data[i], true);
          }
          localStorage.setItem('lampac_' + path, j.fileInfo.changeTime);
        }
      } else if (j.msg && j.msg == 'outFile') {
        goExport(path);
      }
    });
  }

  goImport('sync_favorite');
  goImport('sync_view');

  Lampa.Storage.listener.follow('change', function(e) {
    if (e.name == 'favorite' || (e.name.indexOf('events') >= 0 && e.value.length == 0)) {
      goExport(e.name == 'favorite' ? 'sync_favorite' : 'sync_view');
    }
  });

  function waitEvent() {
    hubConnection = new signalR.HubConnectionBuilder().withUrl('{localhost}/ws').build();

    function tryConnect() {
      hubConnection.start().then(function() {
        hubConnection.invoke("RegistryEvent", suid).then(function() {
          hubConnection.on("event", function(uid, name, data) {
            if (name === 'sync') {
              goImport(data);
            }
          });
        });
		rchtypeInvoke(function() {
			hubConnection.invoke("RchRegistry", JSON.stringify({version: 138, host: location.host, rchtype: rchtype})).then(function() {
			  hubConnection.on("RchClient", function(rchId, url, data, headers, returnHeaders) {
				function result(html) {
				  if (Lampa.Arrays.isObject(html) || Lampa.Arrays.isArray(html)) html = JSON.stringify(html);
				  network.silent('{localhost}/rch/result', false, false, {
					id: rchId,
					value: html
				  }, {
					dataType: 'text',
					timeout: 1000 * 5
				  });
				}
				if (url == 'eval' || url == 'evalrun') {
				  result('');
				} else {
				  network["native"](url, result, function() {
					result('');
				  }, data, {
					dataType: 'text',
					timeout: 1000 * 5,
					headers: headers,
					returnHeaders: returnHeaders
				  });

				}
			  });
			});
		});
        hubConnection.onclose(function() {
          waitEvent();
        });
      }).catch(function(err) {
        setTimeout(function() {
          if(hubConnection.state !== 'Connected') {
			goImport('sync_favorite');
			goImport('sync_view');
			tryConnect();
          }
        }, 5000);
      });
    }

    tryConnect();
  }


  network.silent(account('{localhost}/reqinfo'), function(j) {
    if (j.user_uid) {
      suid = j.user_uid;
      if (typeof signalR == 'undefined') {
        Lampa.Utils.putScript(["{localhost}/signalr-6.0.25_es5.js"], function() {
          waitEvent();
        });
      } else waitEvent();
    }
  });

})();