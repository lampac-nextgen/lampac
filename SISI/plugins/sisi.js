(function() {
  'use strict';

  var Defined = {
    use_api: 'lampac',
    localhost: '{localhost}/sisi',
    framework: ''
  };

  var luid = Lampa.Storage.get('lampac_unic_id', '');
  if (!luid) {
    luid = Lampa.Utils.uid(8).toLowerCase();
    Lampa.Storage.set('lampac_unic_id', luid);
  }

  Lampa.Lang.add({
    lampac_sisiname: {
      ru: 'Клубничка',
      en: 'Strawberry',
      uk: 'Полуничка',
      zh: '草莓'
    }
  });

  var network = new Lampa.Reguest();
  var preview_timer, preview_video;
  var SISI_SOURCE = 'sisi_lampac';
  var REQUEST_TIMEOUT = 10000;

  network.timeout(REQUEST_TIMEOUT);

  function sourceTitle(title) {
    return Lampa.Utils.capitalizeFirstLetter(title.split('.')[0]);
  }

  function isVIP(element) {
    return /vip.mp4/.test(element.video);
  }

  {rch_websoket}

  function modal(text) {
    var id = Lampa.Storage.get('sisi_unic_id', '').toLowerCase();
    var controller = Lampa.Controller.enabled().name;
    var content = sisiModalHtml(text, id);
    Lampa.Modal.open({
      title: 'Доступ ограничен',
      html: $(content),
      size: 'medium',
      onBack: function onBack() {
        Lampa.Modal.close();
        Lampa.Controller.toggle(controller);
      }
    });
  }

  function qualityDefault(qualitys) {
    var preferably = Lampa.Storage.get('video_quality_default', '1080') + 'p';
    var url;

    if (qualitys) {
      for (var q in qualitys) {
        if (q.indexOf(preferably) == 0) url = qualitys[q];
      }

      if (!url) url = qualitys[Lampa.Arrays.getKeys(qualitys)[0]];
    }

    return url;
  }

  function play(element) {
    var controller_enabled = Lampa.Controller.enabled().name;

    if (isVIP(element)) {
      return modal();
    }

    if ({historySave} && !element.history_uid && element.bookmark && Lampa.Storage.field('sisi_history')) {
      network.timeout(REQUEST_TIMEOUT);
      network.silent(Api.account(Defined.localhost + '/history/add'), function(e) {}, function() {}, JSON.stringify(element), {
        headers: {
          'Content-Type': 'application/json'
        }
      });
    }

    if (element.json) {
      Lampa.Loading.start(function() {
        network.clear();
        Lampa.Loading.stop();
      });
      Api.account(element.video + '&json=true');
      Api.qualitys(element.video, function(data) {
        if (data.error) {
          Lampa.Noty.show(Lampa.Lang.translate('torrent_parser_nofiles'));
          Lampa.Loading.stop();
          return;
        }

        var qualitys = data.qualitys || data;
        var recomends = data.recomends || [];
        Lampa.Loading.stop();

        for (var i in qualitys) {
          qualitys[i] = Api.account(qualitys[i], true);
        }

        var video = {
          title: element.name,
          url: Api.account(qualityDefault(qualitys), true),
          url_reserve: data.qualitys_proxy ? Api.account(qualityDefault(data.qualitys_proxy), true) : false,
          quality: qualitys,
          headers: data.headers_stream
        };
        Lampa.Player.play(video);

        if (recomends.length) {
          recomends.forEach(function(a) {
            a.title = Lampa.Utils.shortText(a.name, 50);
            a.icon = '<img class="size-youtube" src="' + a.picture + '" />';
            a.template = 'selectbox_icon';

            a.url = function(call) {
              if (a.json) {
                Api.qualitys(a.video, function(data) {
                  a.quality = data.qualitys;
                  a.url = Api.account(qualityDefault(data.qualitys), true);
                  if (data.qualitys_proxy) a.url_reserve = Api.account(qualityDefault(data.qualitys_proxy), true);
                  call();
                });
              } else {
                a.url = a.video;
                call();
              }
            };
          });
          Lampa.Player.playlist(recomends);
        } else {
          Lampa.Player.playlist([video]);
        }

        Lampa.Player.callback(function() {
          Lampa.Controller.toggle(controller_enabled);
        });
      }, function() {
        Lampa.Noty.show(Lampa.Lang.translate('torrent_parser_nofiles'));
        Lampa.Loading.stop();
      });
    } else {
      if (element.qualitys) {
        for (var i in element.qualitys) {
          element.qualitys[i] = Api.account(element.qualitys[i], true);
        }
      }

      var video = {
        title: element.name,
        url: Api.account(qualityDefault(element.qualitys) || element.video, true),
        url_reserve: Api.account(qualityDefault(element.qualitys_proxy) || element.video_reserve || '', true),
        quality: element.qualitys
      };
      Lampa.Player.play(video);
      Lampa.Player.playlist([video]);
      Lampa.Player.callback(function() {
        Lampa.Controller.toggle(controller_enabled);
      });
    }
  }

  function fixCards(json) {
    json.forEach(function(m) {
      m.background_image = m.picture;
      m.poster = m.picture;
      m.img = m.picture;
      m.name = Lampa.Utils.capitalizeFirstLetter(m.name).replace(/\&(.*?);/g, '');
    });
  }

  function pauseMedia(media) {
    if (!media || !media.length) return;
    var pausePromise;
    try {
      pausePromise = media[0].pause();
    } catch (e) {}
    if (pausePromise !== undefined) {
      pausePromise.then(function () {}).catch(function () {});
    }
  }

  function playMedia(media) {
    if (!media) return;
    var playPromise;
    try {
      playPromise = media.play();
    } catch (e) {}
    if (playPromise !== undefined) {
      playPromise.then(function () {}).catch(function () {});
    }
  }

  function hidePreview() {
    clearTimeout(preview_timer);

    if (preview_video) {
      pauseMedia(preview_video.find('video'));
      preview_video.addClass('hide');
      preview_video = false;
    }
  }

  function preview(target, element) {
    hidePreview();
    preview_timer = setTimeout(function () {
      if (!element.preview || !Lampa.Storage.field('sisi_preview')) return;
      var video = target.find('video');
      var container = target.find('.sisi-video-preview');

      if (!video.length) {
        video = document.createElement('video');
        container = document.createElement('div');
        container.className = 'sisi-video-preview';
        container.append(video);
        target.find('.card__view').append(container);
        video.src = element.preview;
        video.addEventListener('ended', function () {
          container.classList.add('hide');
        });
        video.load();
      } else {
        video = video[0];
        container = container[0];
      }

      preview_video = $(container);
      playMedia(video);
      $(container).removeClass('hide');
    }, 1500);
  }

  function fixList(list) {
    list.forEach(function(a) {
      if (!a.quality && a.time) a.quality = a.time;
    });
    return list;
  }

  function menu$2(target, card_data) {
    if (!card_data.bookmark) return;
    var cm = [{
      title: !card_data.bookmark.uid ? 'В закладки' : 'Удалить из закладок'
    }];

    if (card_data.history_uid) {
      cm.push({
        title: 'Удалить из истории',
        history: true
      });
    }

    if (card_data.related) {
      cm.push({
        title: 'Похожие',
        related: true
      });
    }

    if (card_data.model) {
      cm.push({
        title: card_data.model.name,
        model: true
      });
    }

    if (Lampa.Platform.is('android') && Lampa.Storage.field('player') !== 'inner') {
      cm.push({
        title: 'Плеер Lampa',
        lampaplayer: true
      });
    }

    Lampa.Select.show({
      title: 'Меню',
      items: cm,
      onSelect: function onSelect(m) {
        if (m.model) {
          Lampa.Activity.push({
            url: Defined.localhost.replace('/sisi', '') + '/' + card_data.model.uri,
            title: 'Модель - ' + card_data.model.name,
            component: 'sisi_view_' + Defined.use_api,
            page: 1
          });
        } else if (m.related) {
          Lampa.Activity.push({
            url: card_data.video + '&related=true',
            title: 'Похожие - ' + card_data.title,
            component: 'sisi_view_' + Defined.use_api,
            page: 1
          });
        } else if (m.history) {
          Api.history(card_data, function(status) {
            Lampa.Noty.show('Успешно');
          });
          Lampa.Controller.toggle('content');
        } else if (m.lampaplayer) {
          Lampa.Controller.toggle('content');
          play(card_data);
        } else {
          Api.bookmark(card_data, !card_data.bookmark.uid, function(status) {
            Lampa.Noty.show('Успешно');
          });
          Lampa.Controller.toggle('content');
        }
      },
      onBack: function onBack() {
        Lampa.Controller.toggle('content');
      }
    });
  }

  var Utils = {
    sourceTitle: sourceTitle,
    play: play,
    fixCards: fixCards,
    isVIP: isVIP,
    preview: preview,
    hidePreview: hidePreview,
    fixList: fixList,
    menu: menu$2
  };

  function sisiCardHandlers() {
    return {
      onMenu: function (target, card_data) {
        return Utils.menu(target, card_data);
      },
      onEnter: function (card, element) {
        Utils.hidePreview();
        Utils.play(element);
      },
      onFocus: function (target, element) {
        Utils.preview($(target), element);
      }
    };
  }

  function mapPlaylistLine(line) {
    var handlers = sisiCardHandlers();

    line.url = line.url || '';
    Utils.fixCards(line.results);

    line.params = {
      items: {
        mapping: 'grid',
        cols: 3,
        align_left: true
      }
    };

    line.results.forEach(function (element) {
      element.source = SISI_SOURCE;
      element.params = {
        style: { name: 'collection' },
        emit: {
          onFocus: function (target) {
            handlers.onFocus(target, element);
          },
          onlyEnter: function (target, data) {
            handlers.onEnter(null, data || element);
          },
          onLong: function (target, data) {
            handlers.onMenu($(target), data || element);
          }
        }
      };
    });

    return Lampa.Utils.addSource(line, SISI_SOURCE);
  }

  function normalizeViewJson(json) {
    json.results = Utils.fixList(json.list);
    json.total_pages = json.total_pages || 30;
    delete json.list;
    Utils.fixCards(json.results);

    json.params = {
      items: {
        mapping: 'grid',
        cols: 3
      }
    };

    var handlers = sisiCardHandlers();

    json.results.forEach(function (element) {
      element.source = SISI_SOURCE;
      element.params = {
        style: { name: 'collection' },
        emit: {
          onFocus: function (target) {
            handlers.onFocus(target, element);
          },
          onlyEnter: function (target, data) {
            handlers.onEnter(null, data || element);
          },
          onLong: function (target, data) {
            handlers.onMenu($(target), data || element);
          }
        }
      };
    });

    return Lampa.Utils.addSource(json, SISI_SOURCE);
  }

  function processViewMenu(menu) {
    if (!menu) return;

    menu.forEach(function (m) {
      var spl = m.title.split(':');
      m.title = spl[0].trim();
      if (spl[1]) m.subtitle = Lampa.Utils.capitalizeFirstLetter(spl[1].trim().replace(/all/i, 'Любой'));

      if (m.submenu) {
        m.submenu.forEach(function (s) {
          s.title = Lampa.Utils.capitalizeFirstLetter(s.title.trim().replace(/all/i, 'Любой'));
        });
      }
    });
  }

  function sisiShowEmpty(comp, er) {
    var empty = new Lampa.Empty({
      descr: typeof er == 'string' ? er : Lampa.Lang.translate('empty_text_two')
    });

    Lampa.Activity.all().forEach(function (active) {
      if (comp.activity == active.activity) {
        active.activity.render().find('.activity__body > div')[0].appendChild(empty.render(true));
      }
    });

    comp.start = empty.start.bind(empty);
    comp.activity.loader(false);
    comp.activity.toggle();
  }

  function sisiViewFilter(menu, object) {
    if (!menu) return;

    var items = menu.filter(function (m) {
      return !m.search_on;
    });
    var search = menu.find(function (m) {
      return m.search_on;
    });

    if (!search) search = object.search_start;
    if (!items.length && !search) return;

    if (search) {
      Lampa.Arrays.insert(items, 0, {
        title: 'Найти',
        onSelect: function onSelect() {
          $('body').addClass('ambience--enable');
          Lampa.Input.edit(
            {
              title: 'Поиск',
              value: '',
              free: true,
              nosave: true
            },
            function (value) {
              $('body').removeClass('ambience--enable');
              Lampa.Controller.toggle('content');

              if (value) {
                var separator = search.playlist_url.indexOf('?') !== -1 ? '&' : '?';
                Lampa.Activity.push({
                  url: search.playlist_url + separator + 'search=' + encodeURIComponent(value),
                  title: 'Поиск - ' + value,
                  component: 'sisi_view_' + Defined.use_api,
                  search_start: search,
                  page: 1
                });
              }
            }
          );
        }
      });
    }

    Lampa.Select.show({
      title: 'Фильтр',
      items: items,
      onBack: function onBack() {
        Lampa.Controller.toggle('content');
      },
      onSelect: function onSelect(a) {
        menu.forEach(function (m) {
          m.selected = m == a;
        });

        if (a.submenu) {
          Lampa.Select.show({
            title: a.title,
            items: a.submenu,
            onBack: function onBack() {
              sisiViewFilter(menu, object);
            },
            onSelect: function onSelect(b) {
              Lampa.Activity.push({
                title: object.title,
                url: b.playlist_url,
                component: 'sisi_view_' + Defined.use_api,
                page: 1
              });
            }
          });
        } else {
          sisiViewFilter(menu, object);
        }
      }
    });
  }

  function suspendSisiActivity() {
    Utils.hidePreview();
  }

  var menu$1;

  function ApiPWA() {
    var _this = this;

    var network = new Lampa.Reguest();
    network.timeout(REQUEST_TIMEOUT);

    this.menu = function(success, error) {
      if (menu$1) return success(menu$1);
      DotNet.invokeMethodAsync("JinEnergy", 'sisi', '').then(function(data) {
        if (data) {
          menu$1 = data;
          success(menu$1);
        } else {
          error(data.msg);
        }
      })["catch"](function() {
        console.log('Sisi', 'no load menu');
        error();
      });
    };

    this.view = function(params, success, error) {
      var u = this.account(Lampa.Utils.addUrlComponent(params.url, 'pg=' + (params.page || 1)));
      DotNet.invokeMethodAsync("JinEnergy", u.path, u.query).then(function(json) {
        if (json.list) {
          success(normalizeViewJson(json));
        } else {
          error();
        }
      })["catch"](function() {
        console.log('Sisi', 'no load', u.path + '+' + u.query);
        error();
      });
    };

    this.bookmark = function(element, add, call) {
      call(true);
    };

    this.account = function(u, join) {
      if (join) {
        if (Defined.use_api == 'lampac' && u.indexOf(Defined.localhost.replace('/sisi', '')) == -1) return u;
      }

      var unic_id = Lampa.Storage.get('sisi_unic_id', '');
      var uid = Lampa.Storage.get('lampac_unic_id', '');
      var email = Lampa.Storage.get('account', {}).email;

      if (u.indexOf('box_mac=') == -1) u = Lampa.Utils.addUrlComponent(u, 'box_mac=' + unic_id);

      if (email) {
        if (u.indexOf('account_email=') == -1) u = Lampa.Utils.addUrlComponent(u, 'account_email=' + encodeURIComponent(email));
      }

      if (uid) {
        if (u.indexOf('uid=') == -1) u = Lampa.Utils.addUrlComponent(u, 'uid=' + encodeURIComponent(uid));
      }

      if (u.indexOf('token=') == -1) {
        var token = '{token}';
        if (token != '') u = Lampa.Utils.addUrlComponent(u, 'token={token}');
      }

      if (join) return u;
      return {
        path: u.split('?')[0],
        query: u.split('?')[1]
      };
    };

    this.playlist = function(add_url_query, oncomplite, error) {
      var load = function load() {
        var status = new Lampa.Status(menu$1.length);

        status.onComplite = function(data) {
          var items = [];
          menu$1.forEach(function(m) {
            if (data[m.playlist_url] && data[m.playlist_url].results.length) items.push(data[m.playlist_url]);
          });
          if (items.length) oncomplite(items);
          else error();
        };

        menu$1.forEach(function(m, i) {
          var separator = m.playlist_url.indexOf('?') !== -1 ? '&' : '?';
          var url_query = add_url_query.indexOf('?') !== -1 || add_url_query.indexOf('&') !== -1 ? add_url_query.substring(1) : add_url_query;
          var u = _this.account(m.playlist_url + separator + url_query);

          var b = false;
          var w = setTimeout(function() {
            b = true;
            status.error();
          }, 1000 * 8);
          DotNet.invokeMethodAsync("JinEnergy", u.path, u.query).then(function(json) {
            clearTimeout(w);
            if (b) return;

            if (json.list) {
              json.title = Utils.sourceTitle(m.title);
              json.results = Utils.fixList(json.list);
              json.url = m.playlist_url;
              delete json.list;
              status.append(m.playlist_url, mapPlaylistLine(json));
            } else {
              status.error();
            }
          })["catch"](function() {
            console.log('Sisi', 'no load', u.path + '+' + u.query);
            clearTimeout(w);
            status.error();
          });
        });
      };

      if (menu$1) load();
      else {
        _this.menu(load, error);
      }
    };

    this.main = function(params, oncomplite, error) {
      this.playlist('', oncomplite, error);
    };

    this.search = function(params, oncomplite, error) {
      this.playlist('?search=' + encodeURIComponent(params.query), oncomplite, error);
    };

    this.qualitys = function(video_url, oncomplite, error) {
      var u = this.account(video_url + '&json=true');
      DotNet.invokeMethodAsync("JinEnergy", u.path, u.query).then(oncomplite)["catch"](function(e) {
        console.log('Sisi', 'no load', u.path + '+' + u.query);
        error();
      });
    };

    this.clear = function() {
      network.clear();
    };
  }

  var ApiPWA$1 = new ApiPWA();

  var menu;

  function ApiHttp() {
    var _this = this;

    var network = new Lampa.Reguest();
    network.timeout(REQUEST_TIMEOUT);

    this.menu = function(success, error) {
      if (menu) return success(menu);
      var url = this.account(Defined.localhost);
        url += (url.indexOf('?') === -1 ? '?' : '&') + 'rchtype=' + ((window.rch_nws && window.rch_nws[hostkey] ? window.rch_nws[hostkey].type : window.rch && window.rch[hostkey] ? window.rch[hostkey].type : '') || '');
      network.silent(url, function(data) {
        if (data.channels) {
          menu = data.channels;
          success(menu);
        } else {
          error(data.msg);
        }
      }, error);
    };

    this.view = function(params, success, error, waiting_rch) {
      var u = Lampa.Utils.addUrlComponent(params.url, 'pg=' + (params.page || 1));
      network.silent(this.account(u), function(json) {
        if (json.rch) {
          if (waiting_rch){
            error();
            return;
          }
          rchRun(json, function() {
            _this.view(params, success, error, true);
          });
        } else if (json.accsdb) {
          error();
          Lampa.Noty.show(json.denymsg || json.msg, {style: 'error', time: 8000});
        } else if (json.list) {
          success(normalizeViewJson(json));
        } else {
          error();
        }
      }, error);
    };

    this.bookmark = function(element, add, call) {
      var u = Defined.localhost + '/bookmark/' + (add ? 'add' : 'remove?id=' + element.bookmark.uid);
      network.silent(this.account(u), function(e) {
        call(true);
      }, function() {
        call(false);
      }, JSON.stringify(element), {
        headers: {
          'Content-Type': 'application/json'
        }
      });
    };

    this.history = function(element, call) {
      var u = Defined.localhost + '/history/remove?id=' + element.history_uid;
      network.silent(this.account(u), function(e) {
        call(true);
      }, function() {
        call(false);
      });
    };

    this.account = function(u) {
      u = u.replace(/^[\?&]+/, '');
      u = u.replace(/[\?&]+$/, '');

      if (u.replace(/[\?&]+$/, '').indexOf('{localhost}'.replace(/https:/, '').replace(/http:/, '')) === -1)
        return u;

      var unic_id = Lampa.Storage.get('sisi_unic_id', '');
      var uid = Lampa.Storage.get('lampac_unic_id', '');
      var email = Lampa.Storage.get('account', {}).email;

      if (u.indexOf('box_mac=') === -1)
        u = Lampa.Utils.addUrlComponent(u, 'box_mac=' + unic_id);

      if (email) {
        if (u.indexOf('account_email=') === -1)
          u = Lampa.Utils.addUrlComponent(u, 'account_email=' + encodeURIComponent(email));
      }

      if (uid) {
        if (u.indexOf('uid=') === -1)
          u = Lampa.Utils.addUrlComponent(u, 'uid=' + encodeURIComponent(uid));
      }

      if (u.indexOf('token=') === -1) {
        var token = '{token}';
        if (token != '') u = Lampa.Utils.addUrlComponent(u, 'token={token}');
      }

      var profile_id = Lampa.Storage.get('lampac_profile_id', '');
      if (profile_id != '') u = Lampa.Utils.addUrlComponent(u, 'profile_id=' + profile_id);

      if (u.indexOf('nws_id=') === -1) {
        var nws_id = Lampa.Storage.get('lampac_nws_id', '');
        if (nws_id) u = Lampa.Utils.addUrlComponent(u, 'nws_id=' + encodeURIComponent(nws_id));
      }

      return u;
    };

    this.playlist = function(add_url_query, oncomplite, error) {
      var load = function load() {
        var status = new Lampa.Status(menu.length);

        status.onComplite = function(data) {
          var items = [];
          menu.forEach(function(m) {
            if (data[m.playlist_url] && data[m.playlist_url].results.length) items.push(data[m.playlist_url]);
          });
          if (items.length) oncomplite(items);
          else error();
        };

        menu.forEach(function(m) {
          function loadThis() {
            var separator = m.playlist_url.indexOf('?') !== -1 ? '&' : '?';
            network.silent(_this.account(m.playlist_url.replace(/[\?&]+$/, '') + separator + add_url_query.replace(/^[\?&]+/, '')), function(json) {
              if (json.rch) {
                rchRun(json, function() {
                  loadThis();
                });
              } else if (json.accsdb) {
                status.error();
                Lampa.Noty.show(json.denymsg || json.msg, {style: 'error', time: 8000});
              } else if (json.list) {
                json.title = Utils.sourceTitle(m.title);
                json.results = Utils.fixList(json.list);
                json.url = m.playlist_url;
                delete json.list;
                status.append(m.playlist_url, mapPlaylistLine(json));
              } else {
                status.error();
              }
            }, status.error.bind(status));
          }

          loadThis();
        });
      };

      if (menu) load();
      else {
        _this.menu(load, error);
      }
    };

    this.main = function(params, oncomplite, error) {
      this.playlist('', oncomplite, error);
    };

    this.search = function(params, oncomplite, error) {
      this.playlist('?search=' + encodeURIComponent(params.query), oncomplite, error);
    };

    this.qualitys = function(video_url, oncomplite, error, waiting_rch) {
      network.silent(this.account(video_url + '&json=true'), function(json) {
        if (json.rch) {
          if (waiting_rch) {
            error();
            return;
          }
          rchRun(json, function() {
            _this.qualitys(video_url, oncomplite, error, true);
          });
        } else if (json.accsdb) {
          error();
          Lampa.Noty.show(json.denymsg || json.msg, {style: 'error', time: 8000});
        } else oncomplite(json);
      }, error);
    };

    this.clear = function() {
      network.clear();
    };
  }

  var ApiHttp$1 = new ApiHttp();

  var Api = ApiHttp$1; //Defined.use_api == 'pwa' ? ApiPWA$1 : ApiHttp$1;

  function Sisi(object) {
    var comp = Lampa.Maker.make('Main', object);

    comp.use({
      onCreate: function () {
        Api.main(
          object,
          function (data) {
            for (var i = 0; i < data.length; i++) data[i] = mapPlaylistLine(data[i]);
            this.build(Lampa.Utils.addSource(data, SISI_SOURCE));
          }.bind(this),
          function (er) {
            sisiShowEmpty(this, er);
          }.bind(this)
        );
      },
      onInstance: function (line, data) {
        line.use({
          onMore: function () {
            Lampa.Activity.push({
              url: data.url,
              title: data.title,
              component: 'sisi_view_' + Defined.use_api,
              page: 2
            });
          }
        });
      },
      onPause: suspendSisiActivity,
      onStop: suspendSisiActivity,
      onDestroy: suspendSisiActivity
    });

    return comp;
  }

  function View(object) {
    var menu;
    var comp = Lampa.Maker.make('Category', object, function (module) {
      module.toggle(Lampa.Maker.module('Category').MASK.base, 'Pagination');
    });

    comp.filter = function () {
      sisiViewFilter(menu, object);
    };

    comp.use({
      onCreate: function () {
        Api.view(
          object,
          function (data) {
            menu = data.menu;
            processViewMenu(menu);
            this.build(data);

            if (!data.results.length && object.url.indexOf('/bookmarks') !== -1) {
              Lampa.Noty.show('Удерживайте ОК на видео для добавления в закладки.', {
                time: 10000
              });
            }
          }.bind(this),
          function (er) {
            sisiShowEmpty(this, er);
          }.bind(this)
        );
      },
      onNext: function (resolve, reject) {
        Api.view(object, resolve, reject);
      },
      onRight: function () {
        comp.filter();
      },
      onPause: suspendSisiActivity,
      onStop: suspendSisiActivity,
      onDestroy: suspendSisiActivity
    });

    return comp;
  }

  var Search = {
    title: 'Клубничка',
    search: function search(params, oncomplite) {
      network.timeout(REQUEST_TIMEOUT);
      network.silent(
        '{localhost}/rch/check/connected',
        function (json) {
          if (json.rch) {
            rchRun(json, function () {
              Api.search(params, function (data) {
                oncomplite(Lampa.Utils.addSource(data, SISI_SOURCE));
              });
            });
          } else {
            Api.search(params, function (data) {
              oncomplite(Lampa.Utils.addSource(data, SISI_SOURCE));
            });
          }
        },
        function () {
          oncomplite([]);
        }
      );
    },
    onCancel: function onCancel() {
      Api.clear();
    },
    params: {
      lazy: true,
      align_left: true
    },
    onMore: function onMore(params, close) {
      close();
      var url = Lampa.Utils.addUrlComponent(params.data.url, 'search=' + encodeURIComponent(params.query));
      Lampa.Activity.push({
        url: url,
        title: 'Поиск - ' + params.query,
        component: 'sisi_view_' + Defined.use_api,
        page: 2
      });
    },
    onSelect: function onSelect(params, close) {
      Utils.play(params.element);
    }
  };


  // ── UI assets: CSS + HTML fragments ──
  function sisiCssHtml() {
    return [
      '<style>',
      "@charset 'UTF-8';",
      '/* ── card video preview ── */',
      '.sisi-video-preview {',
      '  position: absolute;',
      '  width: 100%;',
      '  height: 100%;',
      '  left: 0;',
      '  top: 0;',
      '  overflow: hidden;',
      '  border-radius: 1em;',
      '}',
      '.sisi-video-preview video {',
      '  position: absolute;',
      '  width: 100%;',
      '  height: 100%;',
      '  left: 0;',
      '  top: 0;',
      '  object-fit: cover;',
      '}',
      '/* ── menu PWA badge ── */',
      '.sisi-menu-pwa-badge {',
      '  position: absolute;',
      '  right: -0.3em;',
      '  bottom: -0.5em;',
      '  color: #fff;',
      '  padding: 0.2em 0.4em;',
      '  font-size: 0.6em;',
      '  border-radius: 0.5em;',
      '  font-weight: 900;',
      '  text-transform: uppercase;',
      '}',
      '</style>'
    ].join('\n');
  }

  var SISI_ICON_SVG = [
    '<svg width="200" height="243" viewBox="0 0 200 243" fill="none" xmlns="http://www.w3.org/2000/svg">',
    '  <path d="M187.714 130.727C206.862 90.1515 158.991 64.2019 100.983 64.2019C42.9759 64.2019 -4.33044 91.5669 10.875 130.727C26.0805 169.888 63.2501 235.469 100.983 234.997C138.716 234.526 168.566 171.303 187.714 130.727Z" stroke="currentColor" stroke-width="15"/>',
    '  <path d="M102.11 62.3146C109.995 39.6677 127.46 28.816 169.692 24.0979C172.514 56.1811 135.338 64.2018 102.11 62.3146Z" stroke="currentColor" stroke-width="15"/>',
    '  <path d="M90.8467 62.7863C90.2285 34.5178 66.0667 25.0419 31.7127 33.063C28.8904 65.1461 68.8826 62.7863 90.8467 62.7863Z" stroke="currentColor" stroke-width="15"/>',
    '  <path d="M100.421 58.5402C115.627 39.6677 127.447 13.7181 85.2149 9C82.3926 41.0832 83.5258 35.4214 100.421 58.5402Z" stroke="currentColor" stroke-width="15"/>',
    '  <rect x="39.0341" y="98.644" width="19.1481" height="30.1959" rx="9.57407" fill="currentColor"/>',
    '  <rect x="90.8467" y="92.0388" width="19.1481" height="30.1959" rx="9.57407" fill="currentColor"/>',
    '  <rect x="140.407" y="98.644" width="19.1481" height="30.1959" rx="9.57407" fill="currentColor"/>',
    '  <rect x="116.753" y="139.22" width="19.1481" height="30.1959" rx="9.57407" fill="currentColor"/>',
    '  <rect x="64.9404" y="139.22" width="19.1481" height="30.1959" rx="9.57407" fill="currentColor"/>',
    '  <rect x="93.0994" y="176.021" width="19.1481" height="30.1959" rx="9.57407" fill="currentColor"/>',
    '</svg>'
  ].join('\n');

  var SISI_FILTER_BUTTON = [
    '<div class="head__action head__settings selector">',
    '  <svg height="36" viewBox="0 0 38 36" fill="none" xmlns="http://www.w3.org/2000/svg">',
    '    <rect x="1.5" y="1.5" width="35" height="33" rx="1.5" stroke="currentColor" stroke-width="3"></rect>',
    '    <rect x="7" y="8" width="24" height="3" rx="1.5" fill="currentColor"></rect>',
    '    <rect x="7" y="16" width="24" height="3" rx="1.5" fill="currentColor"></rect>',
    '    <rect x="7" y="25" width="24" height="3" rx="1.5" fill="currentColor"></rect>',
    '    <circle cx="13.5" cy="17.5" r="3.5" fill="currentColor"></circle>',
    '    <circle cx="23.5" cy="26.5" r="3.5" fill="currentColor"></circle>',
    '    <circle cx="21.5" cy="9.5" r="3.5" fill="currentColor"></circle>',
    '  </svg>',
    '</div>'
  ].join('\n');

  function sisiMenuItemHtml(title) {
    return [
      '<li class="menu__item selector" data-action="sisi">',
      '  <div class="menu__ico">',
      '    ' + SISI_ICON_SVG,
      '  </div>',
      '  <div class="menu__text">' + title + '</div>',
      '</li>'
    ].join('\n');
  }

  function sisiModalHtml(text, boxMac) {
    return [
      '<div class="about">',
      '  <div>' + (text || 'Добавьте идентификатор устройства в init.conf') + '</div>',
      '  <div class="about__contacts">',
      '    <div>',
      '      <small>unic_id</small><br>',
      '      ' + luid,
      '    </div>',
      '    <div>',
      '      <small>box_mac</small><br>',
      '      ' + boxMac,
      '    </div>',
      '  </div>',
      '</div>'
    ].join('\n');
  }

  function injectSisiAssets() {
    if (window.sisi_assets_injected) return;
    window.sisi_assets_injected = true;
    $('body').append(sisiCssHtml());
  }


  function startPlugin() {
    injectSisiAssets();
    window['plugin_sisi_' + Defined.use_api + '_ready'] = true;
    var unic_id = Lampa.Storage.get('sisi_unic_id', '');

    if (!unic_id) {
      unic_id = Lampa.Utils.uid(8).toLowerCase();
      Lampa.Storage.set('sisi_unic_id', unic_id);
    }

    Lampa.Component.add('sisi_' + Defined.use_api, Sisi);
    Lampa.Component.add('sisi_view_' + Defined.use_api, View);
    // addSourceSearch();
    Lampa.Search.addSource(Search);

    function addFilter() {
      var activi;
      var timer;
      var button;

      function openFilter() {
        if (!activi) return;
        var comp = activi.activity.component;
        if (comp && typeof comp.filter === 'function') comp.filter();
      }

      var filterSvg = SISI_FILTER_BUTTON.match(/<svg[\s\S]*<\/svg>/);
      button = Lampa.Head.addIcon(filterSvg ? filterSvg[0] : '', openFilter);
      button.addClass('head__settings');
      button.hide();

      Lampa.Listener.follow('activity', function(e) {
        if (e.type == 'start') activi = e.object;
        clearTimeout(timer);
        timer = setTimeout(function() {
          if (activi) {
            if (activi.component !== 'sisi_view_' + Defined.use_api) {
              button.hide();
              activi = false;
            }
          }
        }, 1000);

        if (e.type == 'start' && e.component == 'sisi_view_' + Defined.use_api) {
          button.show();
          activi = e.object;
        }
      });
    }

    function addSettings() {
      if (window.sisi_add_param_ready) return;
      window.sisi_add_param_ready = true;
      Lampa.SettingsApi.addComponent({
        component: 'sisi',
        name: Lampa.Lang.translate('lampac_sisiname'),
        icon: SISI_ICON_SVG
      });
      Lampa.SettingsApi.addParam({
        component: 'sisi',
        param: {
          name: 'sisi_preview',
          type: 'trigger',
          values: '',
          "default": true
        },
        field: {
          name: 'Предпросмотр',
          description: 'Показывать предпросмотр при наведение на карточку'
        },
        onRender: function onRender(item) {}
      });
      Lampa.SettingsApi.addParam({
        component: 'sisi',
        param: {
          name: 'sisi_history',
          type: 'trigger',
          values: '',
          "default": true
        },
        field: {
          name: 'История',
          description: 'Сохранять историю просмотров'
        },
        onRender: function onRender(item) {}
      });
    }

    function add() {
      var button = $(sisiMenuItemHtml(Lampa.Lang.translate('lampac_sisiname')));

      if (Defined.use_api == 'pwa') {
        var pw = $('<div class="sisi-menu-pwa-badge">p</div>');
        button.find('.menu__ico').css('position', 'relative').append(pw);
      }

      button.on('hover:enter', function() {
        // Проверка и создание Lampa.ParentalControl, если не существует
        if (!Lampa.ParentalControl) {
            Lampa.ParentalControl = {
            query: function(success, error) {
                // По умолчанию всегда разрешает доступ
                if (typeof success === 'function') success();
            }
            };
        }
        Lampa.ParentalControl.query(function() {
          Api.menu(function(data) {
            // let items = [{
            //     title: 'Все'
            // }]
            var items = [];

            if ({push_all} && (Defined.use_api !== 'pwa' || Lampa.Platform.is('android'))) {
              items.push({
                title: 'Все'
              });
            }

            data.forEach(function(a) {
              a.title = Utils.sourceTitle(a.title);
            });
            items = items.concat(data);
            Lampa.Select.show({
              title: 'Сайты',
              items: items,
              onSelect: function onSelect(a) {
                if (a.playlist_url) {
                  Lampa.Activity.push({
                    url: a.playlist_url,
                    title: a.title,
                    component: 'sisi_view_' + Defined.use_api,
                    page: 1
                  });
                } else {
                  Lampa.Activity.push({
                    url: '',
                    title: Lampa.Lang.translate('lampac_sisiname'),
                    component: 'sisi_' + Defined.use_api,
                    page: 1
                  });
                }
              },
              onBack: function onBack() {
                Lampa.Controller.toggle('menu');
              }
            });
          }, function (e) {
            if (typeof e == 'string') modal(e);
          });
        }, function () {});
      });
      $('.menu .menu__list').eq(0).append(button);
    }

    function init() {
      if (window.lampa_settings.sisi_app) {
        Api.menu(function (data) {
          data.forEach(function (a) {
            a.title = Utils.sourceTitle(a.title);

            Lampa.Menu.addButton('<img src="./img/icons/settings/more.svg">', a.title, function () {
              if (a.playlist_url) {
                Lampa.Activity.push({
                  url: a.playlist_url,
                  title: a.title,
                  component: 'sisi_view_' + Defined.use_api,
                  page: 1
                });
              } else {
                Lampa.Activity.push({
                  url: '',
                  title: Lampa.Lang.translate('lampac_sisiname'),
                  component: 'sisi_' + Defined.use_api,
                  page: 1
                });
              }
            });
          });
        });
      } else {
        add();
      }

      addFilter();
      addSettings();
    }

    if (window.appready) init();
    else {
      Lampa.Listener.follow('app', function(e) {
        if (e.type == 'ready') init();
      });
    }
  }

  if (!window['plugin_sisi_' + Defined.use_api + '_ready']) {
    startPlugin();
  }

})();
