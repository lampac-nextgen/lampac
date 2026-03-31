(function () {
  'use strict';

  if (window.telegram_auth_gate_loaded) return;
  window.telegram_auth_gate_loaded = true;

  var ORIGIN = location.protocol + '//' + location.host;
  var STATUS_URL = ORIGIN + '/tg/auth/status';
  var LS_KEY = 'lampac_unic_id';
  var USER_KEY = 'tg_auth_user';
  var CHECK_INTERVAL = 10000;
  var BOT_USERNAME = 'YOUR_BOT_USERNAME';
  var FOOTER_MESSAGES = [
    'Хорошего вечера 🌒',
    'Музыка уже рядом',
    'Почти внутри. Остался один шаг',
    'Пусть сегодня всё звучит как надо',
    'Сейчас впустим тебя внутрь',
    'Пусть этот вечер будет спокойным',
    'Павлухе и Бате на пиво не забудь отправить 🍻',
    'Если читаешь с MSX — возьми себе Apple TV 😏'
  ];
  var overlay = null;
  var pollTimer = null;
  var authorized = false;

  function getUID() {
    var uid = '';
    try {
      uid = Lampa.Storage.get(LS_KEY, '');
    } catch (e) {}

    if (!uid) {
      uid = Math.random().toString(36).slice(2, 10).toLowerCase();
      try {
        Lampa.Storage.set(LS_KEY, uid);
      } catch (e) {}
    }

    return uid;
  }

  function requestStatus(uid, onSuccess, onError) {
    var network = new Lampa.Reguest();
    network.silent(
      STATUS_URL + '?uid=' + encodeURIComponent(uid),
      function (result) {
        onSuccess(result || {});
      },
      function (err) {
        if (onError) onError(err || {});
      },
      false,
      {
        timeout: 1000 * 8
      }
    );
  }

  function ensureStyle() {
    if (document.getElementById('tg-auth-gate-style')) return;

    var style = document.createElement('style');
    style.id = 'tg-auth-gate-style';
    style.textContent =
      'body.tg-auth-gate-lock > *:not(#tg-auth-gate-overlay):not(#tg-auth-gate-style){filter:blur(4px);pointer-events:none !important;user-select:none !important;}' +
      '#tg-auth-gate-overlay{position:fixed;inset:0;z-index:999999;background:radial-gradient(circle at top, rgba(22,22,26,.96), rgba(6,6,9,.98));display:flex;align-items:center;justify-content:center;padding:3.5vh 3.5vw;box-sizing:border-box;}' +
      '.tg-auth-gate__shell{width:min(1500px,100%);max-height:100%;display:flex;align-items:center;justify-content:center;}' +
      '.tg-auth-gate__box{width:100%;background:linear-gradient(180deg, rgba(17,17,20,.96), rgba(11,11,14,.98));border-radius:28px;padding:2.8em 3em;color:#fff;box-shadow:0 0 0 1px rgba(255,255,255,.06),0 30px 100px rgba(0,0,0,.45);}' +
      '.tg-auth-gate__grid{display:grid;grid-template-columns:minmax(0,1.45fr) minmax(260px,380px);gap:2.4em;align-items:center;}' +
      '.tg-auth-gate__eyebrow{font-size:1em;letter-spacing:.16em;text-transform:uppercase;color:#8d939f;margin-bottom:1em;}' +
      '.tg-auth-gate__title{font-size:clamp(2.6em,4vw,4.2em);font-weight:800;line-height:1.05;margin-bottom:.28em;}' +
      '.tg-auth-gate__text{font-size:clamp(1.15em,1.7vw,1.45em);line-height:1.45;color:#d1d5db;max-width:32em;margin-bottom:1.2em;}' +
      '.tg-auth-gate__steps{display:grid;gap:.7em;margin:1.4em 0 1.8em;}' +
      '.tg-auth-gate__step{display:flex;align-items:flex-start;gap:.9em;font-size:1.08em;color:#d6dae2;}' +
      '.tg-auth-gate__step-index{flex:0 0 1.9em;height:1.9em;border-radius:999px;background:#20232b;color:#fff;display:flex;align-items:center;justify-content:center;font-weight:700;}' +
      '.tg-auth-gate__uid-label{font-size:.95em;letter-spacing:.12em;text-transform:uppercase;color:#9399a5;margin-bottom:.7em;}' +
      '.tg-auth-gate__uid{font-size:clamp(2.2em,4.2vw,3.8em);font-weight:800;letter-spacing:.16em;font-family:ui-monospace,SFMono-Regular,Menlo,Monaco,Consolas,monospace;background:#0f1116;border-radius:20px;padding:.55em .7em;margin-bottom:.65em;word-break:break-word;border:1px solid rgba(255,255,255,.06);}' +
      '.tg-auth-gate__hint{font-size:1.05em;line-height:1.5;color:#afb6c2;margin-bottom:1.5em;max-width:34em;}' +
      '.tg-auth-gate__actions{display:flex;gap:.9em;flex-wrap:wrap;}' +
      '.tg-auth-gate__button{min-height:3.4em;padding:.95em 1.35em;border-radius:16px;cursor:pointer;display:inline-flex;align-items:center;justify-content:center;background:#222631;color:#fff;font-size:1.05em;font-weight:700;box-shadow:inset 0 0 0 1px rgba(255,255,255,.05);}' +
      '.tg-auth-gate__button--primary{background:#f3f4f6;color:#0c0d10;}' +
      '.tg-auth-gate__button--ghost{background:#171a21;color:#dfe3ea;}' +
      '.tg-auth-gate__button.focus{transform:scale(1.03);box-shadow:0 0 0 3px rgba(255,255,255,.18);}' +
      '.tg-auth-gate__meta{margin-top:1.2em;font-size:.95em;color:#7f8793;word-break:break-all;}' +
      '.tg-auth-gate__qr-wrap{display:flex;flex-direction:column;align-items:center;justify-content:center;background:#0d0f14;border-radius:24px;padding:1.4em;border:1px solid rgba(255,255,255,.05);}' +
      '.tg-auth-gate__qr{width:min(100%,320px);aspect-ratio:1/1;border-radius:18px;background:#fff;padding:14px;box-sizing:border-box;}' +
      '.tg-auth-gate__qr-title{font-size:1.2em;font-weight:700;margin-top:1em;margin-bottom:.35em;}' +
      '.tg-auth-gate__qr-text{font-size:1em;line-height:1.45;color:#b8bec9;text-align:center;max-width:18em;}' +
      '@media (max-width: 980px){.tg-auth-gate__box{padding:2em 1.4em;border-radius:22px;}.tg-auth-gate__grid{grid-template-columns:1fr;gap:1.6em;}.tg-auth-gate__text,.tg-auth-gate__hint{max-width:none;}.tg-auth-gate__qr-wrap{order:-1;}.tg-auth-gate__actions{display:grid;grid-template-columns:1fr;}}';
    document.body.appendChild(style);
  }

  function removeOverlay() {
    if (overlay && overlay.parentNode) overlay.parentNode.removeChild(overlay);
    overlay = null;
  }

  function stopPolling() {
    if (pollTimer) clearInterval(pollTimer);
    pollTimer = null;
  }

  function lockApp() {
    document.body.classList.add('tg-auth-gate-lock');
  }

  function unlockApp() {
    authorized = true;
    stopPolling();
    removeOverlay();
    document.body.classList.remove('tg-auth-gate-lock');
  }

  function showSuccessOverlay(result) {
    ensureStyle();
    removeOverlay();

    overlay = document.createElement('div');
    overlay.id = 'tg-auth-gate-overlay';
    overlay.innerHTML =
      '<div class="tg-auth-gate__shell">' +
        '<div class="tg-auth-gate__box" style="max-width:760px;">' +
          '<div class="tg-auth-gate__eyebrow">YOUR_SERVICE_NAME · Авторизация</div>' +
          '<div class="tg-auth-gate__title">Устройство авторизовано</div>' +
          '<div class="tg-auth-gate__text">Выполняется вход в YOUR_SERVICE_NAME. Подождите пару секунд...</div>' +
          '<div class="tg-auth-gate__steps">' +
            '<div class="tg-auth-gate__step"><div class="tg-auth-gate__step-index">✓</div><div><b>@' + (result.username || '') + '</b> успешно подтверждён через Telegram.</div></div>' +
            '<div class="tg-auth-gate__step"><div class="tg-auth-gate__step-index">→</div><div>Сейчас экран авторизации закроется автоматически.</div></div>' +
          '</div>' +
        '</div>' +
      '</div>';

    document.body.appendChild(overlay);
  }

  function buildOverlay(uid, message) {
    ensureStyle();
    removeOverlay();

    var tgUrl = 'https://t.me/' + BOT_USERNAME + '?start=' + encodeURIComponent(uid);
    var qrUrl = 'https://api.qrserver.com/v1/create-qr-code/?size=220x220&data=' + encodeURIComponent(tgUrl);
    var footerMessage = FOOTER_MESSAGES[Math.floor(Math.random() * FOOTER_MESSAGES.length)];

    overlay = document.createElement('div');
    overlay.id = 'tg-auth-gate-overlay';
    overlay.innerHTML =
      '<div class="tg-auth-gate__shell">' +
        '<div class="tg-auth-gate__box">' +
          '<div class="tg-auth-gate__grid">' +
            '<div>' +
              '<div class="tg-auth-gate__eyebrow">YOUR_SERVICE_NAME · Авторизация</div>' +
              '<div class="tg-auth-gate__title">Вход в YOUR_SERVICE_NAME</div>' +
              '<div class="tg-auth-gate__text">' + (message || 'Открой Telegram и привяжи это устройство, чтобы продолжить просмотр.') + '</div>' +
              '<div class="tg-auth-gate__steps">' +
                '<div class="tg-auth-gate__step"><div class="tg-auth-gate__step-index">1</div><div>Нажми <b>Открыть Telegram</b> или отсканируй QR-код телефоном.</div></div>' +
                '<div class="tg-auth-gate__step"><div class="tg-auth-gate__step-index">2</div><div>Бот <b>@' + BOT_USERNAME + '</b> автоматически получит UID устройства.</div></div>' +
                '<div class="tg-auth-gate__step"><div class="tg-auth-gate__step-index">3</div><div>После сообщения об успехе вернись сюда и нажми <b>Проверить снова</b>.</div></div>' +
              '</div>' +
              '<div class="tg-auth-gate__uid-label">UID устройства</div>' +
              '<div class="tg-auth-gate__uid">' + uid + '</div>' +
              '<div class="tg-auth-gate__hint">Если Telegram не открылся автоматически, скопируй UID вручную и отправь его боту. Для ТВ оставлен крупный QR и большой код, чтобы ничего не мешало с дивана.</div>' +
              '<div class="tg-auth-gate__actions">' +
                '<div class="tg-auth-gate__button tg-auth-gate__button--primary selector" id="tg-auth-gate-open">Открыть Telegram</div>' +
                '<div class="tg-auth-gate__button selector" id="tg-auth-gate-refresh">Проверить снова</div>' +
                '<div class="tg-auth-gate__button tg-auth-gate__button--ghost selector" id="tg-auth-gate-copy">Скопировать UID</div>' +
              '</div>' +
              '<div class="tg-auth-gate__meta" style="margin-top:1.35em;font-size:1.02em;color:#c4cad4;">' + footerMessage + '</div>' +
            '</div>' +
            '<div class="tg-auth-gate__qr-wrap">' +
              '<img class="tg-auth-gate__qr" src="' + qrUrl + '" alt="Telegram QR">' +
              '<div class="tg-auth-gate__qr-title">Сканируй QR</div>' +
              '<div class="tg-auth-gate__qr-text">Телефон откроет Telegram с готовой ссылкой на вход в YOUR_SERVICE_NAME.</div>' +
            '</div>' +
          '</div>' +
        '</div>' +
      '</div>';

    document.body.appendChild(overlay);

    var copyBtn = document.getElementById('tg-auth-gate-copy');
    var openBtn = document.getElementById('tg-auth-gate-open');
    var refreshBtn = document.getElementById('tg-auth-gate-refresh');

    if (copyBtn) {
      copyBtn.addEventListener('click', function () {
        try {
          Lampa.Utils.copyTextToClipboard(uid, function () {
            Lampa.Noty.show('UID скопирован');
          }, function () {
            Lampa.Noty.show('Не удалось скопировать UID');
          });
        } catch (e) {
          Lampa.Noty.show('Не удалось скопировать UID');
        }
      });
    }

    if (openBtn) {
      openBtn.addEventListener('click', function () {
        window.location.href = tgUrl;
      });
    }

    if (refreshBtn) {
      refreshBtn.addEventListener('click', function () {
        checkAccess(true);
      });
    }
  }

  function startPolling() {
    stopPolling();
    pollTimer = setInterval(function () {
      if (!authorized) checkAccess(false);
    }, CHECK_INTERVAL);
  }

  function handleAuthorized(result) {
    try {
      Lampa.Storage.set(USER_KEY, {
        telegramId: result.telegramId || '',
        username: result.username || '',
        role: result.role || 'user',
        expiresAt: result.expiresAt || ''
      });
    } catch (e) {}

    showSuccessOverlay(result || {});
    setTimeout(function () {
      unlockApp();
    }, 1600);
  }

  function handleUnauthorized(uid, result) {
    lockApp();
    buildOverlay(uid, result && result.message ? result.message : 'Подтвердите устройство через Telegram-бота.');
    startPolling();
  }

  function checkAccess(forceNotify) {
    var uid = getUID();

    requestStatus(uid, function (result) {
      if (result && result.authorized) {
        handleAuthorized(result);
      } else {
        handleUnauthorized(uid, result);
        if (forceNotify) {
          Lampa.Noty.show((result && result.message) || 'Устройство ещё не авторизовано');
        }
      }
    }, function () {
      handleUnauthorized(uid, { message: 'Не удалось проверить авторизацию. Сервер недоступен.' });
      if (forceNotify) Lampa.Noty.show('Не удалось проверить авторизацию');
    });
  }

  function startGate() {
    checkAccess(false);
  }

  if (window.appready) {
    startGate();
  } else if (document.readyState === 'complete' || document.readyState === 'interactive') {
    setTimeout(startGate, 500);
  } else {
    document.addEventListener('DOMContentLoaded', function () {
      setTimeout(startGate, 500);
    });
  }
})();
