(function () {
    var API_BASE = '{localhost}';

    var activeJob = null;
    var heartbeatTimer = null;
    var waitHandle = null;
	
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

    function showWait(message) {
        Lampa.Loading.start(function(){}, message);
    }

    function hideWait() {
        Lampa.Loading.stop();
    }


    function log() {
        try {
            var args = Array.prototype.slice.call(arguments);
            args.unshift('Transcoder');
            console.log.apply(console, args);
        }
        catch (err) {
            /* noop */
        }
    }

    function notify(message) {
        if (window.Lampa && Lampa.Noty && Lampa.Noty.show) {
            Lampa.Noty.show(message);
        }
        else {
            log(message);
        }
    }

    function resolveMediaUrl(data) {
        if (data && data.url) return data.url;
        return '';
    }

    function isMkvSource(data) {
        var url = resolveMediaUrl(data);
        if (!url) return false;
        return /\.(mkv|avi|flv)($|\?|#)/i.test(url) || /\/lite\/pidtor\//i.test(url);
    }

    function requestFfprobe(mediaUrl, onSuccess, onError) {
        if (!mediaUrl) {
            if (onError) onError(new Error('Empty media url'));
            return;
        }

        var net = new Lampa.Reguest();
        var target = account(API_BASE + '/ffprobe?media=' + encodeURIComponent(mediaUrl));

        log('request ffprobe', target);

        net.native(target, function (response) {
            try {
                var json = typeof response === 'string' ? JSON.parse(response) : response;
                try {
                    var streams = json && Array.isArray(json.streams) ? json.streams : [];
                    var v = streams.find(function(s){ return s.codec_type === 'video'; });
                    var rawCodec = (v && (v.codec_name || (v.tags && v.tags.codec_name))) || '';
                } catch(e) { /* noop */ }
                if (onSuccess) onSuccess(json || {});
            } catch(e) {
                if (onError) onError(e);
            }
        }, function (error) {
            hideWait();
            if (onError) onError(error || new Error('ffprobe request failed'));
        }, null, {
            dataType: 'text',
            timeout: 1000 * 40
        });
    }

    function formatAudioItem(track, index) {
        var tags = track && track.tags ? track.tags : {};
        var title = tags.title || tags.handler_name || ('Audio #' + (index + 1));
        var lang = (tags.language || '').toUpperCase();
        var codec = (track.codec_name || '').toUpperCase();
        var channels = (track.channel_layout || '').replace('(side)', '').replace('stereo', '2.0');
        var rate = track.bit_rate ? Math.round(track.bit_rate / 1000) + ' kbps' : '';

        var subtitleParts = [];
        if (lang) subtitleParts.push(lang);
        if (codec) subtitleParts.push(codec);
        if (channels) subtitleParts.push(channels);
        if (rate) subtitleParts.push(rate);

        return {
            title: title,
            subtitle: subtitleParts.join(' • '),
            track: track
        };
    }

    function showAudioSelector(data, audioTracks) {
        if (!audioTracks.length) {
            notify('Не удалось найти аудиодорожки для MKV файла');
            return;
        }

        var items = audioTracks.map(function (track, index) {
            return formatAudioItem(track, index);
        });

        var last_controller = Lampa.Controller.enabled().name

        Lampa.Select.show({
            title: 'Выберите аудиодорожку',
            items: items,
            onSelect: function (item) {
                Lampa.Select.close();
                if (!item || !item.track) {
                    notify('Выбрана пустая дорожка');
                    return;
                }
                startTranscoding(data, item.track);
            },
            onBack: function () {
                Lampa.Controller.toggle(last_controller)
            }
        });
    }

    function startTranscoding(data, track) {
        stopHeartbeat();
        ensureJobStopped(true);

        var payload = {
            src: resolveMediaUrl(data),
            audio: { index: track.index -1 },
            live: false
        };

        /*if (data && data.subtitles) {
            payload.subtitles = !!data.subtitles;
        }*/

        var net = new Lampa.Reguest();
        net.native(account(API_BASE + '/transcoding/start'), function (response) {
            var json = typeof response === 'string' ? JSON.parse(response) : response;
            if (!json || !json.streamId || !json.playlistUrl) {
                notify('Некорректный ответ от сервера перекодирования');
                log('invalid start response', response);
                return;
            }

            activeJob = {
                streamId: json.streamId,
                playlistUrl: json.playlistUrl
            };

            var playback = data || {};
			playback.transcoding = true;
			playback.ffprobe = null;
            playback.url = json.playlistUrl;
            playback.subtitles_call = json.subtitlesUrl;
            playback.hls_manifest_timeout = json.hls_timeout_seconds * 1000;
            Lampa.Player.play(playback);
			
        }, function (error) {
            hideWait();
            notify('Не удалось запустить перекодирование MKV');
            log('start error', error);
        }, JSON.stringify(payload), {
            dataType: 'json',
            type: 'POST',
            timeout: 1000 * 20,
            headers: {
                'Content-Type': 'application/json'
            }
        });
    }

    function sendHeartbeat() {
        if (!activeJob) return;

        var net = new Lampa.Reguest();
        net.native(account(API_BASE + '/transcoding/' + activeJob.streamId + '/heartbeat'), function () {
            log('heartbeat sent');
        }, function (error) {
            hideWait();
            log('heartbeat error', error);
        }, null, {
            dataType: 'text',
            timeout: 1000 * 10
        });
    }

    function stopHeartbeat() {
        if (heartbeatTimer) {
            clearInterval(heartbeatTimer);
            heartbeatTimer = null;
        }
    }

    function ensureJobStopped(sendRemote) {
        if (!activeJob) return;

        var job = activeJob;
        activeJob = null;
        stopHeartbeat();

        if (sendRemote) {
            var net = new Lampa.Reguest();
            net.native(account(API_BASE + '/transcoding/' + job.streamId + '/stop'), function () {
                log('stop sent');
            }, function (error) {
            hideWait();
                log('stop error', error);
            }, null, {
                dataType: 'text',
                timeout: 1000 * 10
            });
        }
    }

    function handlePlayerStart(e) {
        if (!isMkvSource(e.data) || e.data.transcoding || /\/transcoding\//i.test(e.data.url)) return;

		e.abort();
        e.data.url = e.data.url.replace(/&(preload|stat|m3u)/g, '&play');
		
        log('intercept mkv playback', e.data.url);

        showWait('Получение списка аудио дорожек...');

        requestFfprobe(resolveMediaUrl(e.data), function (info) {
            hideWait();
            var streams = info && Array.isArray(info.streams) ? info.streams : [];
            var audioTracks = streams.filter(function (track) {
                return track.codec_type === 'audio';
            });
            showAudioSelector(e.data, audioTracks);
        }, function (error) {
            hideWait();
            notify('Ошибка анализа MKV файла');
            log('ffprobe error', error);
        });
    }

    function handlePlayerDestroy() {
        ensureJobStopped(true);
    }

    function handleVideoPause() {
        if (!activeJob) return;

        stopHeartbeat();
        heartbeatTimer = setInterval(sendHeartbeat, 1000 * 10);
    }

    function handleVideoPlay() {
        if (!activeJob) return;

        stopHeartbeat();
    }

    if (!window.lampac_transcoding_plugin) {
        window.lampac_transcoding_plugin = true;
		
        Lampa.Player.listener.follow('create', handlePlayerStart);
        Lampa.Player.listener.follow('destroy', handlePlayerDestroy);
        Lampa.PlayerVideo.listener.follow('pause', handleVideoPause);
        Lampa.PlayerVideo.listener.follow('play', handleVideoPlay);
    }
})();