(function () {
    'use strict';

    var unic_id = Lampa.Storage.get('lampac_unic_id', '');
    if (!unic_id) {
        unic_id = Lampa.Utils.uid(8).toLowerCase();
        Lampa.Storage.set('lampac_unic_id', unic_id);
    }

    Lampa.Storage.set('torrserver_url', '{tshost}/ts');

    if ('{token}' != '') {
        Lampa.Storage.set('torrserver_auth', 'true');
        Lampa.Storage.set('torrserver_login', '{token}');
        Lampa.Storage.set('torrserver_password', '{defaultPasswd}');
    }
    else if (window.reqinfo) {
        Lampa.Storage.set('torrserver_auth', 'true');
        Lampa.Storage.set('torrserver_login', window.reqinfo.user_uid);
        Lampa.Storage.set('torrserver_password', '{defaultPasswd}');
    }
    else {
        var uri = '{localhost}/reqinfo?account_email=' + encodeURIComponent(Lampa.Storage.get('account_email', '')) + "&uid=" + encodeURIComponent(Lampa.Storage.get('lampac_unic_id', ''));
        var network = new Lampa.Reguest();
        network.silent(uri, function (j) {
            if (j.user_uid) {
                window.reqinfo = j;
                Lampa.Storage.set('torrserver_auth', 'true');
                Lampa.Storage.set('torrserver_login', j.user_uid);
                Lampa.Storage.set('torrserver_password', '{defaultPasswd}');
            }
        });
    }

})();