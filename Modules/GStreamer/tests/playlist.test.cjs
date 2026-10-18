const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const plugin = fs.readFileSync(path.join(__dirname, '..', 'plugins', 'gst.js'), 'utf8');

// Run the plugin's create handler; only the UI, timers and network are replaced.
function prepare(playlist, audioTracks = 1) {
  const result = {};
  const tracks = Array.from({ length: audioTracks }, (_, index) => ({
    type: 'audio', title: 'Track ' + index, language: 'en', capsName: 'audio/x-aac'
  }));
  class Request {
    timeout() {}
    native(url, success) {
      assert.ok(url.includes('/gst/add?linkencode='));
      success({ id: 'task', hls: 'http://example.invalid/gst/task/master.m3u8',
        audioIndex: 0, probe: { tracks } });
    }
  }
  const context = vm.createContext({
    window: {},
    setTimeout: callback => callback(),
    Lampa: {
      Storage: { get: () => '' },
      Utils: { putScriptAsync() {}, addUrlComponent: (url, value) => url + '&' + value },
      Base64: { encode: value => Buffer.from(value).toString('base64') },
      Reguest: Request,
      Loading: { start() {}, stop() {} },
      Controller: { enabled: () => ({ name: 'content' }) },
      Select: { close() {}, show: options => options.onSelect(options.items[1]) },
      Player: {
        listener: { follow: (event, callback) => { if (event === 'create') result.create = callback; } },
        close() {}, callback() {},
        play: item => { result.playing = item; },
        playlist: items => { result.playlist = items; }
      },
      PlayerVideo: { listener: { follow() {} } }
    }
  });
  vm.runInContext(plugin, context);
  result.create({
    data: { title: 'Episode 1', url: 'http://example.invalid/episode1.mkv',
      torrent_hash: 'neutral', playlist },
    abort: () => { result.aborted = true; }
  });
  return result;
}

for (const tracks of [1, 2]) {
  test('HLS autoplay entries are not routed as torrents with ' + tracks + ' audio tracks', () => {
    const viewed = () => {};
    const timeline = { hash: 'series:1:2', time: 42 };
    const source = {
      title: 'Episode 2', season: 1, episode: 2, id: 'episode-2',
      url: 'http://example.invalid/episode2.mkv?link=neutral&index=2&preload',
      torrent_hash: 'neutral', timeline, viewed
    };
    const result = prepare([source], tracks);
    const next = result.playlist[0];
    assert.ok(result.aborted);
    assert.equal(result.playing.torrent_hash, undefined);
    assert.equal(next.torrent_hash, undefined);
    const url = new URL(next.url.replace('{localhost}', 'http://example.invalid'));
    assert.equal(url.pathname, '/gst/start.m3u8');
    assert.equal(Buffer.from(url.searchParams.get('linkencode'), 'base64').toString(),
      'http://example.invalid/episode2.mkv?link=neutral&index=2&play');
    assert.equal(url.searchParams.get('audio'), tracks === 2 ? '1' : '0');
    assert.equal(next.season, 1);
    assert.equal(next.episode, 2);
    assert.equal(next.id, 'episode-2');
    assert.equal(next.timeline, timeline);
    assert.equal(next.viewed, viewed);
    assert.equal(next.url_orig, source.url);
    assert.equal(next.hls_type, 'hlsjs');
    assert.equal(next.hls_manifest_timeout, 90000);
    assert.equal(source.torrent_hash, 'neutral');
    assert.ok(source.url.endsWith('&preload'));
  });
}

test('non-torrent playlist entries keep their metadata and original URL', () => {
  const source = { title: 'Local video', url: 'http://example.invalid/video.mkv', episode: 3 };
  const next = prepare([source]).playlist[0];
  assert.equal(next.torrent_hash, undefined);
  assert.equal(next.title, 'Local video');
  assert.equal(next.episode, 3);
  assert.equal(next.url_orig, source.url);
  assert.equal(source.url, 'http://example.invalid/video.mkv');
});

test('a standalone video does not gain playlist entries', () => {
  assert.equal(prepare(undefined).playlist.length, 0);
});
