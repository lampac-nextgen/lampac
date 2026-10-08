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

  // Login/password are shared by both links in Lampa, so only touch them while our server is the active one.
  function setAuth(login) {
    var one = Lampa.Storage.get('torrserver_url', '');
    var two = Lampa.Storage.get('torrserver_url_two', '');
    if ((Lampa.Storage.field('torrserver_use_link') == 'two' ? two || one : one || two) != ts_url)
      return;

    Lampa.Storage.set('torrserver_auth', 'true');
    Lampa.Storage.set('torrserver_login', login);
    Lampa.Storage.set('torrserver_password', '{defaultPasswd}');
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
