(function () {
    'use strict';

    var connect_host = '{localhost}';
    var list_opened = false;
	
    var unic_id = Lampa.Storage.get('lampac_unic_id', '');
    if (!unic_id) {
      unic_id = Lampa.Utils.uid(8).toLowerCase();
      Lampa.Storage.set('lampac_unic_id', unic_id);
    }

    function reguest(params, callback) {
        if (params && Array.isArray(params.ffprobe) && params.ffprobe.length) {
            setTimeout(function () {
                callback({
                    streams: params.ffprobe
                });
            }, 200);
        }
        else {
            if (!params || !params.url) {
                callback({ streams: [] });
                return;
            }

            var completed = false;
            var done = function done(result) {
                if (completed) return;
                completed = true;
                callback(result);
            };

            var net = new Lampa.Reguest();
            net.timeout(1000 * 45);

            var sourceUrl = String(params.url).replace(/&(preload|stat|m3u)/g, '&play');
            var url = connect_host + '/gst/probe?link=' + encodeURIComponent(sourceUrl);

            if (url.indexOf('account_email=') == -1) {
                var email = Lampa.Storage.get('account_email');
                if (email) url = Lampa.Utils.addUrlComponent(url, 'account_email=' + encodeURIComponent(email));
            }

            if (url.indexOf('uid=') == -1 && unic_id) {
                url = Lampa.Utils.addUrlComponent(url, 'uid=' + encodeURIComponent(unic_id));
            }

            if (url.indexOf('token=') == -1) {
                var token = '{token}';
                if (token != '') url = Lampa.Utils.addUrlComponent(url, 'token={token}');
            }

            net["native"](url, function (response) {
                var json = response;

                if (typeof response === 'string') {
                    try {
                        json = JSON.parse(response);
                    }
                    catch (e) {
                        json = null;
                    }
                }

                var tracks = json && (json.tracks || json.Tracks);
                done(Array.isArray(tracks)
                    ? { streams: [], probe: json }
                    : { streams: [] });
            }, function () {
                done({ streams: [] });
            }, false, {
                dataType: 'text'
            });
        }
    }

    function subscribeTracks(data) {
      var inited = false;
      var inited_parse = false;
      var webos_replace = {};

      function log() {
        console.log.apply(console.log, arguments);
      }

      function getTracks() {
        var video = Lampa.PlayerVideo.video();
        return video.audioTracks || [];
      }

      function getSubs() {
        var video = Lampa.PlayerVideo.video();
        return video.textTracks || [];
      }

      log('Tracks', 'start');

      function setTracks() {
        if (inited_parse) {
          var new_tracks = [];
          var video_tracks = getTracks();
          var parse_tracks = inited_parse.streams.filter(function (a) {
            return a.codec_type == 'audio';
          });
          var minus = 1;
          log('Tracks', 'set tracks:', video_tracks.length);
          if (parse_tracks.length !== video_tracks.length) parse_tracks = parse_tracks.filter(function (a) {
            return a.codec_name !== 'dts';
          });
          parse_tracks = parse_tracks.filter(function (a) {
            return a.tags;
          });
          log('Tracks', 'filtred tracks:', parse_tracks.length);
          parse_tracks.forEach(function (track) {
            var orig = video_tracks[track.index - minus];
            var elem = {
              index: track.index - minus,
              language: track.tags.language,
              label: track.tags.title || track.tags.handler_name,
              ghost: orig ? false : true,
              selected: orig ? orig.selected == true || orig.enabled == true : false
            };
            console.log('Tracks', 'tracks original', orig);
            Object.defineProperty(elem, "enabled", {
              set: function set(v) {
                if (v) {
                  var aud = getTracks();
                  var trk = aud[elem.index];

                  for (var i = 0; i < aud.length; i++) {
                    aud[i].enabled = false;
                    aud[i].selected = false;
                  }

                  if (trk) {
                    trk.enabled = true;
                    trk.selected = true;
                  }
                }
              },
              get: function get() {}
            });
            new_tracks.push(elem);
          });
          if (parse_tracks.length) Lampa.PlayerPanel.setTracks(new_tracks);
        }
      }

      function setSubs() {
        if (inited_parse) {
          var new_subs = [];
          var video_subs = getSubs();
          var parse_subs = inited_parse.streams.filter(function (a) {
            return a.codec_type == 'subtitle';
          });
          var minus = inited_parse.streams.filter(function (a) {
            return a.codec_type == 'audio';
          }).length + 1;
          log('Tracks', 'set subs:', video_subs.length);
          parse_subs = parse_subs.filter(function (a) {
            return a.tags;
          });
          log('Tracks', 'filtred subs:', parse_subs.length);
          parse_subs.forEach(function (track) {
            var orig = video_subs[track.index - minus];
            var elem = {
              index: track.index - minus,
              language: track.tags.language,
              label: track.tags.title || track.tags.handler_name,
              ghost: video_subs[track.index - minus] ? false : true,
              selected: orig ? orig.selected == true || orig.mode == 'showing' : false
            };
            console.log('Tracks', 'subs original', orig);
            Object.defineProperty(elem, "mode", {
              set: function set(v) {
                if (v) {
                  var txt = getSubs();
                  var sub = txt[elem.index];

                  for (var i = 0; i < txt.length; i++) {
                    txt[i].mode = 'disabled';
                    txt[i].selected = false;
                  }

                  if (sub) {
                    sub.mode = 'showing';
                    sub.selected = true;
                  }
                }
              },
              get: function get() {}
            });
            new_subs.push(elem);
          });
          if (parse_subs.length) Lampa.PlayerPanel.setSubs(new_subs);
        }
      }

      function listenTracks() {
        log('Tracks', 'tracks video event');
        setTracks();
        Lampa.PlayerVideo.listener.remove('tracks', listenTracks);
      }

      function listenSubs() {
        log('Tracks', 'subs video event');
        setSubs();
        Lampa.PlayerVideo.listener.remove('subs', listenSubs);
      }

      function canPlay() {
        log('Tracks', 'canplay video event');
        if (webos_replace.tracks) setWebosTracks(webos_replace.tracks);else setTracks();
        if (webos_replace.subs) setWebosSubs(webos_replace.subs);else setSubs();
        Lampa.PlayerVideo.listener.remove('canplay', canPlay);
      }

      function setWebosTracks(video_tracks) {
        if (inited_parse) {
          var parse_tracks = inited_parse.streams.filter(function (a) {
            return a.codec_type == 'audio';
          });
          log('Tracks', 'webos set tracks:', video_tracks.length);

          if (parse_tracks.length !== video_tracks.length) {
            parse_tracks = parse_tracks.filter(function (a) {
              return a.codec_name !== 'truehd';
            });

            if (parse_tracks.length !== video_tracks.length) {
              parse_tracks = parse_tracks.filter(function (a) {
                return a.codec_name !== 'dts';
              });
            }
          }

          parse_tracks = parse_tracks.filter(function (a) {
            return a.tags;
          });
          log('Tracks', 'webos tracks', video_tracks);
          parse_tracks.forEach(function (track, i) {
            if (video_tracks[i]) {
              video_tracks[i].language = track.tags.language;
              video_tracks[i].label = track.tags.title || track.tags.handler_name;
            }
          });
        }
      }

      function setWebosSubs(video_subs) {
        if (inited_parse) {
          var parse_subs = inited_parse.streams.filter(function (a) {
            return a.codec_type == 'subtitle';
          });
          log('Tracks', 'webos set subs:', video_subs.length);
          if (parse_subs.length !== video_subs.length - 1) parse_subs = parse_subs.filter(function (a) {
            return a.codec_name !== 'hdmv_pgs_subtitle';
          });
          parse_subs = parse_subs.filter(function (a) {
            return a.tags;
          });
          parse_subs.forEach(function (track, a) {
            var i = a + 1;

            if (video_subs[i]) {
              video_subs[i].language = track.tags.language;
              video_subs[i].label = track.tags.title || track.tags.handler_name;
            }
          });
        }
      }

      function listenWebosSubs(_data) {
        log('Tracks', 'webos subs event');
        webos_replace.subs = _data.subs;
        if (inited_parse) setWebosSubs(_data.subs);
      }

      function listenWebosTracks(_data) {
        log('Tracks', 'webos tracks event');
        webos_replace.tracks = _data.tracks;
        if (inited_parse) setWebosTracks(_data.tracks);
      }

      function listenStart() {
        inited = true;
        reguest(data, function (result) {
          log('Tracks', 'parsed', inited_parse);
          inited_parse = result;

          if (inited) {
            if (webos_replace.subs) setWebosSubs(webos_replace.subs);else setSubs();
            if (webos_replace.tracks) setWebosTracks(webos_replace.tracks);else setTracks();
          }
        });
      }

      function listenDestroy() {
        inited = false;
        Lampa.Player.listener.remove('destroy', listenDestroy);
        Lampa.PlayerVideo.listener.remove('tracks', listenTracks);
        Lampa.PlayerVideo.listener.remove('subs', listenSubs);
        Lampa.PlayerVideo.listener.remove('webos_subs', listenWebosSubs);
        Lampa.PlayerVideo.listener.remove('webos_tracks', listenWebosTracks);
        Lampa.PlayerVideo.listener.remove('canplay', canPlay);
        log('Tracks', 'end');
      }

      Lampa.Player.listener.follow('destroy', listenDestroy);
      Lampa.PlayerVideo.listener.follow('tracks', listenTracks);
      Lampa.PlayerVideo.listener.follow('subs', listenSubs);
      Lampa.PlayerVideo.listener.follow('webos_subs', listenWebosSubs);
      Lampa.PlayerVideo.listener.follow('webos_tracks', listenWebosTracks);
      Lampa.PlayerVideo.listener.follow('canplay', canPlay);
      listenStart();
    }

    function probeValue(track, name) {
      if (!track) return null;
      if (typeof track[name] !== 'undefined') return track[name];

      var pascalName = name.charAt(0).toUpperCase() + name.slice(1);
      return typeof track[pascalName] !== 'undefined' ? track[pascalName] : null;
    }

    function probeCodec(track) {
      var codec = probeValue(track, 'codec');

      if (!codec) {
        var caps = probeValue(track, 'capsName') || '';
        codec = caps.replace(/^[^/]+\//, '').replace(/^x-/, '');
      }

      return codec ? String(codec).toUpperCase() : '';
    }

    function probeChannels(channels) {
      var names = {
        1: '1.0',
        2: '2.0',
        6: '5.1',
        7: '6.1',
        8: '7.1'
      };

      return names[channels] || String(channels);
    }

    function fillProbeMetainfo(probe, video, audio, subs) {
      var tracks = probe && (probe.tracks || probe.Tracks);
      if (!Array.isArray(tracks)) return;

      var videoTracks = tracks.filter(function (track) {
        return probeValue(track, 'type') == 'video';
      });
      var audioTracks = tracks.filter(function (track) {
        return probeValue(track, 'type') == 'audio';
      });
      var subtitleTracks = tracks.filter(function (track) {
        return probeValue(track, 'type') == 'subtitle';
      });

      videoTracks.slice(0, 1).forEach(function (track) {
        var line = {};
        var width = probeValue(track, 'width');
        var height = probeValue(track, 'height');
        var codec = probeCodec(track);

        if (width && height) line.video = width + 'x' + height;
        if (codec) line.codec = codec;
        if (Lampa.Arrays.getKeys(line).length) video.push(line);
      });

      audioTracks.forEach(function (track, index) {
        var line = { num: index + 1 };
        var language = probeValue(track, 'language');
        var title = probeValue(track, 'title');
        var codec = probeCodec(track);
        var channels = probeValue(track, 'channels');
        var rate = probeValue(track, 'rate');

        if (language) line.lang = String(language).toUpperCase();
        line.name = title || '';
        if (codec) line.codec = codec;
        if (channels) line.channels = probeChannels(channels);
        if (rate) line.rate = Math.round(rate / 1000) + ' kHz';
        audio.push(line);
      });

      subtitleTracks.forEach(function (track, index) {
        var line = { num: index + 1 };
        var language = probeValue(track, 'language');

        if (language) line.lang = String(language).toUpperCase();
        line.name = probeValue(track, 'title') || '';
        subs.push(line);
      });
    }

    function parseMetainfo(data) {
      var loading = Lampa.Template.get('tracks_loading');
      data.item.after(loading);
      reguest(data.element, function (result) {
        if (list_opened) {
          var append = function append(name, fields) {
            if (fields.length) {
              var block = Lampa.Template.get('tracks_metainfo_block', {});
              block.find('.tracks-metainfo__label').text(Lampa.Lang.translate(name == 'video' ? 'extensions_hpu_video' : name == 'audio' ? 'player_tracks' : 'player_' + name));
              fields.forEach(function (data) {
                var item = $('<div class="tracks-metainfo__item tracks-metainfo__item--' + name + ' selector"></div>');
                item.on('hover:focus', function (e) {
                  Lampa.Modal.scroll().update(item);
                });

                for (var i in data) {
                  var div = $('<div class="tracks-metainfo__column--' + i + '"></div>');
                  div.text(data[i]);
                  item.append(div);
                }

                block.find('.tracks-metainfo__info').append(item);
              });
              html.append(block);
            }
          };

          var video = [];
          var audio = [];
          var subs = [];

          if (result.probe) {
            fillProbeMetainfo(result.probe, video, audio, subs);
          }

          var streams = result.probe ? [] : result.streams;
          var codec_video = streams.filter(function (a) {
            return a.codec_type == 'video';
          });
          var codec_audio = streams.filter(function (a) {
            return a.codec_type == 'audio';
          });
          var codec_subs = streams.filter(function (a) {
            return a.codec_type == 'subtitle';
          });
          codec_video.slice(0, 1).forEach(function (v) {
            var line = {};
            if (v.width && v.height) line.video = v.width + 'х' + v.height;
            if (v.codec_name) line.codec = v.codec_name.toUpperCase();
            if (Boolean(v.is_avc)) line.avc = 'AVC';
            if (Lampa.Arrays.getKeys(line).length) video.push(line);
          });
          codec_audio.forEach(function (a, i) {
            var line = {
              num: i + 1
            };

            if (a.tags) {
              line.lang = (a.tags.language || '').toUpperCase();
            }

            line.name = a.tags ? a.tags.title || a.tags.handler_name : '';
            if (a.codec_name) line.codec = a.codec_name.toUpperCase();
            if (a.channel_layout) line.channels = a.channel_layout.replace('(side)', '').replace('stereo', '2.0');
            var bit = a.bit_rate ? a.bit_rate : a.tags && (a.tags.BPS || a.tags["BPS-eng"]) ? a.tags.BPS || a.tags["BPS-eng"] : '--';
            line.rate = bit == '--' ? bit : Math.round(bit / 1000) + ' ' + Lampa.Lang.translate('speed_kb');
            if (Lampa.Arrays.getKeys(line).length) audio.push(line);
          });
          codec_subs.forEach(function (a, i) {
            var line = {
              num: i + 1
            };

            if (a.tags) {
              line.lang = (a.tags.language || '').toUpperCase();
            }

            line.name = a.tags ? a.tags.title || a.tags.handler_name : '';
            if (Lampa.Arrays.getKeys(line).length) subs.push(line);
          });

          var html = Lampa.Template.get('tracks_metainfo', {});
          append('video', video);
          append('audio', audio);
          append('subs', subs);
          loading.remove();

          if (video.length || audio.length || subs.length) {
            data.item.after(html);
          }

          if (Lampa.Controller.enabled().name == 'modal') Lampa.Controller.toggle('modal');
        }
      });
    }

    if (!window.plugin_lampac_tracks) {
        window.plugin_lampac_tracks = true;

        Lampa.Player.listener.follow('start', function (data) {
            if (data.torrent_hash) subscribeTracks(data);
        });
        Lampa.Listener.follow('torrent_file', function (data) {
            if (data.type == 'list_open') list_opened = true;
            if (data.type == 'list_close') list_opened = false;

            if (data.type == 'render' && data.items.length == 1 && list_opened) {
                parseMetainfo(data);
            }
        });
        Lampa.Template.add('tracks_loading', "\n    <div class=\"tracks-loading\">\n        <span>#{loading}...</span>\n    </div>\n");
        Lampa.Template.add('tracks_metainfo', "\n    <div class=\"tracks-metainfo\"></div>\n");
        Lampa.Template.add('tracks_metainfo_block', "\n    <div class=\"tracks-metainfo__line\">\n        <div class=\"tracks-metainfo__label\"></div>\n        <div class=\"tracks-metainfo__info\"></div>\n    </div>\n");
        Lampa.Template.add('tracks_css', "\n    <style>\n    .tracks-loading{margin-top:1em;display:-webkit-box;display:-webkit-flex;display:-moz-box;display:-ms-flexbox;display:flex;-webkit-box-align:start;-webkit-align-items:flex-start;-moz-box-align:start;-ms-flex-align:start;align-items:flex-start}.tracks-loading:before{content:'';display:inline-block;width:1.3em;height:1.3em;background:url('./img/loader.svg') no-repeat 50% 50%;-webkit-background-size:contain;-moz-background-size:contain;-o-background-size:contain;background-size:contain;margin-right:.4em}.tracks-loading>span{font-size:1.1em;line-height:1.1}.tracks-metainfo{margin-top:1em}.tracks-metainfo__line+.tracks-metainfo__line{margin-top:2em}.tracks-metainfo__label{opacity:.5;font-weight:600}.tracks-metainfo__info{padding-top:1em;line-height:1.2}.tracks-metainfo__info>div{background-color:rgba(0,0,0,0.22);display:-webkit-box;display:-webkit-flex;display:-moz-box;display:-ms-flexbox;display:flex;-webkit-border-radius:.3em;-moz-border-radius:.3em;border-radius:.3em;-webkit-flex-wrap:wrap;-ms-flex-wrap:wrap;flex-wrap:wrap}.tracks-metainfo__info>div.focus{background-color:rgba(255,255,255,0.06)}.tracks-metainfo__info>div>div{padding:1em;-webkit-flex-shrink:0;-ms-flex-negative:0;flex-shrink:0}.tracks-metainfo__info>div>div:not(:last-child){padding-right:1.5em}.tracks-metainfo__info>div+div{margin-top:1em}.tracks-metainfo__column--video,.tracks-metainfo__column--name{margin-right:auto}.tracks-metainfo__column--num{min-width:3em;padding-right:0}.tracks-metainfo__column--rate{min-width:7em;text-align:right}.tracks-metainfo__column--channels{min-width:5em;text-align:right}\n    </style>\n");
        $('body').append(Lampa.Template.get('tracks_css', {}, true));
    }

})();
