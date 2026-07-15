(function () {
    var taskId = null;
    var heartbeatTimer = null;

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

    function resolveMediaUrl(data) {
        if (data && data.url) return data.url;
        return '';
    }

    function isMkvSource(data) {
        var url = resolveMediaUrl(data);
        if (!url) return false;

        if (
            /\/dlna\/stream(?:\?|$)/i.test(url) &&
            /[?&]path=[^&#]*\.mkv(?:[&#]|$)/i.test(url)
        ) {
            return true;
        }

        url = url.split('#')[0].split('?')[0];

        return (
            /\.mkv$/i.test(url) ||
            /\.avi$/i.test(url) ||
            /\/lite\/pidtor\//i.test(url)
        );
    }

    function nameAudioCodec(capsName) {
        var codec = (capsName || '')
            .replace(/^audio\/x-/i, '')
            .replace(/^audio\//i, '')
            .toLowerCase();

        var names = {
            ac3: 'AC-3',
            eac3: 'E-AC-3',
            aac: 'AAC',
            mp3: 'MP3',
            opus: 'Opus',
            vorbis: 'Vorbis',
            flac: 'FLAC',
            dts: 'DTS',
            truehd: 'TrueHD'
        };

        return names[codec] || codec.toUpperCase();
    }

    function formatAudioItem(track, index) {
        var title = track.title || ('Аудиодорожка #' + (index + 1));
        var lang = (track.language || '').toUpperCase();
        var codec = nameAudioCodec(track.capsName);

        var rate = track.rate
            ? Math.round(track.rate / 1000) + ' kHz'
            : '';

        var subtitleParts = [];

        if (lang && lang !== 'UND') subtitleParts.push(lang);
        if (codec) subtitleParts.push(codec);
        if (rate) subtitleParts.push(rate);

        return {
            title: title,
            subtitle: subtitleParts.join(' • '),
            padName: track.padName,
            audioIndex: index || 0
        };
    }

    function createPlaylist(data, audioIndex) {
        var playlist = []

        if (data.playlist) {
            data.playlist.forEach(function (p) {
                playlist.push({
                    title: p.title,
                    url_orig: p.url,
                    url: account('{localhost}/gst/start.m3u8?linkencode=' + encodeURIComponent(Lampa.Base64.encode(p.url))) + '&audio=' + audioIndex
                })
            })
        }

        return playlist
    }

    function handlePlayerStart(e) {
        if (isMkvSource(e.data)) {
            if (e.data.url.indexOf('/gst/') != -1 || e.data.url.indexOf('.m3u8') != -1)
                return;

            e.abort()

            setTimeout(() => {
                Lampa.Player.close();

                Lampa.Loading.start(function () { }, 'Получение списка аудио дорожек...');

                var src = e.data.url.replace(/&(preload|stat|m3u)/g, '&play');

                var network = new Lampa.Reguest();
                network.timeout = 40000;

                network.native(account('{localhost}/gst/add?linkencode=' + encodeURIComponent(Lampa.Base64.encode(src))), function (response) {
                    Lampa.Loading.stop();

                    var json = typeof response === 'string' ? JSON.parse(response) : response;
                    if (!json || !json.id || !json.hls) {
                        Lampa.Noty.show('Не удалось запустить транскодинг');
                        return;
                    }

                    var tracks = json.probe && Array.isArray(json.probe.tracks)
                        ? json.probe.tracks
                        : [];

                    var items = tracks
                        .filter(function (track) {
                            return track && track.type === 'audio';
                        })
                        .map(function (track, index) {
                            return formatAudioItem(track, index);
                        });

                    

                    delete e.data.torrent_hash;
                    e.data.hls_type = 'hlsjs';
                    e.data.hls_manifest_timeout = 20000;

                    if (!items.length || items.length == 1) {
                        e.data.url_orig = e.data.url
                        e.data.url = json.hls;
                        Lampa.Player.play(e.data);
                        Lampa.Player.playlist(createPlaylist(e.data, json.audioIndex))
                        Lampa.Player.callback(function () {
                            Lampa.Controller.toggle('modal')

                            e.data.url = e.data.url_orig

                            Lampa.PlayerPlaylist.get().forEach(function (p) {
                                p.url = p.url_orig
                            })
                        })
                        taskId = json.id;
                        return;
                    }

                    var last_controller = Lampa.Controller.enabled().name

                    Lampa.Select.show({
                        title: 'Выберите аудиодорожку',
                        items: items,
                        onSelect: function (item) {
                            Lampa.Select.close();

                            e.data.url_orig = e.data.url
                            e.data.url = json.hls + '?audio=' + item.audioIndex;

                            Lampa.Player.play(e.data);
                            Lampa.Player.playlist(createPlaylist(e.data, item.audioIndex))
                            Lampa.Player.callback(function () {
                                Lampa.Controller.toggle('modal')

                                e.data.url = e.data.url_orig

                                Lampa.PlayerPlaylist.get().forEach(function (p) {
                                    p.url = p.url_orig
                                })
                            })
                            taskId = json.id;
                        },
                        onBack: function () {
                            Lampa.Controller.toggle(last_controller)
                        }
                    });
                }, function (error) {
                    Lampa.Loading.stop();
                    Lampa.Noty.show('Не удалось запустить транскодинг');
                });
            }, 10);
        }
    }

    function handlePlayerDestroy() {
        if (taskId != null) {
            var network = new Lampa.Reguest();
            network.timeout = 5000;
            network.native('{localhost}/gst/remove?id=' + taskId, function (response) { }, function (error) { });
            taskId = null;
        }
    }

    function sendHeartbeat() {
        if (taskId != null) {
            var net = new Lampa.Reguest();
            net.native('{localhost}/gst/' + taskId + '/heartbeat', function () { }, function (error) { }, null, {
                dataType: 'text',
                timeout: 3000
            });
        }
    }

    function stopHeartbeat() {
        if (heartbeatTimer) {
            clearInterval(heartbeatTimer);
            heartbeatTimer = null;
        }
    }

    function handleVideoPause() {
        if (taskId == null)
            return;

        stopHeartbeat();
        heartbeatTimer = setInterval(sendHeartbeat, 1000 * 20);
    }

    function handleVideoPlay() {
        if (taskId != null)
            stopHeartbeat();
    }

    if (!window.lampac_transcoding_plugin) {
        window.lampac_transcoding_plugin = true;
        Lampa.Utils.putScriptAsync(["{localhost}/gst/tracks.js"]);

        Lampa.Player.listener.follow('create', handlePlayerStart);
        Lampa.Player.listener.follow('destroy', handlePlayerDestroy);
        Lampa.PlayerVideo.listener.follow('pause', handleVideoPause);
        Lampa.PlayerVideo.listener.follow('play', handleVideoPlay);
    }
})();