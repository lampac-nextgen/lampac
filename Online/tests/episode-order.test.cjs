const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const plugin = fs.readFileSync(path.join(__dirname, '..', 'plugin.js'), 'utf8');

// Run the production methods without starting the UI or making provider requests.
function method(name, next) {
  const start = plugin.indexOf('    this.' + name + ' = function');
  const end = plugin.indexOf('    this.' + next + ' = function', start);
  assert.ok(start >= 0 && end > start, 'production method must exist: ' + name);
  return plugin.slice(start, end).replace('{player-inner}', '');
}

function display(videos, player = 'inner') {
  const result = {};
  const component = {
    draw(items, options) {
      result.items = items;
      result.enter = options.onEnter;
    },
    getFileUrl(item, callback) {
      callback({ url: 'resolved:' + item.url }, {});
    },
    filter() {},
    getChoice() { return {}; },
    updateBalanser() {}
  };
  const context = vm.createContext({
    Lampa: {
      Storage: { field: () => player },
      Arrays: { getKeys: value => Object.keys(value || {}) },
      Player: {
        play(item) { result.playing = item; },
        playlist(items) { result.playlist = items; }
      }
    },
    filter_find: { season: [], voice: [] },
    balanser: 'mirage'
  });
  const source = method('toPlayElement', 'orUrlReserve') +
    method('orUrlReserve', 'setDefaultQuality') +
    method('setDefaultQuality', 'display') +
    method('display', 'loadSubtitles');
  vm.runInContext('(function () {\n' + source + '\n})', context).call(component);
  component.display(videos);
  return result;
}

function episode(number, season = 3, method = 'play') {
  return {
    title: number + ' episode',
    season,
    episode: number,
    method,
    url: 'episode-' + season + '-' + number,
    stream: 'stream-' + season + '-' + number,
    qualitys: {},
    voice_name: 'Example voice',
    timeline: { hash: season + ':' + number },
    mark() {}
  };
}

const numbers = items => Array.from(items, item => item.episode);

test('descending episodes are displayed chronologically and autoplay goes from 11 to 12', () => {
  const videos = [12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1].map(n => episode(n));
  const result = display(videos);
  assert.deepEqual(numbers(result.items), [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
  result.enter(videos[1]);
  assert.equal(result.playing.episode, 11);
  assert.deepEqual(numbers(result.playlist), [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
  assert.equal(result.playlist[11].episode, 12);
  assert.equal(result.playing.playlist, result.playlist);
  assert.equal(result.items[10], videos[1]);
  assert.deepEqual(numbers(videos), [12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1]);
});

test('episode numbers are sorted numerically, including string metadata', () => {
  const result = display(['10', '2', '1'].map(n => episode(n, '3')));
  assert.deepEqual(numbers(result.items), ['1', '2', '10']);
});

test('seasons take precedence over episode numbers', () => {
  const result = display([episode(1, 10), episode(10, 2), episode(2, 2)]);
  assert.deepEqual(Array.from(result.items, item => [item.season, item.episode]), [[2, 2], [2, 10], [10, 1]]);
});

test('already ascending episodes retain their order', () => {
  const videos = [1, 2, 10, 11].map(n => episode(n));
  const result = display(videos);
  assert.deepEqual(Array.from(result.items), videos);
});

test('movie translations retain provider order', () => {
  const videos = [episode(0, 0), episode(0, 0)];
  videos[0].voice_name = 'First voice';
  videos[1].voice_name = 'Second voice';
  const result = display(videos);
  assert.deepEqual(Array.from(result.items), videos);
  result.enter(videos[1]);
  assert.equal(result.playing.voice_name, 'Second voice');
  assert.equal(result.playlist.length, 1);
});

test('incomplete episode metadata retains provider order', () => {
  const videos = [episode(12), episode(0), episode(10)];
  const result = display(videos);
  assert.deepEqual(Array.from(result.items), videos);
});

for (const player of ['inner', 'android']) {
  test('call-based episodes keep URLs, metadata and chronological playlists for ' + player, () => {
    const videos = [episode(12, 3, 'call'), episode(11, 3, 'call'), episode(10, 3, 'call')];
    const result = display(videos, player);
    result.enter(videos[1]);
    assert.deepEqual(numbers(result.playlist), [10, 11, 12]);
    assert.equal(result.playing.url, 'resolved:episode-3-11');
    assert.equal(result.playing.timeline, videos[1].timeline);
    assert.equal(result.playing.voice_name, 'Example voice');
    const next = result.playlist[2];
    if (player === 'inner') {
      assert.equal(typeof next.url, 'function');
      let called = false;
      next.url(() => { called = true; });
      assert.ok(called);
      assert.equal(next.url, 'resolved:episode-3-12');
    } else {
      assert.equal(next.url, 'stream-3-12');
    }
  });
}
