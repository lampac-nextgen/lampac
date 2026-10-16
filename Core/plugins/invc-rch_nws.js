function getAndroidVersion() {
  if (Lampa.Platform.is('android')) {
    try {
      var current = AndroidJS.appVersion().split('-');
      return parseInt(current.pop());
    } catch (e) {
      return 0;
    }
  } else {
    return 0;
  }
}

var hostkey = '{localhost}'.replace('http://', '').replace('https://', '');

if (!window.rch_nws || !window.rch_nws[hostkey]) {
  if (!window.rch_nws) window.rch_nws = {};

  window.rch_nws[hostkey] = {
    type: Lampa.Platform.is('android') ? 'apk' : Lampa.Platform.is('tizen') ? 'cors' : undefined,
    startTypeInvoke: false,
    rchRegistry: false,
    apkVersion: getAndroidVersion()
  };
}

window.rch_nws[hostkey].typeInvoke = function rchtypeInvoke(host, call) {
  if (!window.rch_nws[hostkey].startTypeInvoke) {
    window.rch_nws[hostkey].startTypeInvoke = true;

    var check = function check(good) {
      window.rch_nws[hostkey].type = Lampa.Platform.is('android') ? 'apk' : good ? 'cors' : 'web';
      call();
    };

    if (Lampa.Platform.is('android') || Lampa.Platform.is('tizen')) check(true);
    else {
      var net = new Lampa.Reguest();
      net.silent('{localhost}'.indexOf(location.host) >= 0 ? 'https://github.com/' : host + '/cors/check', function() {
        check(true);
      }, function() {
        check(false);
      }, false, {
        dataType: 'text'
      });
    }
  } else call();
};

window.rch_nws[hostkey].Registry = function RchRegistry(client, startConnection) {
  window.rch_nws[hostkey].typeInvoke('{localhost}', function() {

    client.invoke("RchRegistry", {
      host: location.host,
      rchtype: Lampa.Platform.is('android') ? 'apk' : Lampa.Platform.is('tizen') ? 'cors' : (window.rch_nws[hostkey].type || 'web'),
      apkVersion: Lampa.Platform.is('android') ? (window.rch_nws[hostkey].apkVersion || 0) : 0,
      player: Lampa.Storage.field('player')
    });

    if (window.rch_nws[hostkey].rchRegistry)
      return;

    window.rch_nws[hostkey].rchRegistry = true;

    var handled = false;
    client.on('RchRegistry', function (clientIp, connectionId, rchtype) {
      if (startConnection && !handled) {
	    handled = true;
	    startConnection();
      }
    });

    client.on("RchClient", function(rchId, url, data, headers, returnHeaders) {
      var network = new Lampa.Reguest();
	  
	  function sendResult(uri, html) {
	    $.ajax({
	      url: '{localhost}/rch/' + uri + '?id=' + rchId,
	      type: 'POST',
	      data: html,
	      async: true,
	      cache: false,
	      contentType: false,
	      processData: false,
	      success: function(j) {},
	      error: function() {
	        client.invoke("RchResult", rchId, '');
	      }
	    });
	  }

      function result(html) {
        if (Lampa.Arrays.isObject(html) || Lampa.Arrays.isArray(html)) {
          html = JSON.stringify(html);
        }

        if (typeof CompressionStream !== 'undefined' && html && html.length > 1000) {
          var compressionStream = new CompressionStream('gzip');
          var encoder = new TextEncoder();
          var readable = new ReadableStream({
            start: function(controller) {
              controller.enqueue(encoder.encode(html));
              controller.close();
            }
          });
          var compressedStream = readable.pipeThrough(compressionStream);
          new Response(compressedStream).arrayBuffer()
            .then(function(compressedBuffer) {
              var compressedArray = new Uint8Array(compressedBuffer);
              if (compressedArray.length > html.length) {
                sendResult('result', html);
              } else {
                sendResult('gzresult', compressedArray);
              }
            })
            .catch(function() {
              sendResult('result', html);
            });

        } else {
          sendResult('result', html);
        }
      }

      if (url == 'eval' || url == 'evalrun') {
        result('');
      } else if (url == 'ping') {
        result('pong');
      } else {
        console.log('RCH', url);
        network["native"](url, result, function(e) {
          console.log('RCH', 'result empty, ' + e.status);
          result('');
        }, data, {
          dataType: 'text',
          timeout: 1000 * 8,
          headers: headers,
          returnHeaders: returnHeaders
        });
      }
    });

    client.on('Connected', function(connectionId) {
      console.log('RCH', 'ConnectionId: ' + connectionId);
      window.rch_nws[hostkey].connectionId = connectionId;
    });
    client.on('Closed', function() {
      console.log('RCH', 'Connection closed');
    });
    client.on('Error', function(err) {
      console.log('RCH', 'error:', err);
    });
  });
};
