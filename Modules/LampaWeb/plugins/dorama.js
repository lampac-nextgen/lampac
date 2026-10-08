(function() {
  'use strict';

  function startLampacDorama() {
    registerLampacDoramaSource();
    addLampacDoramaMenuButton();
  }

  function readyLampacDorama() {
    if (typeof Lampa === 'undefined' || typeof $ === 'undefined') {
      setTimeout(readyLampacDorama, 250);
      return;
    }

    if (window.appready) {
      startLampacDorama();
      return;
    }

    Lampa.Listener.follow('app', function(e) {
      if (e.type == 'ready') startLampacDorama();
    });
  }

function lampacDoramaDate(daysOffset) {
  var date = new Date();
  date.setDate(date.getDate() + (daysOffset || 0));

  var month = date.getMonth() + 1;
  var day = date.getDate();

  return date.getFullYear() + '-' + (month < 10 ? '0' + month : month) + '-' + (day < 10 ? '0' + day : day);
}

function lampacDoramaDiscoverUrl(extra) {
  extra = extra || {};

  var query = [
    'with_original_language=ko',
    'include_adult=false'
  ];

  if (!extra.hasOwnProperty('with_genres')) {
    query.push('with_genres=18');
  }

  for (var key in extra) {
    if (extra.hasOwnProperty(key)) {
      query.push(encodeURIComponent(key) + '=' + encodeURIComponent(extra[key]));
    }
  }

  return 'discover/tv?' + query.join('&');
}

function lampacDoramaSections() {
  var year = new Date().getFullYear();

  return [
    {
      title: 'Сейчас смотрят',
      url: lampacDoramaDiscoverUrl({ sort_by: 'popularity.desc', 'air_date.gte': lampacDoramaDate(-14), 'air_date.lte': lampacDoramaDate(14) })
    },
    {
      title: 'Новые серии',
      url: lampacDoramaDiscoverUrl({ sort_by: 'air_date.desc', 'air_date.gte': lampacDoramaDate(-14), 'air_date.lte': lampacDoramaDate(0) })
    },
    {
      title: 'Онгоинги',
      url: lampacDoramaDiscoverUrl({ sort_by: 'popularity.desc', with_status: '0|2', 'first_air_date.lte': lampacDoramaDate(0), 'air_date.gte': lampacDoramaDate(0), 'air_date.lte': lampacDoramaDate(21) })
    },
    {
      title: 'Популярное',
      url: lampacDoramaDiscoverUrl({ sort_by: 'popularity.desc' })
    },
    {
      title: 'Последние добавления',
      url: lampacDoramaDiscoverUrl({ sort_by: 'first_air_date.desc', 'first_air_date.lte': lampacDoramaDate(0) })
    },
    {
      title: 'Новинки этого года',
      url: lampacDoramaDiscoverUrl({ sort_by: 'first_air_date.desc', first_air_date_year: year, 'first_air_date.lte': lampacDoramaDate(0) })
    },
    {
      title: 'С высоким рейтингом',
      url: lampacDoramaDiscoverUrl({ sort_by: 'vote_average.desc', 'vote_average.gte': 7, 'vote_count.gte': 50 })
    },
    {
      title: 'Комедийные дорамы',
      url: lampacDoramaDiscoverUrl({ sort_by: 'popularity.desc', with_genres: '18,35' })
    },
    {
      title: 'Криминальные',
      url: lampacDoramaDiscoverUrl({ sort_by: 'popularity.desc', with_genres: '18,80' })
    },
    {
      title: 'Детективы',
      url: lampacDoramaDiscoverUrl({ sort_by: 'popularity.desc', with_genres: '18,9648' })
    },
    {
      title: 'Боевики',
      url: lampacDoramaDiscoverUrl({ sort_by: 'popularity.desc', with_genres: '18,10759' })
    },
    {
      title: 'Фэнтези',
      url: lampacDoramaDiscoverUrl({ sort_by: 'popularity.desc', with_genres: '18,10765' })
    },
    {
      title: 'Семейные',
      url: lampacDoramaDiscoverUrl({ sort_by: 'popularity.desc', with_genres: '18,10751' })
    },
    {
      title: 'Мини-сериалы',
      url: lampacDoramaDiscoverUrl({ sort_by: 'popularity.desc', with_type: 2 })
    },
    {
      title: 'Netflix',
      url: lampacDoramaDiscoverUrl({ sort_by: 'popularity.desc', with_networks: 213 })
    },
    {
      title: 'tvN',
      url: lampacDoramaDiscoverUrl({ sort_by: 'popularity.desc', with_networks: 866 })
    },
    {
      title: 'JTBC',
      url: lampacDoramaDiscoverUrl({ sort_by: 'popularity.desc', with_networks: 885 })
    }
  ];
}

var LAMPAC_DORAMA_SOURCE_VERSION = '2026-05-16-expanded-sections';
var lampacDoramaNetwork;
var LAMPAC_DORAMA_MENU_ICON = '<svg class="lampac-dorama-menu-icon" height="38" viewBox="0 0 38 38" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M7 8.5H31" stroke="currentColor" stroke-width="3" stroke-linecap="round"/><path d="M19 8.5V30" stroke="currentColor" stroke-width="3" stroke-linecap="round"/><path d="M8 30H30" stroke="currentColor" stroke-width="3" stroke-linecap="round"/><circle cx="12.5" cy="20" r="5" stroke="currentColor" stroke-width="3"/><path d="M25 14V26" stroke="currentColor" stroke-width="3" stroke-linecap="round"/><path d="M25 20H31" stroke="currentColor" stroke-width="3" stroke-linecap="round"/></svg>';

function lampacDoramaAddParam(url, param) {
  return url + (/\?/.test(url) ? '&' : '?') + param;
}

function lampacDoramaTmdbUrl(url, page) {
  var language = 'ru-RU';

  if (Lampa.Storage && Lampa.Storage.field) {
    language = Lampa.Storage.field('tmdb_lang') || language;
  }

  url = lampacDoramaAddParam(url, 'api_key=4ef0d7355d9ffb5151e987764708ce96');
  url = lampacDoramaAddParam(url, 'language=' + encodeURIComponent(language));
  url = lampacDoramaAddParam(url, 'page=' + encodeURIComponent(page || 1));

  var localhost = '{localhost}'.replace(/\/$/, '');
  if (localhost && localhost.indexOf('{') !== 0) {
    return localhost + '/tmdb/api/3/' + url;
  }

  var protocol = Lampa.Utils && Lampa.Utils.protocol ? Lampa.Utils.protocol() : 'http://';
  var proxy = Lampa.Storage && Lampa.Storage.field && Lampa.Storage.field('proxy_tmdb');
  var base = proxy ? 'apitmdb.cub.watch/3/' : 'api.themoviedb.org/3/';

  return protocol + base + url;
}

function lampacDoramaReguest() {
  if (!lampacDoramaNetwork && Lampa.Reguest) lampacDoramaNetwork = new Lampa.Reguest();
  return lampacDoramaNetwork;
}

function lampacDoramaIsDiscoverUrl(url) {
  return url &&
    url.indexOf('discover/tv?') === 0 &&
    url.indexOf('with_original_language=ko') >= 0 &&
    url.indexOf('with_genres=18') >= 0;
}

function prepareLampacDoramaPage(json, url, page, title) {
  if (!json) json = { results: [] };

  var currentPage = page || (json && json.page) || 1;
  var totalPages = parseInt((json && json.total_pages) || 0, 10);
  var hasResults = json && json.results && json.results.length;
  var hasNext = totalPages ? currentPage < totalPages : hasResults && json.results.length >= 20;

  json.url = url;
  json.page = currentPage;

  if (title) json.title = title;
  if (hasNext) json.more = true;
  else json.nomore = true;

  return json;
}

function lampacDoramaLoad(url, page, oncomplite, onerror, title) {
  var tmdb = Lampa.Api && Lampa.Api.sources && Lampa.Api.sources.tmdb;

  var network = lampacDoramaReguest();

  if (!url || !tmdb || !network) {
    if (onerror) onerror();
    return;
  }

  network.silent(lampacDoramaTmdbUrl(url, page || 1), function(json) {
    oncomplite(prepareLampacDoramaPage(json, url, page || 1, title));
  }, onerror);
}

function patchLampacDoramaActivityRouting() {
  if (!Lampa.Activity || Lampa.Activity._lampacDoramaPatched) return;

  var originalPush = Lampa.Activity.push;
  Lampa.Activity.push = function(object) {
    if (object && object.component == 'category_full' && lampacDoramaIsDiscoverUrl(object.url)) {
      object.source = 'lampac_dorama';
    }

    return originalPush.apply(this, arguments);
  };

  Lampa.Activity._lampacDoramaPatched = true;
}

function patchLampacDoramaListFallbackSources() {
  if (!Lampa.Api || !Lampa.Api.sources) return;

  ['cub', 'tmdb'].forEach(function(sourceName) {
    var source = Lampa.Api.sources[sourceName];

    if (!source || !source.list || source._lampacDoramaListPatched) return;

    var originalList = source.list;

    source.list = function(params, oncomplite, onerror) {
      if (params && lampacDoramaIsDiscoverUrl(params.url)) {
        lampacDoramaLoad(params.url, params.page, oncomplite, onerror, params.title);
        return;
      }

      return originalList.apply(this, arguments);
    };

    source._lampacDoramaListPatched = true;
  });
}

function registerLampacDoramaSource() {
  if (!Lampa.Api || !Lampa.Api.sources) return false;
  patchLampacDoramaActivityRouting();
  patchLampacDoramaListFallbackSources();
  if (Lampa.Api.sources.lampac_dorama && Lampa.Api.sources.lampac_dorama._lampacVersion == LAMPAC_DORAMA_SOURCE_VERSION) return true;
  if (!Lampa.Api.sources.tmdb || !Lampa.Api.sources.tmdb.list) return false;

  var tmdb = Lampa.Api.sources.tmdb;

  Lampa.Api.sources.lampac_dorama = {
    _lampacVersion: LAMPAC_DORAMA_SOURCE_VERSION,
    main: function(params, oncomplite, onerror) {
      Lampa.Api.sources.lampac_dorama.category(params, oncomplite, onerror);
    },
    category: function(params, oncomplite, onerror) {
      var sections = lampacDoramaSections();
      var pending = sections.length;
      var data = [];

      function done() {
        pending--;

        if (pending > 0) return;

        var rows = [];
        for (var i = 0; i < data.length; i++) {
          if (data[i] && data[i].results && data[i].results.length) rows.push(data[i]);
        }

        if (rows.length) oncomplite(rows);
        else if (onerror) onerror();
      }

      sections.forEach(function(section, index) {
        lampacDoramaLoad(section.url, 1, function(json) {
          data[index] = json;
          done();
        }, done, section.title);
      });
    },
    list: function(params, oncomplite, onerror) {
      lampacDoramaLoad(params.url, params.page, oncomplite, onerror, params.title);
    },
    menuCategory: function(params, oncomplite) {
      oncomplite(lampacDoramaSections().map(function(section) {
        return {
          title: section.title,
          url: section.url
        };
      }));
    },
    full: tmdb.full,
    person: tmdb.person,
    seasons: tmdb.seasons,
    collections: tmdb.collections,
    menu: tmdb.menu,
    clear: tmdb.clear
  };

  return true;
}

function openLampacDoramaMenu() {
  if (registerLampacDoramaSource()) {
    Lampa.Activity.push({
      url: 'tv',
      title: 'Дорамы',
      component: 'category',
      source: 'lampac_dorama',
      card_type: true,
      page: 1
    });

    return;
  }

  Lampa.Activity.push({
    url: lampacDoramaDiscoverUrl({ sort_by: 'popularity.desc' }),
    title: 'Дорамы',
    component: 'category_full',
    source: 'tmdb',
    card_type: true,
    page: 1
  });
}

function getLampacDoramaAnchor() {
  var items = $('.menu .menu__item');
  var tv = items.filter('[data-action="tv"]').first();

  if (tv.length) return tv;

  return items.filter(function() {
    return $(this).find('.menu__text').text().trim() == 'Сериалы';
  }).first();
}

function bindLampacDoramaButton(button) {
  button.find('.menu__ico').html(LAMPAC_DORAMA_MENU_ICON);
  button.off('hover:enter.lampac-dorama click.lampac-dorama');
  button.on('hover:enter.lampac-dorama click.lampac-dorama', function() {
    openLampacDoramaMenu();
  });
}

function createLampacDoramaButton() {
  var button = $("<li class=\"menu__item selector\" data-action=\"dorama\">\n        <div class=\"menu__ico\"><img src=\"./img/icons/menu/tv.svg\" /></div>\n        <div class=\"menu__text\">Дорамы</div>\n    </li>");

  bindLampacDoramaButton(button);

  return button;
}

function addLampacDoramaMenuButton(attempt) {
  var anchor = getLampacDoramaAnchor();

  if (!anchor.length) {
    if ((attempt || 0) < 40) {
      setTimeout(function() {
        addLampacDoramaMenuButton((attempt || 0) + 1);
      }, 250);
    }

    return;
  }

  var existing = $('.menu .menu__item[data-action="dorama"]');
  var button = existing.first();

  existing.not(button).remove();

  if (!button.length) {
    button = createLampacDoramaButton();
  } else {
    bindLampacDoramaButton(button);
  }

  if (!button.prev().is(anchor)) {
    anchor.after(button.detach());
  }

  if ((attempt || 0) < 12) {
    setTimeout(function() {
      addLampacDoramaMenuButton((attempt || 0) + 1);
    }, 500);
  }
}


  readyLampacDorama();
})();

