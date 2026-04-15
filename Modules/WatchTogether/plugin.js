(function () {
    'use strict';

    if (window.watchtogether_plugin_started) {
        console.log('[WT] Plugin already running, aborting second start.');
        return;
    }
    window.watchtogether_plugin_started = true;

    console.log('[WT] Plugin initialization started.');

    var _rawLang = (Lampa.Storage.get('language') || 'en').toLowerCase();
    var i18n = {
        ru: {
            create_fail: 'Не удалось создать комнату WatchTogether',
            create_err: 'Ошибка сервера при создании комнаты',
            no_movie_id: 'Ошибка: сервер не вернул ID фильма',
            room_not_found: function (id) { return 'Комната ' + id + ' не найдена'; },
            join_err: 'Ошибка сервера при получении данных комнаты',
            joined_room: function (id) { return 'Вы вошли в комнату: ' + id; },
            input_title: 'ID Комнаты',
            badge_room: 'Комната',
            badge_viewers: 'Зрителей',
            kicked: 'Вы вошли в эту комнату с другого устройства.',
            copied: function (id) { return 'ID комнаты скопирован: ' + id; },
            click_to_copy: 'Нажмите чтобы скопировать'
        },
        en: {
            create_fail: 'Failed to create WatchTogether room',
            create_err: 'Server error while creating room',
            no_movie_id: 'Error: server did not return movie ID',
            room_not_found: function (id) { return 'Room ' + id + ' not found'; },
            join_err: 'Server error while fetching room data',
            joined_room: function (id) { return 'You joined room: ' + id; },
            input_title: 'Room ID',
            badge_room: 'Room',
            badge_viewers: 'Viewers',
            kicked: 'You joined this room from another device.',
            copied: function (id) { return 'Room ID copied: ' + id; },
            click_to_copy: 'Click to copy'
        },
        uk: {
            create_fail: 'Не вдалося створити кімнату WatchTogether',
            create_err: 'Помилка сервера під час створення кімнати',
            no_movie_id: 'Помилка: сервер не повернув ID фільму',
            room_not_found: function (id) { return 'Кімнату ' + id + ' не знайдено'; },
            join_err: 'Помилка сервера під час отримання даних кімнати',
            joined_room: function (id) { return 'Ви увійшли до кімнати: ' + id; },
            input_title: 'ID Кімнати',
            badge_room: 'Кімната',
            badge_viewers: 'Глядачів',
            kicked: 'Ви увійшли до цієї кімнати з іншого пристрою.',
            copied: function (id) { return 'ID кімнати скопійовано: ' + id; },
            click_to_copy: 'Натисніть щоб скопіювати'
        }
    };
    var T = i18n[_rawLang] || i18n['en'];

    var unic_id = Lampa.Storage.get('lampac_unic_id', '');
    if (!unic_id) {
        unic_id = Lampa.Utils.uid(8).toLowerCase();
        Lampa.Storage.set('lampac_unic_id', unic_id);
        console.log('[WT] Generated new unic_id:', unic_id);
    } else {
        console.log('[WT] Loaded existing unic_id:', unic_id);
    }

    var localhost = '{localhost}/';
    var inRoom = false;
    var currentRoomId = null;

    // Player and Sync variables
    var ws;
    var syncInterval;
    var isSystemSyncing = false;
    var lastUserActionTime = 0;

    // Initial Sync Lock variables
    var initialSyncLock = false;
    var targetInitialState = null;

    var expectedState = {
        seek: -1,
        play: false,
        pause: false
    };
    var currentRoomMemberCount = 1;

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
        return url;
    }

    function getTmdbId(card) {
        if (!card) return 0;
        return card.id || card.tmdb_id || 0;
    }

    function createRoom(card) {
        var tmdb_id = getTmdbId(card);
        var title = card.title || card.name || card.original_title || '';
        var source = card.source || 'tmdb';
        var type = (card.name || card.number_of_seasons || card.first_air_date) ? 'tv' : 'movie';

        console.log('[WT] createRoom triggered. Title:', title, '| ID:', tmdb_id, '| Source:', source, '| Type:', type);

        var url = account(localhost + 'watchtogether/create?title=' + encodeURIComponent(title) + '&tmdb_id=' + tmdb_id + '&source=' + encodeURIComponent(source) + '&type=' + encodeURIComponent(type));

        Lampa.Network.silent(url, function (res) {
            console.log('[WT] createRoom response:', res);
            if (res && res.id) {
                joinRoom(res.id, card);
            } else {
                Lampa.Noty.show(T.create_fail);
            }
        }, function (a, c) {
            console.error('[WT] createRoom Network Error:', a, c);
            Lampa.Noty.show(T.create_err);
        });
    }

    function joinRoom(roomId, card) {
        console.log('[WT] joinRoom triggered for room:', roomId);

        var url = account(localhost + 'watchtogether/info?id=' + encodeURIComponent(roomId));

        Lampa.Network.silent(url, function (res) {
            console.log('[WT] info response:', res);
            if (res && res.id) {
                var serverTmdbId = res.tmdb_id || res.tmdbId || res.tmdb_Id;
                var currentCardId = getTmdbId(card);

                var cleanTitle = res.title || '';
                var source = res.source || 'tmdb';
                var type = res.type || 'movie';
                var roomState = res.state || 'paused';
                var roomPos = res.position || 0;

                if (card && (!serverTmdbId || currentCardId == serverTmdbId)) {
                    console.log('[WT] Joining room with existing card data.');
                    _doJoinAndPlay(res.id, card, roomState, roomPos);
                }
                else if (serverTmdbId) {
                    console.log('[WT] Fetching from API for ID:', serverTmdbId, '| Source:', source, '| Type:', type);

                    var apiSource = Lampa.Api.sources[source];

                    if (!apiSource || typeof apiSource.get !== 'function') {
                        console.warn('[WT] API Source "' + source + '" not found, falling back to TMDB.');
                        apiSource = Lampa.Api.sources.tmdb;
                    }

                    var runFallback = function () {
                        console.log('[WT] API fetch failed. Using fallback dummy card.');
                        var dummyCard = {
                            id: serverTmdbId,
                            tmdb_id: serverTmdbId,
                            title: cleanTitle,
                            original_title: cleanTitle,
                            name: cleanTitle,
                            original_name: cleanTitle,
                            source: source
                        };
                        if (type === 'tv') dummyCard.number_of_seasons = 1;

                        _doJoinAndPlay(res.id, dummyCard, roomState, roomPos);
                    };

                    apiSource.get(type + '/' + serverTmdbId, {}, function (data) {
                        data.source = source;
                        _doJoinAndPlay(res.id, data, roomState, roomPos);
                    }, function () {
                        console.log('[WT] Exact API fetch failed. Falling back.');
                        runFallback();
                    });
                }
                else {
                    Lampa.Noty.show(T.no_movie_id);
                }
            } else {
                Lampa.Noty.show(T.room_not_found(roomId));
            }
        }, function (a, c) {
            console.error('[WT] joinRoom Network Error:', a, c);
            Lampa.Noty.show(T.join_err);
        });
    }

    function _doJoinAndPlay(roomId, card, state, position) {
        console.log('[WT] Executing _doJoinAndPlay. Room:', roomId);
        currentRoomId = roomId;
        inRoom = true;

        initialSyncLock = true;
        targetInitialState = { state: state || 'paused', position: position || 0 };
        console.log('[WT] Initial Sync Lock ENGAGED. Target:', targetInitialState);

        Lampa.Noty.show(T.joined_room(roomId));

        if (ws && ws.readyState === 1) {
            ws.send(JSON.stringify({
                method: 'watchtogether_join',
                args: [currentRoomId, unic_id]
            }));
        }

        var searchTitle = card.title || card.name || '';
        var searchOriginal = card.original_title || card.original_name || '';

        Lampa.Activity.push({
            url: '',
            title: 'Watch Together (' + roomId + ')',
            component: 'lampac',
            search: searchTitle,
            search_one: searchTitle,
            search_two: searchOriginal,
            movie: card,
            page: 1
        });
    }

    function showJoinMenu() {
        Lampa.Input.edit({
            title: T.input_title,
            value: '',
            free: true,
            nosave: true
        }, function (new_value) {
            if (new_value) joinRoom(new_value.toUpperCase(), null);
        });
    }

    // UI Helpers
    function getVideo() {
        var vid = null;
        if (typeof Lampa.PlayerVideo !== 'undefined' && Lampa.PlayerVideo.video) {
            vid = Lampa.PlayerVideo.video();
        }
        if (!vid) {
            vid = document.querySelector('.player-video__display video') || document.querySelector('.player video') || document.querySelector('video');
        }
        return vid;
    }

    function copyRoomId() {
        if (!currentRoomId) return;
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(currentRoomId).then(function () {
                Lampa.Noty.show(T.copied(currentRoomId));
            }).catch(function (err) {
                console.error('[WT] Copy failed', err);
            });
        }
    }

    function updateRoomUI(count) {
        if (!inRoom || !currentRoomId) return;
        var nameContainer = $('.player-info__name, .player-panel__name');

        if (count !== undefined) currentRoomMemberCount = count;

        if (nameContainer.length && !$('.wt-room-badge').length) {
            var badge = $('<div class="wt-room-badge" style="display:inline-block; margin-left: 15px; padding: 4px 12px; background: rgba(255,255,255,0.15); border-radius: 6px; font-size: 0.85em; color: #fff; cursor: pointer;"></div>');
            badge.on('click hover:enter', copyRoomId);
            nameContainer.after(badge);
        }

        if ($('.wt-room-badge').length) {
            $('.wt-room-badge').html(T.badge_room + ': <b style="color:#00e676;" title="' + T.click_to_copy + '">' + currentRoomId + '</b> | ' + T.badge_viewers + ': <b>' + currentRoomMemberCount + '</b>');
        }
    }

    function applySync(vid, state, position) {
        if (vid.currentTime === undefined) return;

        var diff = vid.currentTime - position;
        var absDiff = Math.abs(diff);

        console.log('[WT] Sync. State:', state, '| Server Pos:', position, '| Local Pos:', vid.currentTime, '| Diff:', diff);

        if (absDiff > 5) {
            expectedState.seek = position;
            vid.currentTime = position;
            console.log('[WT] Hard seeked local video to', position);
        } else if (absDiff > 1) {
            var rate = diff > 0 ? 0.92 : 1.08;
            vid.playbackRate = rate;
            var correctionMs = (absDiff / 0.08) * 1000;
            console.log('[WT] Soft rate correction:', rate, 'for', Math.round(correctionMs), 'ms');
            if (vid._wt_rate_timeout) clearTimeout(vid._wt_rate_timeout);
            vid._wt_rate_timeout = setTimeout(function () {
                vid._wt_rate_timeout = null;
                if (vid.playbackRate !== 1) vid.playbackRate = 1;
            }, Math.min(correctionMs, 12000));
        }

        if (state === 'playing' && vid.paused) {
            expectedState.play = true;
            console.log('[WT] Forcing play via Lampa API...');
            if (typeof Lampa.PlayerVideo !== 'undefined' && Lampa.PlayerVideo.play) {
                Lampa.PlayerVideo.play();
            } else {
                var playPromise = vid.play();
                if (playPromise !== undefined) playPromise.catch(function (err) { });
            }
        } else if (state === 'paused' && !vid.paused) {
            expectedState.pause = true;
            vid.playbackRate = 1;
            console.log('[WT] Forcing pause via Lampa API...');
            if (typeof Lampa.PlayerVideo !== 'undefined' && Lampa.PlayerVideo.pause) {
                Lampa.PlayerVideo.pause();
            } else {
                vid.pause();
            }
        }
    }

    // Add Create Room button
    Lampa.Listener.follow('full', function (e) {
        if (e.type == 'complite') {
            var button = $('<div class="full-start__button selector view--watchtogether"><svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path><circle cx="9" cy="7" r="4"></circle><path d="M23 21v-2a4 4 0 0 0-3-3.87"></path><path d="M16 3.13a4 4 0 0 1 0 7.75"></path></svg><span>Watch Together</span></div>');
            var cardData = (e.data && e.data.movie) || (e.object.activity && e.object.activity.object ? e.object.activity.object.item : null) || e.object.item;

            button.on('hover:enter', function () {
                if (cardData) createRoom(cardData);
            });

            var container = e.object.activity.render();
            if (container) {
                var targetContainer = container.find('.full-start-new__buttons');
                if (!targetContainer.length) targetContainer = container.find('.full-start__buttons');
                if (targetContainer.length) targetContainer.append(button);
            }
        }
    });

    // Add Join Room button
    var headButtonAdded = false;
    Lampa.Listener.follow('app', function (e) {
        if (e.type == 'ready' && !headButtonAdded) {
            headButtonAdded = true;
            var headBtn = $('<div class="head__action selector"><svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path><circle cx="9" cy="7" r="4"></circle><path d="M23 21v-2a4 4 0 0 0-3-3.87"></path><path d="M16 3.13a4 4 0 0 1 0 7.75"></path></svg></div>');
            headBtn.on('hover:enter', showJoinMenu);
            $('.head__actions').append(headBtn);
        }
    });

    // WebSocket logic
    function connectWs() {
        if (!window.lampa_nws_url) {
            var protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
            var host = localhost.replace('https://', '').replace('http://', '').replace(/\/$/, '');
            window.lampa_nws_url = protocol + '//' + host + '/nws';
        }

        if (ws && ws.readyState === 1) return;

        console.log('[WT] Connecting to WebSocket:', window.lampa_nws_url);
        ws = new WebSocket(window.lampa_nws_url);

        ws.onopen = function () {
            console.log('[WT] WebSocket opened.');
            if (inRoom && currentRoomId) {
                ws.send(JSON.stringify({
                    method: 'watchtogether_join',
                    args: [currentRoomId, unic_id]
                }));
            }
        };

        ws.onmessage = function (e) {
            try {
                var data = JSON.parse(e.data);
                console.log('[WT] WS Received:', data.method, data.args);

                if (data.method == 'watchtogether_members') {
                    updateRoomUI(data.args[0]);
                } else if (data.method == 'watchtogether_kicked') {
                    Lampa.Noty.show(T.kicked);
                    inRoom = false;
                    currentRoomId = null;
                } else if (data.method == 'watchtogether_sync_update') {
                    if (!inRoom) return;

                    var state = data.args[0];
                    var position = data.args[1];

                    if (initialSyncLock) {
                        console.log('[WT] Updating Initial Sync Target from fresh WS sync.');
                        targetInitialState = { state: state, position: position };
                        return;
                    }

                    var vid = getVideo();
                    if (!vid) return;

                    var timeSinceAction = Date.now() - lastUserActionTime;
                    if (timeSinceAction < 2000) {
                        console.log('[WT] Race condition prevented. Ignoring incoming state:', state, '. Re-asserting local state.');
                        sendSyncState(vid.paused ? 'paused' : 'playing');
                        return;
                    }

                    isSystemSyncing = true;
                    applySync(vid, state, position);
                    setTimeout(function () { isSystemSyncing = false; }, 500);
                }
            } catch (err) { }
        };

        ws.onerror = function (err) { console.error('[WT] WebSocket Error:', err); };
        ws.onclose = function () { setTimeout(connectWs, 5000); };
    }

    function sendSyncState(state) {
        if (!inRoom || initialSyncLock || isSystemSyncing) return;
        var vid = getVideo();

        if (ws && ws.readyState === 1 && vid) {
            console.log('[WT] Sending sync state ->', state, '| Pos:', vid.currentTime);
            ws.send(JSON.stringify({
                method: 'watchtogether_sync',
                args: [currentRoomId, unic_id, state, vid.currentTime || 0, 0, 0]
            }));
        }
    }

    setInterval(function () {
        if (!inRoom) {
            $('.wt-room-badge').remove();
            return;
        }

        var vid = getVideo();

        if (vid) {
            updateRoomUI();

            if (!vid._wt_hooked) {
                vid._wt_hooked = true;
                console.log('[WT] SUCCESS: Video element found and hooked!');

                var enforceInitialState = function () {
                    if (!initialSyncLock || !targetInitialState) return;
                    console.log('[WT] Enforcing Initial Sync Lock State:', targetInitialState);

                    if (Math.abs(vid.currentTime - targetInitialState.position) > 2) {
                        expectedState.seek = targetInitialState.position;
                        vid.currentTime = targetInitialState.position;
                    }

                    if (targetInitialState.state === 'paused') {
                        expectedState.pause = true;
                        if (typeof Lampa.PlayerVideo !== 'undefined' && Lampa.PlayerVideo.pause) {
                            Lampa.PlayerVideo.pause();
                        } else {
                            vid.pause();
                        }
                    } else {
                        expectedState.play = true;
                        if (typeof Lampa.PlayerVideo !== 'undefined' && Lampa.PlayerVideo.play) {
                            Lampa.PlayerVideo.play();
                        } else {
                            var p = vid.play();
                            if (p) p.catch(function () { });
                        }
                    }

                    if (!vid._wt_enforce_timeout) {
                        vid._wt_enforce_timeout = setTimeout(function () {
                            if (initialSyncLock) {
                                console.log('[WT] Initial Sync Lock RELEASED after 3s of playback readiness.');
                                initialSyncLock = false;
                                targetInitialState = null;
                            }
                        }, 3000);
                    }
                };

                if (vid.readyState >= 1) {
                    enforceInitialState();
                } else {
                    vid.addEventListener('loadedmetadata', enforceInitialState);
                }
                vid.addEventListener('canplay', enforceInitialState);

                vid.addEventListener('play', function () {
                    if (initialSyncLock) {
                        if (targetInitialState && targetInitialState.state === 'paused') {
                            console.log('[WT] Blocking Lampa Auto-Play (Initial sync says paused)');
                            if (typeof Lampa.PlayerVideo !== 'undefined' && Lampa.PlayerVideo.pause) {
                                Lampa.PlayerVideo.pause();
                            } else {
                                vid.pause();
                            }
                        }
                        return;
                    }

                    var wasExpected = expectedState.play;
                    expectedState.play = false;

                    if (isSystemSyncing || wasExpected) return;

                    console.log('[WT] Native PLAY event fired');
                    lastUserActionTime = Date.now();
                    sendSyncState('playing');
                });

                vid.addEventListener('pause', function () {
                    if (vid._wt_rate_timeout) { clearTimeout(vid._wt_rate_timeout); vid._wt_rate_timeout = null; }
                    vid.playbackRate = 1;

                    if (initialSyncLock) return;

                    var wasExpected = expectedState.pause;
                    expectedState.pause = false;

                    if (isSystemSyncing || wasExpected) return;

                    console.log('[WT] Native PAUSE event fired');
                    lastUserActionTime = Date.now();
                    sendSyncState('paused');
                });

                vid.addEventListener('seeked', function () {
                    if (!isSystemSyncing) {
                        if (vid._wt_rate_timeout) { clearTimeout(vid._wt_rate_timeout); vid._wt_rate_timeout = null; }
                        vid.playbackRate = 1;
                    }

                    if (initialSyncLock) {
                        console.log('[WT] Blocking Lampa Local Seek. Restoring server position.');
                        if (targetInitialState && Math.abs(vid.currentTime - targetInitialState.position) > 2) {
                            expectedState.seek = targetInitialState.position;
                            vid.currentTime = targetInitialState.position;
                        }
                        return;
                    }

                    if (isSystemSyncing) return;

                    if (expectedState.seek !== -1) {
                        if (Math.abs(vid.currentTime - expectedState.seek) < 1.5) {
                            expectedState.seek = -1;
                            return;
                        } else {
                            console.log('[WT] System seek overridden by local seek. Forcing expected:', expectedState.seek);
                            vid.currentTime = expectedState.seek;
                            return;
                        }
                    }
                    console.log('[WT] Native SEEKED event fired. Is paused?', vid.paused);
                    lastUserActionTime = Date.now();
                    sendSyncState(vid.paused ? 'paused' : 'playing');
                });

                clearInterval(syncInterval);
                syncInterval = setInterval(function () {
                    if (inRoom && vid && !vid.paused && expectedState.seek === -1 && !initialSyncLock && !isSystemSyncing) {
                        sendSyncState('playing');
                    }
                }, 5000);
            }
        }
    }, 1000);

    if (typeof Lampa.Player !== 'undefined' && Lampa.Player.listener) {
        Lampa.Player.listener.follow('destroy', function () {
            console.log('[WT] Player destroyed. Leaving room.');
            var wasInRoom = inRoom;

            clearInterval(syncInterval);
            $('.wt-room-badge').remove();

            if (inRoom && ws && ws.readyState === 1) {
                ws.send(JSON.stringify({
                    method: 'watchtogether_leave',
                    args: [currentRoomId, unic_id]
                }));
            }

            var vidToUnhook = getVideo();
            if (vidToUnhook) {
                vidToUnhook._wt_hooked = false;
                if (vidToUnhook._wt_rate_timeout) { clearTimeout(vidToUnhook._wt_rate_timeout); vidToUnhook._wt_rate_timeout = null; }
                vidToUnhook.playbackRate = 1;
            }

            expectedState.play = false;
            expectedState.pause = false;
            expectedState.seek = -1;
            initialSyncLock = false;
            targetInitialState = null;
            isSystemSyncing = false;

            inRoom = false;
            currentRoomId = null;

            if (wasInRoom) {
                setTimeout(function () {
                    var active = Lampa.Activity.active();
                    if (active && active.title && active.title.indexOf('Watch Together') !== -1) {
                        console.log('[WT] Forcing activity backward to completely exit room UI.');
                        Lampa.Activity.backward();
                    }
                }, 300);
            }
        });
    }

    window.addEventListener('beforeunload', function () {
        if (inRoom && ws && ws.readyState === 1) {
            ws.send(JSON.stringify({
                method: 'watchtogether_leave',
                args: [currentRoomId, unic_id]
            }));
        }
    });

    connectWs();

})();