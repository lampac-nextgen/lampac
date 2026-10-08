/* OIDC access screen. Layout adapted from lampac-nextgen/QRAuth DenyPageGenerator (MIT). */
var network = new Lampa.Reguest();
var oidcName = 'SSO';
var oidcQrTimer = null;
var oidcQrCode = '';
var oidcFocusGuard = false;

Lampa.Lang.add({
  oidc_screen_title: { ru: 'Вход в Lampa', en: 'Sign in to Lampa', uk: 'Вхід до Lampa', zh: '登录 Lampa' },
  oidc_subtitle: { ru: 'Войдите в аккаунт, чтобы открыть Lampa.', en: 'Sign in to access Lampa.', uk: 'Увійдіть до облікового запису, щоб відкрити Lampa.', zh: '登录以使用 Lampa。' },
  oidc_login_sso: { ru: 'Войти через %s', en: 'Sign in with %s', uk: 'Увійти через %s', zh: '使用 %s 登录' },
  oidc_login_password: { ru: 'Войти по паролю', en: 'Sign in with password', uk: 'Увійти за паролем', zh: '使用密码登录' },
  oidc_password_prompt: { ru: 'Введите пароль', en: 'Enter your password', uk: 'Введіть пароль', zh: '输入密码' },
  oidc_verifying: { ru: 'Проверяем пароль…', en: 'Checking password…', uk: 'Перевіряємо пароль…', zh: '正在验证密码…' },
  oidc_wrong_password: { ru: 'Неправильный пароль', en: 'Incorrect password', uk: 'Неправильний пароль', zh: '密码错误' },
  oidc_connection_error: { ru: 'Ошибка соединения', en: 'Connection error', uk: 'Помилка з’єднання', zh: '连接错误' },
  oidc_account_created: { ru: 'Аккаунт создан. Сохраните пароль.', en: 'Account created. Save your password.', uk: 'Обліковий запис створено. Збережіть пароль.', zh: '账户已创建。请保存密码。' },
  oidc_new_password: { ru: 'Ваш пароль — сохраните его для будущего входа', en: 'Save this password for future sign-ins', uk: 'Збережіть цей пароль для наступних входів', zh: '请保存此密码以供下次登录' },
  oidc_continue: { ru: 'Понятно, продолжить', en: 'Continue', uk: 'Зрозуміло, продовжити', zh: '继续' },
  oidc_hint: { ru: 'На телевизоре отсканируйте QR-код справа. На этом устройстве можно войти через SSO или по паролю.', en: 'Scan the QR code on your TV, or sign in on this device with SSO or a password.', uk: 'На телевізорі відскануйте QR-код праворуч. На цьому пристрої можна увійти через SSO або за паролем.', zh: '在电视上扫描右侧二维码，或在此设备上使用 SSO 或密码登录。' },
  oidc_qr_creating: { ru: 'Создаём QR-код для входа…', en: 'Creating a sign-in QR code…', uk: 'Створюємо QR-код для входу…', zh: '正在生成登录二维码…' },
  oidc_qr_caption: { ru: 'Отсканируйте код телефоном и войдите через %s. Код действует 10 минут.', en: 'Scan with your phone and sign in with %s. The code expires in 10 minutes.', uk: 'Відскануйте код телефоном і увійдіть через %s. Код діє 10 хвилин.', zh: '用手机扫码并通过 %s 登录。二维码将在 10 分钟后失效。' },
  oidc_qr_alt: { ru: 'QR-код для входа', en: 'Sign-in QR code', uk: 'QR-код для входу', zh: '登录二维码' },
  oidc_qr_failed: { ru: 'Не удалось создать QR-код', en: 'Could not create the QR code', uk: 'Не вдалося створити QR-код', zh: '无法生成二维码' },
  oidc_qr_image_failed: { ru: 'QR не загрузился. Откройте ссылку ниже на телефоне.', en: 'The QR image did not load. Open the link below on your phone.', uk: 'QR-код не завантажився. Відкрийте посилання нижче на телефоні.', zh: '二维码加载失败。请在手机上打开下方链接。' },
  oidc_qr_expired: { ru: 'Код истёк. Создаём новый…', en: 'Code expired. Creating a new one…', uk: 'Термін дії коду минув. Створюємо новий…', zh: '二维码已过期。正在生成新的二维码…' },
  oidc_blocked: { ru: 'Доступ закрыт', en: 'Access denied', uk: 'Доступ закрито', zh: '访问被拒绝' }
});

function oidcT(key, name) {
  var value = Lampa.Lang.translate(key);
  return name ? value.replace('%s', name) : value;
}

(function () {
  var style = document.createElement('style');
  style.textContent = [
    '#dpc{position:fixed;top:0;right:0;bottom:0;left:0;z-index:99999;overflow:auto;background:#0a0a0b;color:#fafafa;font-family:"SegoeUI","Segoe UI",system-ui,sans-serif}',
    '#dpc *{box-sizing:border-box}',
    '#dpc-w{position:relative;min-height:100%;display:flex;align-items:center;overflow:hidden;background:radial-gradient(70% 80% at 85% 30%,#29232e 0%,#111116 48%,#0a0a0b 100%)}',
    '#dpc-wall{position:absolute;top:-18%;left:-12%;width:124%;height:140%;display:flex;flex-wrap:wrap;align-content:center;transform:perspective(1400px) rotateX(13deg) rotateZ(-7deg);opacity:.35;pointer-events:none}',
    '#dpc-wall i{display:block;width:9.5%;height:0;padding-bottom:13.8%;margin:.25%;border-radius:.4em;background:linear-gradient(145deg,#4d303b,#17232b 60%,#32313a)}',
    '#dpc-wall i:nth-child(5n+2){background:linear-gradient(145deg,#3a4050,#442d30 70%,#16171d)}',
    '#dpc-wall i:nth-child(5n+3){background:linear-gradient(135deg,#39483d,#252332 65%,#5b3930)}',
    '#dpc-wall i:nth-child(5n+4){background:linear-gradient(145deg,#493b30,#253345 65%,#191b20)}',
    '#dpc-shade{position:absolute;top:0;left:0;width:100%;height:100%;pointer-events:none;background:linear-gradient(90deg,rgba(8,8,10,.94),rgba(8,8,10,.82) 38%,rgba(8,8,10,.55) 100%),linear-gradient(0deg,rgba(8,8,10,.65),transparent 25%,transparent 75%,rgba(8,8,10,.5))}',
    '#dpc-content{position:relative;z-index:1;width:100%;min-height:460px;display:flex;align-items:center}',
    '#dpc-l{flex:1;min-width:0;padding:3em 2em 3em 5%;display:flex;flex-direction:column;align-items:flex-start;gap:1.15em}',
    '#dpc-logo{display:flex;align-items:center;gap:.7em;margin-bottom:.7em;font-size:1.1em;font-weight:700;letter-spacing:.1em;text-transform:uppercase}',
    '#dpc-logo svg{width:1.9em;height:1.9em;flex-shrink:0}',
    '#dpc-title{font-size:2.5em;line-height:1.2;font-weight:700;margin:0}',
    '#dpc-subtitle{font-size:1em;line-height:1.6;color:#c7c7cc;margin:0;max-width:40ch}',
    '#dpc-actions{display:flex;flex-direction:column;gap:.8em;margin-top:.9em;max-width:27em;width:100%}',
    '#dpc .dpc-b{display:flex;align-items:center;gap:.8em;width:100%;min-height:3.2em;padding:.35em .45em .35em 1.25em;border:1px solid rgba(255,255,255,.15);border-radius:999px;background:rgba(22,22,25,.58);-webkit-backdrop-filter:blur(12px);backdrop-filter:blur(12px);box-shadow:0 .35em 1.4em rgba(0,0,0,.28);color:#fafafa;font-family:inherit;font-size:1.15em;font-weight:600;text-align:left;cursor:pointer;transition:border-color .16s,background .16s}',
    '#dpc .dpc-b:hover,body:not(.mouse--controll) #dpc .dpc-b.focus{border-color:rgba(255,255,255,.7);background:rgba(65,65,71,.58)}',
    '#dpc .dpc-b:disabled{opacity:.5;cursor:default}',
    '#dpc .dpc-b>svg{width:1.2em;height:1.2em;flex-shrink:0}',
    '#dpc .dpc-arr{display:flex;align-items:center;justify-content:center;margin-left:auto;width:2em;height:2em;flex-shrink:0;border-radius:50%;background:rgba(255,255,255,.12);font-size:1em}',
    '#dpc-err{min-height:1.4em;color:#ff9185;font-size:.95em}',
    '#dpc-newpass{display:none;padding:.9em 1.2em;border:1px solid rgba(255,255,255,.35);border-radius:1em;background:rgba(255,255,255,.08);line-height:1.5}',
    '#dpc-newpass strong{display:block;font-size:1.5em;letter-spacing:.08em}',
    '#dpc-hint{border-top:1px solid rgba(255,255,255,.1);padding-top:1.2em;color:#b8b8bd;line-height:1.5;max-width:34em}',
    '#dpc-r{width:35em;flex-shrink:0;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:1em;padding:3em 5% 3em 2em;text-align:center}',
    '#dpc-qr-wrap{width:22em;height:22em;padding:.55em;border:1px solid rgba(255,255,255,.15);border-radius:1.9em;background:rgba(10,10,11,.6)}',
    '#dpc-qr-plate{width:100%;height:100%;padding:.8em;border-radius:1.35em;background:#fff}',
    '#dpc-qr-box{width:100%;height:100%;display:flex;align-items:center;justify-content:center}',
    '#dpc-qr-box img,#dpc-qr-box canvas,#dpc-qr-box svg{display:block;width:100%!important;height:100%!important}',
    '#dpc-qrsub{max-width:24em;color:#c7c7cc;line-height:1.5}',
    '#dpc-link{max-width:25em;word-break:break-all;color:#a5a5ad;font-size:.75em;text-decoration:none}',
    '@media(max-width:700px),(max-height:480px) and (pointer:coarse){#dpc{font-size:16px}#dpc-content{min-height:100vh}#dpc-l{padding:2em 1.5em}#dpc-r{display:none}#dpc-title{font-size:2.1em}#dpc-actions{max-width:none}}',
    '@media(prefers-reduced-motion:reduce){#dpc .dpc-b{transition:none}}',
    '.settings-input{z-index:100000!important}.selectbox{z-index:100001!important}'
  ].join('');
  document.head.appendChild(style);
})();

function oidcIcon(path) {
  return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">' + path + '</svg>';
}

function oidcButton(id, icon, label) {
  return '<button type="button" id="' + id + '" class="dpc-b selector">' + icon + '<span class="dpc-label">' + label + '</span><span class="dpc-arr">→</span></button>';
}

function oidcSetError(message) {
  var box = document.getElementById('dpc-err');
  if (box) box.textContent = message || '';
}

function oidcStopQr() {
  if (oidcQrTimer) clearInterval(oidcQrTimer);
  oidcQrTimer = null;
  oidcQrCode = '';
}

function oidcRenderQr(url) {
  var box = document.getElementById('dpc-qr-box');
  if (!box) return;
  box.innerHTML = '';
  var image = document.createElement('img');
  image.alt = oidcT('oidc_qr_alt');
  image.src = 'https://api.qrserver.com/v1/create-qr-code/?size=480x480&margin=4&data=' + encodeURIComponent(url);
  image.onerror = function () {
    var sub = document.getElementById('dpc-qrsub');
    if (sub) sub.textContent = oidcT('oidc_qr_image_failed');
  };
  box.appendChild(image);
}

function oidcCreateQr() {
  if (!document.getElementById('dpc-qr-box') || document.getElementById('dpc').className === 'dpc-blocked') return;
  var xhr = new XMLHttpRequest();
  xhr.open('POST', '{localhost}/oidc/link', true);
  xhr.onload = function () {
    if (document.getElementById('dpc').className === 'dpc-blocked') return;
    var data;
    try { data = JSON.parse(xhr.responseText); } catch (e) { data = null; }
    if (xhr.status < 200 || xhr.status >= 300 || !data || !data.link || !data.url) {
      oidcSetError(oidcT('oidc_qr_failed'));
      return;
    }
    oidcQrCode = data.link;
    oidcRenderQr(data.url);
    var link = document.getElementById('dpc-link');
    if (link) { link.textContent = data.url; link.href = data.url; }
    var sub = document.getElementById('dpc-qrsub');
    if (sub) sub.textContent = oidcT('oidc_qr_caption', oidcName);
    oidcQrTimer = setInterval(oidcPollQr, 2500);
    setTimeout(function () {
      if (oidcQrCode === data.link) {
        oidcStopQr();
        var sub = document.getElementById('dpc-qrsub');
        if (sub) sub.textContent = oidcT('oidc_qr_expired');
        oidcCreateQr();
      }
    }, 600000);
  };
  xhr.onerror = function () { oidcSetError(oidcT('oidc_qr_failed')); };
  xhr.send();
}

function oidcPollQr() {
  if (!oidcQrCode) return;
  var xhr = new XMLHttpRequest();
  xhr.open('GET', '{localhost}/oidc/link/' + encodeURIComponent(oidcQrCode) + '/status', true);
  xhr.onload = function () {
    var data;
    try { data = JSON.parse(xhr.responseText); } catch (e) { data = null; }
    if (!data || !data.uid) return;
    oidcStopQr();
    Lampa.Storage.set('lampac_unic_id', data.uid);
    try { localStorage.removeItem('activity'); } catch (e2) { }
    window.location.reload();
  };
  xhr.send();
}

function oidcLogin() {
  var uid = Lampa.Storage.get('lampac_unic_id', '');
  if (!uid) {
    uid = Lampa.Utils.uid(8).toLowerCase();
    Lampa.Storage.set('lampac_unic_id', uid);
  }
  window.location.href = '{localhost}/oidc/login?uid=' + encodeURIComponent(uid);
}

function oidcPassword() {
  var button = document.getElementById('dpc-password');
  if (!button || button.disabled) return;
  var returned = false;
  var watch = null;
  oidcFocusGuard = false;

  function restoreFocus() {
    if (returned) return;
    returned = true;
    if (watch) clearInterval(watch);
    oidcFocusGuard = true;
    if (Lampa.Controller && Lampa.Controller.toggle)
      Lampa.Controller.toggle('dpc_component');
  }

  Lampa.Input.edit({ free: true, title: oidcT('oidc_password_prompt'), nosave: true, value: '', nomic: true }, function (value) {
    restoreFocus();
    if (!value) return;
    button.disabled = true;
    oidcSetError(oidcT('oidc_verifying'));
    var url = '{localhost}/testaccsdb';
    url = Lampa.Utils.addUrlComponent(url, 'account_email=' + encodeURIComponent(value));
    var uid = Lampa.Storage.get('lampac_unic_id', '');
    if (uid) url = Lampa.Utils.addUrlComponent(url, 'uid=' + encodeURIComponent(uid));
    (new Lampa.Reguest()).silent(url, function (result) {
      button.disabled = false;
      if (!result || !result.success) { oidcSetError(oidcT('oidc_wrong_password')); return; }
      if (result.uid) {
        Lampa.Storage.set('lampac_unic_id', result.uid);
        var box = document.getElementById('dpc-newpass');
        box.style.display = 'block';
        box.querySelector('strong').textContent = result.uid;
        button.querySelector('.dpc-label').textContent = oidcT('oidc_continue');
        $(button).off('hover:enter').on('hover:enter', function () {
          try { localStorage.removeItem('activity'); } catch (e) { }
          window.location.href = '/';
        });
        oidcSetError(oidcT('oidc_account_created'));
      } else {
        Lampa.Storage.set('lampac_unic_id', value);
        try { localStorage.removeItem('activity'); } catch (e) { }
        window.location.href = '/';
      }
    }, function () { button.disabled = false; oidcSetError(oidcT('oidc_connection_error')); }, { code: value });
  });

  // Some TV keyboards close on Back without calling Input.edit's callback.
  // Return the remote to this screen when Lampa removes its keyboard UI.
  var tries = 0;
  watch = setInterval(function () {
    if (returned || ++tries > 1200) { clearInterval(watch); return; }
    if (!document.querySelector('.settings-input')) restoreFocus();
  }, 100);
}

function oidcLoadPosters() {
  // Lampa's movie source supplies the backdrop when available. Gradient tiles
  // remain visible if the catalogue or poster CDN cannot be reached.
  try {
    var tmdb = Lampa.Api && Lampa.Api.sources && Lampa.Api.sources.tmdb;
    if (!tmdb || !tmdb.get) return;
    tmdb.get('trending/movie/week', {}, function (res) {
      var entries = (res && res.results || []).filter(function (item) { return item.poster_path; });
      var wall = document.getElementById('dpc-wall');
      if (!wall || !entries.length) return;
      var tiles = wall.children;
      for (var i = 0; i < tiles.length; i++) {
        tiles[i].style.backgroundImage = 'url("https://image.tmdb.org/t/p/w300' + entries[i % entries.length].poster_path + '")';
        tiles[i].style.backgroundSize = 'cover';
        tiles[i].style.backgroundPosition = 'center';
      }
    }, function () {});
  } catch (e) { }
}

function addDevice(message, blocked) {
  if (document.getElementById('dpc')) return;
  var lock = oidcIcon('<rect x="5" y="11" width="14" height="9" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>');
  var key = oidcIcon('<circle cx="8" cy="15" r="4"/><path d="m11 12 9-9M17 6l3 3M15 8l3 3"/>');
  var html = '<div id="dpc"><div id="dpc-w"><div id="dpc-wall"></div><div id="dpc-shade"></div><div id="dpc-content">' +
    '<div id="dpc-l"><div id="dpc-logo">' + oidcIcon('<circle cx="12" cy="12" r="10"/><circle cx="12" cy="12" r="6"/><circle cx="12" cy="12" r="2"/>') + '<span>Lampac</span></div>' +
    '<h1 id="dpc-title"></h1><p id="dpc-subtitle"></p><div id="dpc-actions">' +
    oidcButton('dpc-sso', key, oidcT('oidc_login_sso', 'SSO')) + oidcButton('dpc-password', lock, oidcT('oidc_login_password')) +
    '<div id="dpc-newpass"><span></span><strong></strong></div><div id="dpc-err"></div></div>' +
    '<div id="dpc-hint"></div></div>' +
    '<div id="dpc-r"><div id="dpc-qr-wrap"><div id="dpc-qr-plate"><div id="dpc-qr-box"></div></div></div>' +
    '<div id="dpc-qrsub"></div><a id="dpc-link" target="_blank" rel="noopener"></a></div>' +
    '</div></div></div>';
  document.body.insertAdjacentHTML('beforeend', html);
  if (blocked) document.getElementById('dpc').className = 'dpc-blocked';
  var wall = document.getElementById('dpc-wall');
  for (var i = 0; i < 70; i++) wall.appendChild(document.createElement('i'));
  oidcLoadPosters();
  document.getElementById('dpc-title').textContent = oidcT('oidc_screen_title');
  document.getElementById('dpc-subtitle').textContent = message || oidcT('oidc_subtitle');
  document.querySelector('#dpc-newpass span').textContent = oidcT('oidc_new_password');
  document.getElementById('dpc-hint').textContent = oidcT('oidc_hint');
  document.getElementById('dpc-qrsub').textContent = oidcT('oidc_qr_creating');
  $('#dpc-sso').on('hover:enter', oidcLogin);
  $('#dpc-password').on('hover:enter', oidcPassword);
  (new Lampa.Reguest()).silent('{localhost}/oidc/config', function (res) {
    if (!res || !res.name) return;
    oidcName = String(res.name);
    document.querySelector('#dpc-sso .dpc-label').textContent = oidcT('oidc_login_sso', oidcName);
    var sub = document.getElementById('dpc-qrsub');
    if (sub && oidcQrCode) sub.textContent = oidcT('oidc_qr_caption', oidcName);
  }, function () {});
  if (!blocked) oidcCreateQr();
  if (!blocked && Lampa.Controller && Lampa.Controller.add) {
    Lampa.Controller.add('dpc_component', {
      toggle: function () {
        Lampa.Controller.collectionSet($('#dpc-w'), false, true);
        Lampa.Controller.collectionFocus(document.getElementById('dpc-password'), $('#dpc-w'));
      },
      back: function () {}
    });
    if (Lampa.Controller.listener && Lampa.Controller.listener.follow) {
      Lampa.Controller.listener.follow('toggle', function (event) {
        if (oidcFocusGuard && event.name !== 'dpc_component' && document.getElementById('dpc'))
          Lampa.Controller.toggle('dpc_component');
      });
    }
    oidcFocusGuard = true;
    Lampa.Controller.toggle('dpc_component');
  }
}

function showBlocked(message) {
  addDevice(message, true);
  document.getElementById('dpc-title').textContent = oidcT('oidc_blocked');
  document.getElementById('dpc-actions').style.display = 'none';
  document.getElementById('dpc-r').style.display = 'none';
  document.getElementById('dpc-hint').style.display = 'none';
  oidcStopQr();
}

function handleOidcReturn() {
  var search = window.location.search || '';
  if (!/[?&]oidc=ok(?:&|$)/.test(search)) return false;
  var match = search.match(/[?&]uid=([^&]*)/);
  if (match && match[1]) Lampa.Storage.set('lampac_unic_id', decodeURIComponent(match[1]));
  try { localStorage.removeItem('activity'); } catch (e) { }
  var rest = search.replace(/([?&])oidc=ok&?/, '$1').replace(/([?&])uid=[^&]*&?/, '$1').replace(/[?&]+$/, '');
  window.location.replace(window.location.origin + window.location.pathname + rest + (window.location.hash || ''));
  return true;
}

function checkAutch() {
  var url = '{localhost}/testaccsdb';
  var uid = Lampa.Storage.get('lampac_unic_id', '');
  if (uid) url = Lampa.Utils.addUrlComponent(url, 'uid=' + encodeURIComponent(uid));
  var token = '{token}';
  if (token) url = Lampa.Utils.addUrlComponent(url, 'token={token}');
  network.silent(url, function (res) {
    if (!res.accsdb) { network.clear(); network = null; return; }
    window.start_deep_link = { component: 'denypages', page: 1, url: '' };
    if (res.newuid) Lampa.Storage.set('lampac_unic_id', Lampa.Utils.uid(8).toLowerCase());
    window.sync_disable = true;
    var app = document.getElementById('app');
    if (app) app.style.display = 'none';
    var loading = document.getElementById('loading-element');
    if (loading) loading.style.display = 'none';
    if (res.denymsg) showBlocked(res.denymsg);
    else addDevice(res.msg);
  }, function () {});
}

if (!handleOidcReturn()) checkAutch();
