(function() {
  'use strict';

  window.lampac_version = { major: 0, minor: 0 };

  //localStorage.setItem('cub_mirrors', '["mirror-kurwa.men"]');
  
  window.lampa_settings = window.lampa_settings || {};
  window.lampa_settings.torrents_use = true;    // Показывать кнопку торрентов
  window.lampa_settings.demo = false;           // demo off
  window.lampa_settings.read_only = false;      // Режим только для чтения, без кнопок онлайн и расширений
  window.lampa_settings.socket_use = true;      // cub - Использовать сокеты для синхронизации данных
  window.lampa_settings.socket_url = undefined; // cub - Адрес сокета, по умолчанию лампа берет адреса из манифеста
  window.lampa_settings.socket_methods = true;  // cub - Обрабатывать сообщения сокетов
  window.lampa_settings.account_use = true;   // cub - Использовать аккаунты
  window.lampa_settings.account_sync = true;  // cub - Синхронизировать закладки, таймкоды и прочее
  window.lampa_settings.plugins_store = true; // cub магазин расширений
  window.lampa_settings.feed = true;          // cub лента
  window.lampa_settings.iptv = false;         // Является ли приложение IPTV
  window.lampa_settings.white_use = false;    // Белая и пушистая лампа, для одобрения модерации
  window.lampa_settings.push_state = true;    // адрес в url /?card=1241982&media=movie 
  window.lampa_settings.lang_use = true;      // Подключить другие языки интерфейса, по умолчанию только русский и английский
  window.lampa_settings.plugins_use = true;   // Разрешить установку плагинов и расширений
  window.lampa_settings.dcma = false;         // Добавить список блокировки карточек, пример: [{"id":3566556,"cat":"movie"},...]
  window.lampa_settings.services = true;      // Различные сервисы cub в приложении
  window.lampa_settings.youtube = true;       // Подключить YouTube API
  window.lampa_settings.geo = true;           // Определять гео по IP, иначе будет RU
  window.lampa_settings.mirrors = true;       // Использовать поиск зеркал

  window.lampa_settings.disable_features = window.lampa_settings.disable_features || {};
  window.lampa_settings.disable_features.dmca = true;           // шлет нахер правообладателей - on
  window.lampa_settings.disable_features.ads = true;            // Вспомогатиленые сервисы на подписку према
  window.lampa_settings.disable_features.reactions = false;     // cub реакции
  window.lampa_settings.disable_features.discuss = false;       // cub комментарии
  window.lampa_settings.disable_features.ai = false;            // cub AI-поиск
  window.lampa_settings.disable_features.install_proxy = false; // cub tmdb proxy
  window.lampa_settings.disable_features.subscribe = false;     // cub подписки
  window.lampa_settings.disable_features.blacklist = false;     // Черный список плагинов
  window.lampa_settings.disable_features.persons = false;       // Подписка на актеров
  window.lampa_settings.disable_features.trailers = false;      // Трейлеры
  window.lampa_settings.disable_features.lgbt = true;           // Разрешить ЛГБТ контент

  window.lampa_settings.developer = window.lampa_settings.developer || {};
  
  
  {lampainit-invc}

  var timer = setInterval(function() {
    if (typeof Lampa !== 'undefined') {
      clearInterval(timer);
	  
      if (lampainit_invc)
        lampainit_invc.appload();

      if ({btn_priority_forced})
        Lampa.Storage.set('full_btn_priority', '{full_btn_priority_hash}');

      var unic_id = Lampa.Storage.get('lampac_unic_id', '');
      if (!unic_id) {
        unic_id = Lampa.Utils.uid(8).toLowerCase();
        Lampa.Storage.set('lampac_unic_id', unic_id);
      }

      Lampa.Utils.putScriptAsync(["{localhost}/privateinit.js?account_email=" + encodeURIComponent(Lampa.Storage.get('account_email', '')) + "&uid=" + encodeURIComponent(Lampa.Storage.get('lampac_unic_id', ''))], function() {});

      if (window.appready) {
        start();
      }
      else {
        Lampa.Listener.follow('app', function(e) {
          if (e.type == 'ready') {
            start();
          }
        });
      }

	  {pirate_store}
    }
  }, 200);

  function start() {
    {deny}
	
    if (lampainit_invc) lampainit_invc.appready();
    var first_initiale = !Lampa.Storage.get('lampac_initiale', 'false');

    if (first_initiale) {
      Lampa.Storage.set('lampac_initiale', 'true');
      Lampa.Storage.set('source', 'cub');
      Lampa.Storage.set('video_quality_default', '2160');
      Lampa.Storage.set('full_btn_priority', '{full_btn_priority_hash}');
      Lampa.Storage.set('proxy_tmdb', '{country}' == 'RU');
      Lampa.Storage.set('poster_size', 'w300');

      Lampa.Storage.set('parser_use', 'true');
      Lampa.Storage.set('jackett_url', '{jachost}');
      Lampa.Storage.set('jackett_key', '1');
      Lampa.Storage.set('parser_torrent_type', 'jackett');
    }

    var plugins = Lampa.Plugins.get();

    var plugins_add = {initiale};

    var plugins_push = [];

    plugins_add.forEach(function(plugin) {
      if (!plugins.find(function(a) {
          return a.url == plugin.url;
        })) {
        Lampa.Plugins.add(plugin);
        Lampa.Plugins.save();

        plugins_push.push(plugin.url);
      }
    });

    if (plugins_push.length) Lampa.Utils.putScript(plugins_push, function() {}, function() {}, function() {}, true);
	
    if (lampainit_invc && first_initiale)
      lampainit_invc.first_initiale();

  }
})();
