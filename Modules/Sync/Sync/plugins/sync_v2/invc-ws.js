(function() {
  'use strict';

    /* Слушать события
      document.addEventListener('lwsEvent', function(e) {
        console.log(e.detail);
		// e.detail.name
		// e.detail.data
      }); 
	*/

  {invc-rch_nws}

  var nwsClient;
  
  window.lwsEvent = {
    uid: '', 
	connectionId: '',
	init: false
  };
  
  window.lwsEvent.send = function hubEvnt(name, data) {
    nwsClient.invoke("events", window.lwsEvent.uid, name, data);
  };
  
  window.lwsEvent.sendId = function hubEvnt(connectionId, name, data) {
    nwsClient.invoke("eventsId", connectionId, window.lwsEvent.uid, name, data);
  };

  function sendEvent(name, data) {
    var hubEvents = document.createEvent('CustomEvent');
    hubEvents.initCustomEvent('lwsEvent', true, true, {
      uid: window.lwsEvent.uid,
      name: name,
      data: data
    });

    document.dispatchEvent(hubEvents);
  }


  function account(url) {
    url = url + '';
    if (url.indexOf('account_email=') == -1) {
      var email = Lampa.Storage.get('account_email');
      if (email) url = Lampa.Utils.addUrlComponent(url, 'account_email=' + encodeURIComponent(email));
    }
    if (url.indexOf('uid=') == -1) {
      var uid = Lampa.Storage.get('lampac_unic_id', '');
      if (uid) url = Lampa.Utils.addUrlComponent(url, 'uid=' + encodeURIComponent(uid));
    }
    if (url.indexOf('token=') == -1) {
      var token = '{token}';
      if (token != '') url = Lampa.Utils.addUrlComponent(url, 'token={token}');
    }
    return url;
  }


  function waitEvent() {
    if (!window.nwsClient) window.nwsClient = {};
    else if (window.nwsClient[hostkey] && window.nwsClient[hostkey].socket)
      window.nwsClient[hostkey].socket.close();

    window.nwsClient[hostkey] = new NativeWsClient('{localhost}/nws', {
      autoReconnect: true
    });

    nwsClient = window.nwsClient[hostkey];

    nwsClient.on('Connected', function(connectionId) {
      window.lwsEvent.connectionId = connectionId;
	  nwsClient.invoke("RegistryEvent", window.lwsEvent.uid);
      window.rch_nws[hostkey].Registry(nwsClient);
	  window.rch_nws[hostkey].connectionId = connectionId;
      sendEvent('system', 'connected');
    });

    nwsClient.on("event", function(uid, name, data) {
      sendEvent(name, data);
    });

    nwsClient.connect();
  }


  function start(j) {
    window.reqinfo = j;
    window.lwsEvent.init = true;
    window.lwsEvent.uid = j.user_uid;
    if (typeof NativeWsClient == 'undefined') {
        Lampa.Utils.putScript(["{localhost}/js/nws-client-es5.js?v21042026"], function() {}, false, function() {
        waitEvent();
      }, true);
    } else waitEvent();
  }
  
  
  if (!window.lwsEvent.init) {
    if (!window.reqinfo) {
      var network = new Lampa.Reguest();
      network.silent(account('{localhost}/reqinfo'), function(j) {
        if (j.user_uid)
          start(j);
      });
    } 
    else
      start(window.reqinfo);
  }

})();