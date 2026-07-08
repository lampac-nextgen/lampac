(function () {
    'use strict';

    var lampac_host = '{localhost}';
    var network = new Lampa.Reguest();

    function getToken() {
        return Lampa.Storage.get('token', '')
            || Lampa.Storage.get('lampac_unic_id', '')
            || Lampa.Storage.get('account_email', '');
    }

    function apiUrl(path) {
        var token = getToken();
        var sep = path.indexOf('?') >= 0 ? '&' : '?';
        return lampac_host + path + (token ? sep + 'token=' + encodeURIComponent(token) : '');
    }

    function pad(n) { return n < 10 ? '0' + n : '' + n; }

    var TMDB_KEY = '';

    // TMDB image/api с поддержкой прокси — используем встроенные методы Lampa
    function tmdbImage(url) {
        try { return Lampa.TMDB.image(url); } catch(e) {}
        return 'https://image.tmdb.org/' + url;
    }

    function tmdbApi(path) {
        try { return Lampa.TMDB.api(path + '&api_key=' + (Lampa.TMDB.key ? Lampa.TMDB.key() : TMDB_KEY)); } catch(e) {}
        return 'https://api.themoviedb.org/3/' + path + '&api_key=' + TMDB_KEY;
    }

    // =============================================
    //  Компонент подписок — построен по образцу
    //  src/interaction/items/category/base.js
    // =============================================
    function TgSubscribesComponent(object) {
        var scroll = new Lampa.Scroll({ mask: true, over: true, step: 250, end_ratio: 2 });
        var items  = [];
        var html   = document.createElement('div');
        var body   = $(document.createElement('div'));  // jQuery объект, как в Lampa
        var last;
        var active = 0;

        this.create = function () {
            var self = this;

            // Как в base.js: сначала scroll.minus(), потом append body
            scroll.minus();
            scroll.append(body);
            html.appendChild(scroll.render(true));

            // Layer.visible при скролле — для ленивой загрузки изображений
            scroll.onScroll = function () {
                Lampa.Layer.visible(scroll.render(true));
            };

            self.activity.loader(true);

            var net = new Lampa.Reguest();
            net.timeout(15000);

            net['native'](apiUrl('/api/tg/subscriptions'), function (result) {
                if (!result.linked || !result.results || !result.results.length) {
                    self.empty();
                    return;
                }

                var subs     = result.results;
                var lang     = Lampa.Storage.field('tmdb_lang') || 'ru-RU';
                var total    = subs.length;
                var done     = 0;
                var tmdbData = {};

                function buildCards() {
                    // Классы сетки из items.js: mapping--grid + cols--6
                    body.addClass('mapping--grid');
                    body.addClass('cols--6');

                    var frag = document.createDocumentFragment();

                    subs.forEach(function (sub) {
                        var t = tmdbData[sub.tmdb_id] || {};

                        var cardData = {
                            id:             sub.tmdb_id,
                            tmdb_id:        sub.tmdb_id,
                            tmdb:           sub.tmdb_id,
                            name:           t.name            || sub.title,
                            title:          t.name            || sub.title,
                            original_name:  t.original_name   || sub.title,
                            poster_path:    t.poster_path     || '',
                            backdrop_path:  t.backdrop_path   || '',
                            vote_average:   t.vote_average    || 0,
                            first_air_date: t.first_air_date  || '',
                            number_of_seasons: t.number_of_seasons || sub.last_season,
                            source: 'tmdb'
                        };

                        if (t.poster_path)
                            cardData.img = tmdbImage('t/p/w300' + t.poster_path);

                        var card = new Lampa.Card(cardData, { object: object, card_category: true });
                        card.create();

                        var season  = parseInt(sub.last_season)  || 0;
                        var episode = parseInt(sub.last_episode) || 0;
                        var voice   = (sub.voice || '').trim();

                        // Бейдж подписки — без pad() как в стандартном Subscribe модуле Lampa
                        var badge = document.createElement('div');
                        badge.className = 'card__subscribe';
                        badge.innerHTML =
                            '<div class="card__subscribe-status on"></div>' +
                            '<div class="card__subscribe-position">S' + season + ' E' + episode + '</div>' +
                            '<div class="card__subscribe-voice">' + (voice || 'Любая') + '</div>';
                        var view = card.render(true).querySelector('.card__view');
                        if (view) view.after(badge);

                        card.onFocus = function (target, data) {
                            last   = target;
                            active = items.indexOf(card);
                            scroll.update($(target), true);
                            // Используем стандартную функцию Lampa — сама выбирает постер/backdrop по настройкам
                            Lampa.Background.change(Lampa.Utils.cardImgBackground(data));
                        };

                        card.onEnter = function (target, data) {
                            Lampa.Activity.push({
                                url: '', component: 'full',
                                id: data.tmdb_id || data.id,
                                method: 'tv', card: data, source: 'tmdb'
                            });
                        };

                        frag.appendChild(card.render(true));
                        items.push(card);
                    });

                    body.append(frag);

                    // Устанавливаем фон на первую карточку при открытии
                    if (items.length > 0) {
                        Lampa.Background.change(Lampa.Utils.cardImgBackground(items[0].data));
                    }

                    // Layer.visible — запускает ленивую загрузку изображений в карточках
                    Lampa.Layer.visible(scroll.render(true));

                    self.activity.loader(false);
                    self.activity.toggle();
                }

                // Параллельная загрузка TMDB через Lampa.Network (с кешем и прокси)
                subs.forEach(function (sub) {
                    var url = tmdbApi('tv/' + sub.tmdb_id + '?language=' + lang);
                    Lampa.Network.silent(url, function (t) {
                            if (t && !t.status_code) tmdbData[sub.tmdb_id] = t;
                            if (++done >= total) buildCards();
                        },
                        function () { if (++done >= total) buildCards(); },
                        false, { cache: { life: 60 * 24 } }
                    );
                });

            }, function () { self.empty(); });
        };

        this.empty = function () {
            var empty = new Lampa.Empty();
            html.appendChild(empty.render(true));
            this.start = empty.start.bind(empty);
            this.activity.loader(false);
            this.activity.toggle();
        };

        // start() — точная копия base.js
        this.start = function () {
            var self = this;
            Lampa.Controller.add('content', {
                link: self,
                invisible: true,
                toggle: function () {
                    scroll.restorePosition ? scroll.restorePosition() : null;
                    Lampa.Controller.collectionSet(scroll.render(true));
                    Lampa.Controller.collectionFocus(last || false, scroll.render(true));
                },
                left:  function () { if (Navigator.canmove('left'))  Navigator.move('left');  else Lampa.Controller.toggle('menu'); },
                right: function () { if (Navigator.canmove('right')) Navigator.move('right'); },
                up:    function () { if (Navigator.canmove('up'))    Navigator.move('up');    else Lampa.Controller.toggle('head'); },
                down:  function () { if (Navigator.canmove('down'))  Navigator.move('down'); },
                back:  function () { Lampa.Activity.backward(); }
            });
            Lampa.Controller.toggle('content');
        };

        this.pause   = function () {};
        this.stop    = function () {};
        this.render  = function (js) { return js ? html : $(html); };
        this.destroy = function () {
            items.forEach(function (c) { try { c.destroy(); } catch(e) {} });
            scroll.destroy();
            html.remove();
            items = [];
        };
    }

    // =============================================
    //  Меню
    // =============================================
    var MENU_ICON   = '<svg><use xlink:href="#sprite-subscribes"></use></svg>';
    var MENU_TITLE  = 'TG Подписки';
    var MENU_ACTION = 'tg_subscribes';

    function openSubscribesPage() {
        Lampa.Activity.push({ url: '', title: MENU_TITLE, component: MENU_ACTION, page: 1 });
    }

    function addMenu() {
        if ($('.menu__item[data-action="' + MENU_ACTION + '"]').length) return;
        Lampa.Menu.addButton(MENU_ICON, MENU_TITLE, openSubscribesPage).attr('data-action', MENU_ACTION);
        try {
            var sort = Lampa.Storage.get('menu_sort', '[]');
            if (typeof sort === 'string') sort = JSON.parse(sort);
            if (Array.isArray(sort) && sort.indexOf(MENU_TITLE) === -1) {
                sort.push(MENU_TITLE);
                Lampa.Storage.set('menu_sort', JSON.stringify(sort));
            }
        } catch(e) {}
    }

    function registerComponent() {
        Lampa.Component.add(MENU_ACTION, TgSubscribesComponent);
        setTimeout(addMenu, 500);
    }

    // =============================================
    //  Перехват кнопки 🔔 в карточке сериала
    // =============================================
    function initSubscribeOverride() {
        Lampa.Listener.follow('full', function (e) {
            if (e.type === 'complite')
                setTimeout(function () { overrideSubscribeButton(e.object); }, 300);
        });
    }

    function overrideSubscribeButton(obj) {
        if (!obj || !obj.card || !obj.card.number_of_seasons) return;
        var card = obj.card;
        var button;
        try { button = obj.activity.render().find('.button--subscribe'); } catch(e) { return; }
        if (!button || !button.length) return;

        button.removeClass('hide');
        button.off('hover:enter');

        checkStatus(card.id, function (status) {
            if (status.subscribed) button.addClass('active').find('path').attr('fill', 'currentColor');
            button.on('hover:enter', function () {
                if (!status.linked) showLinkDialog();
                else showVoiceMenu(card, button, status);
            });
        });
    }

    function showVoiceMenu(card, button, currentStatus) {
        var title  = card.name || card.title || '';
        var year   = card.first_air_date ? parseInt(card.first_air_date) : (card.year || 0);
        var tmdbId = card.id || 0;

        Lampa.Loading.start(function () { network.clear(); Lampa.Loading.stop(); });
        network.clear();
        network.timeout(30000);
        network['native'](
            apiUrl('/api/tg/voices?title=' + encodeURIComponent(title) + '&year=' + year + '&season=0&tmdb_id=' + tmdbId),
            function (result) {
                Lampa.Loading.stop();
                var season = result.season || card.number_of_seasons || 1;
                var items  = [];

                if (currentStatus && currentStatus.subscribed && currentStatus.voices) {
                    currentStatus.voices.forEach(function (v) {
                        items.push({ title: '❌ Отписаться: ' + (v || 'Любая озвучка'), unsubscribe: true, voice: v });
                    });
                    if (items.length) items.push({ title: '', separator: true });
                }

                items.push({ title: '🔔 Любая озвучка (оригинал)', subtitle: 'При выходе серии', voice: '', orid: '', voice_id: 0, collaps_orid: '', voice_source: '' });

                if (result.success && result.voices && result.voices.length) {
                    items.push({ title: 'Озвучки:', separator: true });
                    result.voices.forEach(function (v) {
                        var src   = v.source || 'mirage';
                        var badge = src === 'collaps' ? ' [C]' : src === 'rhs' ? ' [R]' : src === 'videohub' ? ' [V]' : '';
                        items.push({ title: '🎙 ' + v.name + badge, voice: v.name, orid: result.orid || '', voice_id: v.id || 0, collaps_orid: result.collaps_orid || '', kp_id: result.kp_id || 0, voice_source: src });
                    });
                }

                Lampa.Select.show({
                    title: 'TG Уведомления', items: items,
                    onSelect: function (a) {
                        Lampa.Controller.toggle('content');
                        if (a.unsubscribe) doUnsubscribe(card, button, a.voice);
                        else if (typeof a.voice !== 'undefined') doSubscribe(card, a.voice, season, button, a.orid, a.voice_id, a.collaps_orid, a.voice_source, a.kp_id);
                    },
                    onBack: function () { Lampa.Controller.toggle('content'); }
                });
            },
            function () {
                Lampa.Loading.stop();
                var items = [{ title: '🔔 Подписаться (без озвучки)', voice: '', orid: '', voice_id: 0 }];
                if (currentStatus && currentStatus.subscribed) items.unshift({ title: '❌ Отписаться', unsubscribe: true, voice: '' });
                Lampa.Select.show({
                    title: 'TG Уведомления', items: items,
                    onSelect: function (a) {
                        Lampa.Controller.toggle('content');
                        if (a.unsubscribe) doUnsubscribe(card, button, null);
                        else doSubscribe(card, '', card.number_of_seasons || 1, button, '', 0);
                    },
                    onBack: function () { Lampa.Controller.toggle('content'); }
                });
            }
        );
    }

    function doSubscribe(card, voice, season, button, orid, voiceId, collapsOrid, voiceSource, kpId) {
        network.clear();
        network['native'](apiUrl('/api/tg/subscribe'), function (r) {
            if (r.success) {
                Lampa.Noty.show(voice ? '🔔 Подписка: ' + voice : '🔔 Подписка оформлена!');
                button.addClass('active').find('path').attr('fill', 'currentColor');
            } else if (r.msg === 'not_linked') {
                showLinkDialog();
            } else {
                Lampa.Noty.show('Ошибка: ' + (r.msg || ''));
            }
        }, function () {
            Lampa.Noty.show('Ошибка подписки');
        }, JSON.stringify({ tmdb_id: card.id, title: card.name || card.title || '', voice: voice, season: season, episode: 0, mirage_orid: orid || '', mirage_voice_id: voiceId || 0, voice_episode: 0, collaps_orid: collapsOrid || '', voice_source: voiceSource || '', kp_id: kpId || 0 }),
        { dataType: 'json', contentType: 'application/json' });
    }

    function doUnsubscribe(card, button, voice) {
        network.clear();
        network['native'](apiUrl('/api/tg/unsubscribe'), function (r) {
            if (r.success) {
                Lampa.Noty.show('Подписка отменена');
                checkStatus(card.id, function (st) {
                    if (!st.subscribed) button.removeClass('active').find('path').attr('fill', 'transparent');
                });
            }
        }, function () { Lampa.Noty.show('Ошибка'); },
        JSON.stringify({ tmdb_id: card.id, voice: voice }),
        { dataType: 'json', contentType: 'application/json' });
    }

    function checkStatus(tmdb_id, callback) {
        var n = new Lampa.Reguest();
        n.timeout(5000);
        n['native'](apiUrl('/api/tg/status?tmdb_id=' + tmdb_id),
            function (r) { callback(r || { success: false, linked: false, subscribed: false, voices: [] }); },
            function ()  { callback({ success: false, linked: false, subscribed: false, voices: [] }); }
        );
    }

    function showLinkDialog() {
        var n = new Lampa.Reguest();
        n['native'](apiUrl('/api/tg/link'), function (r) {
            if (!r.success || !r.link) { Lampa.Noty.show('Бот не запущен'); return; }
            Lampa.Select.show({
                title: 'Привязка Telegram',
                items: [
                    { title: 'Открыть ссылку', subtitle: r.link, link: r.link },
                    { title: 'Скопировать ссылку', show_link: r.link }
                ],
                onSelect: function (a) {
                    Lampa.Controller.toggle('content');
                    if (a.link) {
                        if (typeof Android !== 'undefined' && Android.openBrowser) Android.openBrowser(a.link);
                        else if (window.open) window.open(a.link, '_blank');
                    }
                    if (a.show_link) Lampa.Noty.show(a.show_link);
                },
                onBack: function () { Lampa.Controller.toggle('content'); }
            });
        }, function () { Lampa.Noty.show('Модуль TelegramBot недоступен'); });
    }

    // =============================================
    //  Init
    // =============================================
    function init() {
        registerComponent();
        initSubscribeOverride();
        console.log('[TG-Notify] Plugin loaded v17');
    }

    if (window.appready) init();
    else Lampa.Listener.follow('app', function (e) { if (e.type === 'ready') init(); });

})();
