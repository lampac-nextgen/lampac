(function () {
  'use strict';

  var unic_id = Lampa.Storage.get('lampac_unic_id', '');
  if (!unic_id) {
    unic_id = Lampa.Utils.uid(8).toLowerCase();
    Lampa.Storage.set('lampac_unic_id', unic_id);
  }

  // The main link is only ours while the user hasn't replaced it with their own server.
  var ts_url = '{tshost}/ts';
  var cur_url = Lampa.Storage.get('torrserver_url', '');
  if (!cur_url || cur_url == ts_url || cur_url == Lampa.Storage.get('lampac_torrserver_url', '')) {
    Lampa.Storage.set('torrserver_url', ts_url);
    Lampa.Storage.set('lampac_torrserver_url', ts_url);
  }

  function ownServer(value, server) {
    try {
      var own = new URL(Lampa.Utils.checkEmptyUrl(server || ts_url));
      var selected = new URL(Lampa.Utils.checkEmptyUrl(value));
      return selected.origin == own.origin && !selected.username && !selected.password && !selected.search && !selected.hash &&
        selected.pathname.replace(/\/+$/, '') == own.pathname.replace(/\/+$/, '');
    }
    catch (e) { return false; }
  }

  function selectedOwn() {
    var one = Lampa.Storage.get('torrserver_url', '');
    var two = Lampa.Storage.get('torrserver_url_two', '');
    return ownServer(Lampa.Storage.field('torrserver_use_link') == 'two' ? two || one : one || two, state.server);
  }

  function readAuth() {
    return { auth: Lampa.Storage.get('torrserver_auth', 'false'), login: Lampa.Storage.get('torrserver_login', ''), password: Lampa.Storage.get('torrserver_password', '') };
  }

  function writeAuth(auth) {
    Lampa.Storage.set('torrserver_auth', auth.auth, true);
    Lampa.Storage.set('torrserver_login', auth.login, true);
    Lampa.Storage.set('torrserver_password', auth.password, true);
  }

  function nativeAuth() {
    return { auth: 'true', login: state.login || '', password: state.password };
  }

  var state = Lampa.Storage.listener.send.lampacMediaAuth;
  if (!state) {
    state = {};
    state.server = ts_url;
    state.own = selectedOwn();
    // With own selected at startup, the common settings may already contain a native secret.
    state.external = state.own ? { auth: 'false', login: '', password: '' } : readAuth();
    var originalSend = Lampa.Storage.listener.send;
    Lampa.Storage.listener.send = function (type, event) {
      if (!state.enabled || type != 'change' || !event ||
          !/^(torrserver_url|torrserver_url_two|torrserver_use_link)$/.test(event.name))
        return originalSend.apply(this, arguments);

      var own = selectedOwn();
      if (own && !state.own) {
        state.external = readAuth();
        writeAuth(nativeAuth());
      }
      else if (!own && state.own) {
        state.login = Lampa.Storage.get('torrserver_login', '') || state.login;
        writeAuth(state.external);
      }
      else if (own) state.login = Lampa.Storage.get('torrserver_login', '') || state.login;
      state.own = own;

      // Storage writes before send(). Pinned ServiceTorserver's earlier listener immediately
      // probes the changed URL (even an unselected link), reading these common Basic fields.
      var selectedAuth = readAuth();
      if (event.name != 'torrserver_use_link')
        writeAuth(ownServer(event.value, state.server) ? nativeAuth() : state.external);
      try { return originalSend.apply(this, arguments); }
      finally { writeAuth(selectedAuth); }
    };
    Lampa.Storage.listener.send.lampacMediaAuth = state;
  }
  state.enabled = {accsdb};
  state.server = ts_url;
  state.password = '{defaultPasswd}';

  function setAuth(login) {
    // Retain the native identity even while an external server owns the common Basic fields.
    if (!login) return;
    state.login = login;
    if (selectedOwn()) writeAuth(nativeAuth());
  }

  // Cover native and external-player URLs at Lampa's exported URL producer, including TS GST.
  if (Lampa.Torserver && typeof Lampa.Torserver.stream == 'function') {
    if (!Lampa.Torserver.stream.lampacMediaAuth) {
      var originalStream = Lampa.Torserver.stream;
      Lampa.Torserver.stream = function () {
        var result = originalStream.apply(this, arguments);
        if (!state.enabled) return result;
        try {
          var own = new URL(Lampa.Utils.checkEmptyUrl(state.server));
          var one = Lampa.Storage.get('torrserver_url', '');
          var two = Lampa.Storage.get('torrserver_url_two', '');
          var selected = Lampa.Storage.field('torrserver_use_link') == 'two' ? two || one : one || two;
          var media = new URL(result);
          var base = own.pathname.replace(/\/$/, '');
          if (!ownServer(selected, state.server) ||
              media.origin != own.origin || media.username || media.password ||
              !new RegExp('^' + base.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '/+(stream|gst)(/|$)').test(media.pathname))
            return result;

          var login = Lampa.Storage.get('torrserver_login', '');
          if (!login) return result;
          var identities = [];
          media.searchParams.forEach(function (value, key) {
            if (/^(uid|token|account_email|box_mac|lcrqpasswd)$/i.test(key)) identities.push(key);
          });
          identities.forEach(function (key) { media.searchParams.delete(key); });
          // addUrlComponent keeps the native player's URL shape; fragments must remain last.
          var fragment = media.hash;
          media.hash = '';
          return Lampa.Utils.addUrlComponent(media.href, 'uid=' + encodeURIComponent(login)) + fragment;
        }
        catch (e) { return result; }
      };
      Lampa.Torserver.stream.lampacMediaAuth = state;
    }
  }

  if ('{token}' != '') {
    setAuth('{token}');
  }
  else if (window.reqinfo) {
    setAuth(window.reqinfo.user_uid);
  }
  else {
    var uri = '{localhost}/reqinfo?account_email=' + encodeURIComponent(Lampa.Storage.get('account_email', '')) + "&uid=" + encodeURIComponent(Lampa.Storage.get('lampac_unic_id', ''));
    var network = new Lampa.Reguest();
    network.silent(uri, function (j) {
      if (j.user_uid) {
        window.reqinfo = j;
        setAuth(j.user_uid);
      }
    });
  }

})();
