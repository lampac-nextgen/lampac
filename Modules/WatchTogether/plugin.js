(function () {
  "use strict";

  if (window.WatchTogether_plugin_started) {
    console.log("[WT] Already running.");
    return;
  }
  window.WatchTogether_plugin_started = true;

  var _rawLang = (Lampa.Storage.get("language") || "en").toLowerCase();
  var i18n = {
    uk: {
      menu_title: "WatchTogether",
      settings_title: "WatchTogether",
      param_name: "Ім'я користувача",
      param_name_descr:
        "Як вас бачитимуть інші в кімнатах. Порожньо = ідентифікатор Лампи.",
      param_use_pwd: "Використовувати пароль",
      param_use_pwd_descr:
        "Запитувати пароль при створенні кімнати та підставляти у власні кімнати.",
      param_pwd: "Пароль за замовчуванням",
      param_pwd_descr: "Буде використано при створенні кімнат із паролем.",
      head_title: "WatchTogether - список кімнат",
      create_btn: "Створити кімнату за посиланням",
      full_card_btn: "WatchTogether - Дивитися з друзями",
      settings_open_rooms: "Відкрити список кімнат",
      settings_open_rooms_descr:
        "Показує список доступних кімнат і дозволяє створити свою",
      empty_list: "Відкритих кімнат немає",
      input_url: "Посилання на потік (m3u8 / mp4)",
      input_room_name: "Назва кімнати",
      input_password: "Пароль кімнати",
      input_join_password: "Введіть пароль кімнати",
      wrong_password: "Невірний пароль",
      create_fail: "Не вдалося створити кімнату",
      create_ok: function (n) {
        return 'Кімнату "' + n + '" створено';
      },
      join_ok: function (n) {
        return 'Ви увійшли до кімнати "' + n + '"';
      },
      no_room: "Кімнату не знайдено",
      no_stream: "У цій кімнаті немає потоку",
      kicked: "Ви увійшли до цієї кімнати з іншого пристрою",
      host_left: "Хост покинув кімнату - перегляд завершено",
      net_err: "Помилка мережі",
      need_url: "Не задано посилання на потік",
      create_from_player: "Поділитися останнім потоком",
      pending_share:
        "Запустіть відтворення - кімнату буде створено автоматично",
      label_owner: "Хост",
      label_members: "Глядачів",
      label_locked: "Захищено паролем",
      badge_room: "Кімната",
      badge_viewers: "Глядачів",
      notice_joined: function (n) {
        return n + " приєднався";
      },
      notice_left: function (n) {
        return n + " покинув кімнату";
      },
      notice_paused: function (n) {
        return n + " поставив паузу";
      },
      notice_resumed: function (n) {
        return n + " продовжив відтворення";
      },
      notice_seeked: function (n) {
        return n + " перемотав";
      },
      notice_host_changed: function (n) {
        return "Новий хост: " + n;
      },
      player_create_descr: "Створити кімнату на цей потік",
      already_in_room: function (n) {
        return 'Ви вже в кімнаті "' + n + '"';
      },
      join_mode_title: "Режим перегляду",
      join_mode_direct: "Використати потік хоста (Прямо)",
      join_mode_manual: "Вибрати джерело власноруч",
      copy_link: "Скопіювати веб-посилання",
      link_copied: "Посилання скопійовано!",
    },
    en: {
      menu_title: "WatchTogether",
      settings_title: "WatchTogether",
      param_name: "Display name",
      param_name_descr: "How others see you in rooms. Empty = Lampa ID.",
      param_use_pwd: "Use password",
      param_use_pwd_descr:
        "Ask for password when creating a room and prefill your default.",
      param_pwd: "Default password",
      param_pwd_descr: "Used when creating password-protected rooms.",
      head_title: "WatchTogether - available rooms",
      create_btn: "Create room from URL",
      full_card_btn: "WatchTogether - Watch with friends",
      settings_open_rooms: "Open room browser",
      settings_open_rooms_descr:
        "Shows the list of available rooms and lets you create your own",
      empty_list: "No open rooms",
      input_url: "Stream URL (m3u8 / mp4)",
      input_room_name: "Room name",
      input_password: "Room password",
      input_join_password: "Enter room password",
      wrong_password: "Wrong password",
      create_fail: "Failed to create room",
      create_ok: function (n) {
        return 'Room "' + n + '" created';
      },
      join_ok: function (n) {
        return 'Joined room "' + n + '"';
      },
      no_room: "Room not found",
      no_stream: "This room has no stream",
      kicked: "You joined this room from another device",
      host_left: "Host left the room - session ended",
      net_err: "Network error",
      need_url: "Stream URL is empty",
      create_from_player: "Share last stream",
      pending_share: "Start playback - the room will be created automatically",
      label_owner: "Host",
      label_members: "Viewers",
      label_locked: "Password protected",
      badge_room: "Room",
      badge_viewers: "Viewers",
      notice_joined: function (n) {
        return n + " joined";
      },
      notice_left: function (n) {
        return n + " left the room";
      },
      notice_paused: function (n) {
        return n + " paused";
      },
      notice_resumed: function (n) {
        return n + " resumed playback";
      },
      notice_seeked: function (n) {
        return n + " seeked";
      },
      notice_host_changed: function (n) {
        return "New host: " + n;
      },
      player_create_descr: "Create a room for this stream",
      already_in_room: function (n) {
        return 'You are already in room "' + n + '"';
      },
      join_mode_title: "Join Mode",
      join_mode_direct: "Use Host Stream (Direct)",
      join_mode_manual: "Select Source Manually",
      copy_link: "Copy web link",
      link_copied: "Link copied!",
    },
    ru: {
      menu_title: "WatchTogether",
      settings_title: "WatchTogether",
      param_name: "Имя пользователя",
      param_name_descr:
        "Как вас будут видеть в комнатах. Пусто = идентификатор Лампы.",
      param_use_pwd: "Использовать пароль",
      param_use_pwd_descr:
        "Запрашивать пароль при создании комнаты и подставлять в свои комнаты.",
      param_pwd: "Пароль по умолчанию",
      param_pwd_descr: "Будет использоваться при создании комнат с паролем.",
      head_title: "WatchTogether - список комнат",
      create_btn: "Создать комнату по ссылке",
      full_card_btn: "WatchTogether - Смотреть с друзьями",
      settings_open_rooms: "Открыть список комнат",
      settings_open_rooms_descr:
        "Показывает список доступных комнат и позволяет создать свою",
      empty_list: "Открытых комнат нет",
      input_url: "Ссылка на поток (m3u8 / mp4)",
      input_room_name: "Название комнаты",
      input_password: "Пароль комнаты",
      input_join_password: "Введите пароль комнаты",
      wrong_password: "Неверный пароль",
      create_fail: "Не удалось создать комнату",
      create_ok: function (n) {
        return 'Комната "' + n + '" создана';
      },
      join_ok: function (n) {
        return 'Вошли в комнату "' + n + '"';
      },
      no_room: "Комната не найдена",
      no_stream: "В этой комнате нет потока",
      kicked: "Вы вошли в комнату с другого устройства",
      host_left: "Хост покинул комнату - просмотр завершён",
      net_err: "Ошибка сети",
      need_url: "Не задана ссылка на поток",
      create_from_player: "Поделиться последним потоком",
      pending_share:
        "Запустите воспроизведение - комната будет создана автоматически",
      label_owner: "Хост",
      label_members: "Зрителей",
      label_locked: "Защищено паролем",
      badge_room: "Комната",
      badge_viewers: "Зрителей",
      notice_joined: function (n) {
        return n + " присоединился";
      },
      notice_left: function (n) {
        return n + " покинул комнату";
      },
      notice_paused: function (n) {
        return n + " поставил паузу";
      },
      notice_resumed: function (n) {
        return n + " продолжил воспроизведение";
      },
      notice_seeked: function (n) {
        return n + " перемотал";
      },
      notice_host_changed: function (n) {
        return "Новый хост: " + n;
      },
      player_create_descr: "Создать комнату с этим потоком",
      already_in_room: function (n) {
        return 'Вы уже в комнате "' + n + '"';
      },
      join_mode_title: "Режим просмотра",
      join_mode_direct: "Использовать поток хоста (Напрямую)",
      join_mode_manual: "Выбрать источник вручную",
      copy_link: "Скопировать веб-ссылку",
      link_copied: "Ссылка скопирована!",
    },
  };
  var T = i18n[_rawLang] || i18n["en"];
  console.log(
    "[WT] language detected:",
    _rawLang,
    "→ using",
    i18n[_rawLang] ? _rawLang : "en (fallback)",
  );

  var unic_id = Lampa.Storage.get("lampac_unic_id", "");
  if (!unic_id) {
    unic_id = Lampa.Utils.uid(8).toLowerCase();
    Lampa.Storage.set("lampac_unic_id", unic_id);
  }

  function getDisplayName() {
    var custom = (Lampa.Storage.get("watchtogether_display_name", "") || "")
      .toString()
      .trim();
    return custom || unic_id;
  }
  function isUsePassword() {
    return Lampa.Storage.field("watchtogether_use_password") === true;
  }
  function getDefaultPassword() {
    return (
      Lampa.Storage.get("watchtogether_default_password", "") || ""
    ).toString();
  }

  var localhost = "{localhost}/";
  var inRoom = false;
  var currentRoomId = null;
  var currentRoomName = "";
  var currentRoomOwnerUid = null;
  var currentRoomPassword = "";
  var episodeSwitchPending = false;
  var assignedDisplayName = getDisplayName();

  function iAmHost() {
    return !!currentRoomOwnerUid && currentRoomOwnerUid === unic_id;
  }
  var lastStreamUrl = null;
  var lastStreamTitle = null;
  var pendingShareCard = null;
  var lastViewedCard = null;
  var serverTimeOffset = 0;
  var pingSamples = [];
  var pingPending = {};
  var pingBurstTimer = null;
  var pingPeriodicTimer = null;
  var pingWatchdogTimer = null;
  var PING_INTERVAL_MS = 20000;
  var PONG_TIMEOUT_MS = 30000;

  function serverNow() {
    return Date.now() + serverTimeOffset;
  }

  function sendPing() {
    if (!ws || ws.readyState !== 1) return;
    var t0 = Date.now();
    pingPending[t0] = true;
    sendWs("watchtogether_ping", [t0]);
  }

  function handlePong(t0, serverT) {
    if (!pingPending[t0]) return;
    delete pingPending[t0];
    var t1 = Date.now();
    var rtt = t1 - t0;
    var offset = serverT + rtt / 2 - t1;
    pingSamples.push({ offset: offset, rtt: rtt });
    if (pingSamples.length > 8) pingSamples.shift();
    var best = pingSamples.reduce(function (a, b) {
      return a.rtt < b.rtt ? a : b;
    });
    serverTimeOffset = best.offset;
  }

  function startPingBurst() {
    if (pingBurstTimer) clearInterval(pingBurstTimer);
    sendPing();
    var n = 0;
    pingBurstTimer = setInterval(function () {
      sendPing();
      n++;
      if (n >= 3) {
        clearInterval(pingBurstTimer);
        pingBurstTimer = null;
      }
    }, 500);
  }

  function startPingPeriodic() {
    if (pingPeriodicTimer) clearInterval(pingPeriodicTimer);
    pingPeriodicTimer = setInterval(sendPing, PING_INTERVAL_MS);
  }

  function startPingWatchdog() {
    if (pingWatchdogTimer) clearInterval(pingWatchdogTimer);
    pingWatchdogTimer = setInterval(checkPingWatchdog, 5000);
  }

  function checkPingWatchdog() {
    if (!ws || ws.readyState !== 1) return;
    var now = Date.now();
    for (var key in pingPending) {
      if (!pingPending.hasOwnProperty(key)) continue;
      var t0 = parseInt(key, 10);
      if (!isNaN(t0) && now - t0 > PONG_TIMEOUT_MS) {
        console.log("[WT] pong watchdog timeout - forcing reconnect");
        try {
          ws.close();
        } catch (err) {}
        return;
      }
    }
  }

  function stopPingTimers() {
    if (pingPeriodicTimer) {
      clearInterval(pingPeriodicTimer);
      pingPeriodicTimer = null;
    }
    if (pingBurstTimer) {
      clearInterval(pingBurstTimer);
      pingBurstTimer = null;
    }
    if (pingWatchdogTimer) {
      clearInterval(pingWatchdogTimer);
      pingWatchdogTimer = null;
    }
  }

  var ws;
  var syncInterval;
  var isSystemSyncing = false;
  var lastUserActionTime = 0;
  var initialSyncLock = false;
  var targetInitialState = null;
  var expectedState = { seek: -1, play: false, pause: false };
  var currentRoomMembers = [];

  var SYNC_HEARTBEAT_MS = 2000;
  var SYNC_TOLERANCE_S = 0.3;
  var SYNC_HARD_SEEK_S = 1.5;
  var SYNC_RATE_GAIN = 0.1;
  var SYNC_MAX_RATE_OFFSET = 0.1;
  var SYNC_RATE_RESET_MS = 4000;

  function account(url) {
    url = url + "";
    if (url.indexOf("account_email=") == -1) {
      var email = Lampa.Storage.get("account_email");
      if (email)
        url = Lampa.Utils.addUrlComponent(
          url,
          "account_email=" + encodeURIComponent(email),
        );
    }
    if (url.indexOf("uid=") == -1) {
      url = Lampa.Utils.addUrlComponent(
        url,
        "uid=" + encodeURIComponent(unic_id),
      );
    }
    return url;
  }

  function getTmdbId(card) {
    if (!card) return 0;
    return card.id || card.tmdb_id || 0;
  }

  function getCardPoster(card) {
    if (!card) return "";
    if (card.poster_path)
      return "https://image.tmdb.org/t/p/w300" + card.poster_path;
    return card.img || card.background_image || "";
  }

  function expectedPositionNow(state, basePosition, atServerTime) {
    if (state !== "playing" || !atServerTime || atServerTime <= 0)
      return basePosition;
    var elapsedSec = (serverNow() - atServerTime) / 1000;
    if (elapsedSec < 0 || elapsedSec > 3600) return basePosition;
    return basePosition + elapsedSec;
  }

  var passwordParamItem = null;
  function updatePasswordVisibility() {
    if (passwordParamItem)
      passwordParamItem.toggleClass("hide", !isUsePassword());
  }

  function registerSettings() {
    var META = {
      name: "WatchTogether",
      version: "2.0.0",
      author: "lampac",
    };

    if (!Lampa.SettingsApi) return;

    Lampa.SettingsApi.addComponent({
      component: "watchtogether",
      name: T.settings_title,
      icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="6"></circle><path d="M10.5 9.5 L10.5 14.5 L15 12 Z" fill="currentColor" stroke="none"></path><circle cx="4" cy="4" r="1.8" fill="currentColor" stroke="none"></circle><circle cx="20" cy="4" r="1.8" fill="currentColor" stroke="none"></circle><circle cx="4" cy="20" r="1.8" fill="currentColor" stroke="none"></circle><circle cx="20" cy="20" r="1.8" fill="currentColor" stroke="none"></circle></svg>',
    });

    Lampa.SettingsApi.addParam({
      component: "watchtogether",
      param: { name: "watchtogether_meta", type: "static" },
      field: {
        name: META.name + " v" + META.version,
        description: "Author: " + META.author,
      },
      onRender: function (item) {
        item.on("hover:enter", function () {});
      },
    });

    Lampa.SettingsApi.addParam({
      component: "watchtogether",
      param: { name: "watchtogether_open_rooms", type: "button" },
      field: {
        name: T.settings_open_rooms,
        description: T.settings_open_rooms_descr,
      },
      onChange: function () {
        openRoomBrowser();
      },
    });

    Lampa.SettingsApi.addParam({
      component: "watchtogether",
      param: {
        name: "watchtogether_display_name",
        type: "input",
        values: "",
        default: "",
      },
      field: { name: T.param_name, description: T.param_name_descr },
      onChange: function () {
        assignedDisplayName = getDisplayName();
      },
    });

    Lampa.SettingsApi.addParam({
      component: "watchtogether",
      param: {
        name: "watchtogether_use_password",
        type: "trigger",
        default: false,
      },
      field: { name: T.param_use_pwd, description: T.param_use_pwd_descr },
      onChange: function () {
        updatePasswordVisibility();
      },
    });

    Lampa.SettingsApi.addParam({
      component: "watchtogether",
      param: {
        name: "watchtogether_default_password",
        type: "input",
        values: "",
        default: "",
      },
      field: { name: T.param_pwd, description: T.param_pwd_descr },
      onRender: function (item) {
        passwordParamItem = item;
        updatePasswordVisibility();
      },
    });
  }

  function openRoomBrowser() {
    var url = account(localhost + "watchtogether/list");
    Lampa.Network.silent(
      url,
      function (res) {
        var items = [];

        items.push({
          title: '<span style="color:#00e676">+ ' + T.create_btn + "</span>",
          action: "create",
        });

        (res && res.rooms ? res.rooms : []).forEach(function (r) {
          var lock = r.has_password ? " 🔒" : "";
          var titleHtml =
            "<b>" +
            safe(r.name) +
            "</b>" +
            lock +
            '<br><span style="opacity:.7;font-size:.85em">' +
            safe(r.title || "") +
            " · " +
            T.label_owner +
            ": " +
            safe(r.owner || "") +
            " · " +
            T.label_members +
            ": " +
            (r.members || 0) +
            "</span>";
          items.push({ title: titleHtml, room: r });
        });

        if (items.length === 1) {
          items.push({
            title: '<span style="opacity:.6">' + T.empty_list + "</span>",
            disabled: true,
          });
        }

        Lampa.Select.show({
          title: T.head_title,
          items: items,
          onSelect: function (a) {
            if (a.disabled) return;
            if (a.action === "create") return askCreateRoom();
            if (a.room) return tryJoinRoom(a.room);
          },
          onBack: function () {
            Lampa.Controller.toggle("content");
          },
        });
      },
      function () {
        Lampa.Noty.show(T.net_err);
      },
    );
  }

  function safe(s) {
    return (s + "").replace(/[<>&"']/g, function (c) {
      return "&#" + c.charCodeAt(0) + ";";
    });
  }

  function tryJoinRoom(room) {
    if (!room.has_password) return joinRoom(room.id, "");

    var prefill = isUsePassword() ? getDefaultPassword() : "";
    Lampa.Input.edit(
      {
        title: T.input_join_password,
        value: prefill,
        free: true,
        nosave: true,
      },
      function (val) {
        joinRoom(room.id, val || "");
      },
    );
  }

  function joinRoom(roomId, password) {
    var url = account(
      localhost +
        "watchtogether/join?id=" +
        encodeURIComponent(roomId) +
        "&password=" +
        encodeURIComponent(password || ""),
    );
    Lampa.Network.silent(
      url,
      function (res) {
        if (res && res.id) {
          currentRoomPassword = password || "";
          doJoinAndPlay(res);
        } else Lampa.Noty.show(T.no_room);
      },
      function (xhr) {
        if (xhr && xhr.status === 401) Lampa.Noty.show(T.wrong_password);
        else if (xhr && xhr.status === 404) Lampa.Noty.show(T.no_room);
        else Lampa.Noty.show(T.net_err);
      },
    );
  }

  function doJoinAndPlay(room) {
    if (!room.stream_url && !room.tmdb_id) {
      Lampa.Noty.show(T.no_stream);
      return;
    }

    var options = [];
    if (room.stream_url) {
      options.push({ title: T.join_mode_direct, useStream: true });
    }
    if (room.tmdb_id) {
      options.push({ title: T.join_mode_manual, useStream: false });
    }

    if (options.length === 1) {
      proceedJoin(room, options[0].useStream);
    } else {
      Lampa.Select.show({
        title: T.join_mode_title,
        items: options,
        onSelect: function (a) {
          proceedJoin(room, a.useStream);
        },
        onBack: function () {
          Lampa.Controller.toggle("content");
        },
      });
    }
  }

  function proceedJoin(room, useStream) {
    currentRoomId = room.id;
    currentRoomName = room.name || "";
    currentRoomOwnerUid = room.owner_uid || null;
    inRoom = true;

    var roomState = room.state || "paused";
    var roomPos = room.position || 0;
    var needsInitialSync = roomState === "playing" || roomPos > 0.5;

    initialSyncLock = needsInitialSync;
    targetInitialState = needsInitialSync
      ? {
          state: roomState,
          position: roomPos,
          atServerTime: room.at_server_time || 0,
        }
      : null;

    Lampa.Noty.show(T.join_ok(currentRoomName));

    sendWs("watchtogether_join", [
      currentRoomId,
      unic_id,
      getDisplayName(),
      currentRoomPassword,
    ]);

    if (useStream) {
      Lampa.Player.play({
        url: room.stream_url,
        title: room.title || currentRoomName,
        poster: room.poster || "",
      });
    } else {
      Lampa.Activity.push({
        url: "",
        title: room.title || currentRoomName,
        component: "full",
        id: room.tmdb_id,
        source: room.source || "tmdb",
        method: room.type || "movie",
      });
    }
  }

  function hostJoinExistingPlayback(roomId, roomName, password) {
    currentRoomId = roomId;
    currentRoomName = roomName || "";
    currentRoomPassword = password || "";
    currentRoomOwnerUid = unic_id;
    inRoom = true;

    initialSyncLock = false;
    targetInitialState = null;

    sendWs("watchtogether_join", [
      currentRoomId,
      unic_id,
      getDisplayName(),
      currentRoomPassword,
    ]);

    var vid = getVideo();
    if (vid) {
      var state = vid.paused ? "paused" : "playing";
      sendWs("watchtogether_sync", [
        currentRoomId,
        unic_id,
        state,
        vid.currentTime || 0,
      ]);
    }

    Lampa.Noty.show(T.join_ok(currentRoomName || roomId));
  }

  function askCreateRoom() {
    if (lastStreamUrl) {
      Lampa.Select.show({
        title: T.create_btn,
        items: [
          {
            title:
              T.create_from_player + " (" + safe(lastStreamTitle || "") + ")",
            share: true,
          },
          { title: T.input_url, share: false },
        ],
        onSelect: function (a) {
          if (a.share)
            askRoomDetails({
              stream_url: lastStreamUrl,
              title: lastStreamTitle || "",
            });
          else promptStreamUrl();
        },
        onBack: function () {
          Lampa.Controller.toggle("content");
        },
      });
    } else {
      promptStreamUrl();
    }
  }

  function createRoomFromPending(room, streamUrl) {
    var url = account(localhost + "watchtogether/create");
    var pwd = isUsePassword() ? getDefaultPassword() : "";
    var data = {
      tmdb_id: room.id || 0,
      source: room.source || "",
      type: room.method || "",
      name: "",
      title: room.title || "",
      poster: room.img || "",
      stream_url: streamUrl || "",
      password: pwd,
      owner_uid: unic_id,
      owner_name: getDisplayName(),
      initial_state: "paused",
      initial_position: 0,
    };

    network.clear();
    network.timeout(5000);
    network.request(
      url,
      function (res) {
        currentRoomPassword = pwd;
        if (streamUrl) res.stream_url = streamUrl;
        if (room.id) res.tmdb_id = room.id;
        res.title = room.title;
        res.poster = room.img;
        res.source = room.source;
        res.type = room.method;
        doJoinAndPlay(res);
      },
      function (a, c) {
        Lampa.Noty.show(network.errorDecode(a, c));
      },
      data,
    );
  }

  function promptStreamUrl() {
    Lampa.Input.edit(
      {
        title: T.input_url,
        value: "",
        free: true,
        nosave: true,
      },
      function (val) {
        if (!val) return Lampa.Noty.show(T.need_url);
        askRoomDetails({ stream_url: val, title: "" });
      },
    );
  }

  function askRoomDetails(seed) {
    Lampa.Input.edit(
      {
        title: T.input_room_name,
        value: seed.title || "Room " + Math.floor(Math.random() * 1000),
        free: true,
        nosave: true,
      },
      function (name) {
        if (!name) name = "Room";
        if (isUsePassword()) {
          var pre = getDefaultPassword();
          Lampa.Input.edit(
            {
              title: T.input_password,
              value: pre,
              free: true,
              nosave: true,
            },
            function (pwd) {
              seed.name = name;
              seed.password = pwd || "";
              createRoom(seed, false);
            },
          );
        } else {
          seed.name = name;
          seed.password = "";
          createRoom(seed, false);
        }
      },
    );
  }

  var createPending = false;
  function createRoom(seed, hostAlreadyPlaying) {
    if (createPending) return;
    createPending = true;

    var url = account(localhost + "watchtogether/create");
    var postData = {
      stream_url: seed.stream_url || "",
      title: seed.title || "",
      poster: seed.poster || "",
      name: seed.name || "",
      password: seed.password || "",
      tmdb_id: seed.tmdb_id || 0,
      source: seed.source || "",
      type: seed.type || "",
      initial_state: seed.initial_state || "",
      initial_position: seed.initial_position || 0,
      owner_uid: unic_id,
      owner_name: getDisplayName(),
    };

    Lampa.Network.silent(
      url,
      function (res) {
        createPending = false;
        if (res && res.id) {
          Lampa.Noty.show(T.create_ok(res.name || res.id));
          if (hostAlreadyPlaying) {
            hostJoinExistingPlayback(
              res.id,
              res.name || seed.name || "",
              seed.password || "",
            );
          } else {
            joinRoom(res.id, seed.password || "");
          }
        } else {
          Lampa.Noty.show(T.create_fail);
        }
      },
      function () {
        createPending = false;
        Lampa.Noty.show(T.create_fail);
      },
      postData,
    );
  }

  function autoCreateRoomFromPending(card, streamUrl) {
    var tmdb_id = getTmdbId(card);
    var title =
      card.title ||
      card.name ||
      card.original_title ||
      card.original_name ||
      "";
    var source = card.source || "tmdb";
    var type =
      card.name || card.number_of_seasons || card.first_air_date
        ? "tv"
        : "movie";
    var poster = getCardPoster(card);

    var vid = getVideo();
    var state = vid && !vid.paused ? "playing" : "paused";
    var position = vid ? vid.currentTime || 0 : 0;

    var seed = {
      stream_url: streamUrl,
      title: title,
      poster: poster,
      tmdb_id: tmdb_id,
      source: source,
      type: type,
      name: title || "Room " + Math.floor(Math.random() * 1000),
      password: "",
      initial_state: state,
      initial_position: position,
    };

    if (isUsePassword()) {
      Lampa.Input.edit(
        {
          title: T.input_password,
          value: getDefaultPassword(),
          free: true,
          nosave: true,
        },
        function (pwd) {
          seed.password = pwd || "";
          createRoom(seed, true);
        },
      );
    } else {
      createRoom(seed, true);
    }
  }

  Lampa.Listener.follow("app", function (e) {
    if (e.type !== "ready" || window.WatchTogether_head_added) return;
    if (!Lampa.Head || typeof Lampa.Head.addIcon !== "function") return;
    window.WatchTogether_head_added = true;

    var svg =
      '<svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="6"></circle><path d="M10.5 9.5 L10.5 14.5 L15 12 Z" fill="currentColor" stroke="none"></path><circle cx="4" cy="4" r="1.8" fill="currentColor" stroke="none"></circle><circle cx="20" cy="4" r="1.8" fill="currentColor" stroke="none"></circle><circle cx="4" cy="20" r="1.8" fill="currentColor" stroke="none"></circle><circle cx="20" cy="20" r="1.8" fill="currentColor" stroke="none"></circle></svg>';
    var btn = Lampa.Head.addIcon(svg, openRoomBrowser);
    if (btn && btn.attr) btn.attr("title", T.menu_title);
  });

  Lampa.Listener.follow("full", function (e) {
    if (e.type !== "complite") return;

    var cardData =
      (e.data && e.data.movie) ||
      (e.object && e.object.activity && e.object.activity.object
        ? e.object.activity.object.item
        : null) ||
      (e.object && e.object.item);
    if (!cardData) return;

    lastViewedCard = cardData;
  });

  function getVideo() {
    var vid = null;
    if (typeof Lampa.PlayerVideo !== "undefined" && Lampa.PlayerVideo.video)
      vid = Lampa.PlayerVideo.video();
    if (!vid)
      vid =
        document.querySelector(".player-video__display video") ||
        document.querySelector(".player video") ||
        document.querySelector("video");
    return vid;
  }

  var debugRefreshTimer = null;

  function buildDebugHtml() {
    var wsState = ws
      ? ["CONNECTING", "OPEN", "CLOSING", "CLOSED"][ws.readyState] || "UNKNOWN"
      : "NO_WS";
    var lastRtt = pingSamples.length
      ? pingSamples[pingSamples.length - 1].rtt
      : "-";
    var bestRtt = pingSamples.length
      ? pingSamples.reduce(function (a, b) {
          return a.rtt < b.rtt ? a : b;
        }).rtt
      : "-";
    var pendingCount = 0;
    for (var k in pingPending) {
      if (pingPending.hasOwnProperty(k)) pendingCount++;
    }
    var pos = "-",
      bufRdy = "-";
    var vid = getVideo();
    if (vid) {
      pos = (vid.currentTime || 0).toFixed(2);
      bufRdy = vid.readyState + (vid._lp_buffering ? "*" : "");
    }
    return [
      "<div><b>WS:</b> " + safe(wsState) + "</div>",
      "<div><b>Room:</b> " + safe(currentRoomId || "-") + "</div>",
      "<div><b>Members:</b> " + currentRoomMembers.length + "</div>",
      "<div><b>Clock offset:</b> " + Math.round(serverTimeOffset) + " ms</div>",
      "<div><b>RTT last/best:</b> " + lastRtt + " / " + bestRtt + " ms</div>",
      "<div><b>Ping pending:</b> " + pendingCount + "</div>",
      "<div><b>initialSyncLock:</b> " + initialSyncLock + "</div>",
      "<div><b>Video:</b> " + pos + " s (ready " + bufRdy + ")</div>",
    ].join("");
  }

  function updateRoomBadge() {
    if (!inRoom || !currentRoomId) return;
    var nameContainer = $(".player-info__name, .player-panel__name");
    if (nameContainer.length && !$(".watchtogether-room-badge").length) {
      var badge = $(
        '<div class="watchtogether-room-badge" style="position:relative;display:inline-block;margin-left:15px;padding:4px 12px;background:rgba(255,255,255,0.15);border-radius:6px;font-size:0.85em;color:#fff;cursor:pointer;">' +
          '<span class="watchtogether-room-badge-text"></span>' +
          '<div class="watchtogether-debug-panel" style="display:none;position:absolute;top:100%;left:0;margin-top:6px;padding:8px 12px;background:rgba(0,0,0,0.9);border:1px solid rgba(255,255,255,0.25);border-radius:6px;z-index:9999;white-space:nowrap;font-family:monospace;font-size:0.85em;line-height:1.5;text-align:left;color:#fff;"></div>' +
          "</div>",
      );
      badge.on("mouseenter", function () {
        var $p = $(this).find(".watchtogether-debug-panel");
        $p.html(buildDebugHtml()).css("display", "block");
        if (debugRefreshTimer) clearInterval(debugRefreshTimer);
        debugRefreshTimer = setInterval(function () {
          if ($p.is(":visible")) $p.html(buildDebugHtml());
          else {
            clearInterval(debugRefreshTimer);
            debugRefreshTimer = null;
          }
        }, 500);
      });
      badge.on("mouseleave", function () {
        $(this).find(".watchtogether-debug-panel").css("display", "none");
        if (debugRefreshTimer) {
          clearInterval(debugRefreshTimer);
          debugRefreshTimer = null;
        }
      });
      nameContainer.after(badge);
    }
    var $badge = $(".watchtogether-room-badge");
    if (!$badge.length) return;

    var wsOk = ws && ws.readyState === 1;
    var dotColor = wsOk ? "#00e676" : "#ff5252";
    var dotTitle = wsOk ? "WS connected" : "WS disconnected";
    var dot =
      '<span style="display:inline-block;width:8px;height:8px;border-radius:50%;background:' +
      dotColor +
      ';margin-right:8px;vertical-align:middle;" title="' +
      dotTitle +
      '"></span>';

    $badge
      .find(".watchtogether-room-badge-text")
      .html(
        dot +
          T.badge_room +
          ': <b style="color:#00e676;">' +
          safe(currentRoomName || currentRoomId) +
          "</b> | " +
          T.badge_viewers +
          ": <b>" +
          currentRoomMembers.length +
          "</b>",
      );
  }

  function clearRateAdjust(vid) {
    if (vid._lp_rate_timeout) {
      clearTimeout(vid._lp_rate_timeout);
      vid._lp_rate_timeout = null;
    }
    if (vid.playbackRate !== 1) vid.playbackRate = 1;
  }

  function applySync(vid, state, basePosition, atServerTime) {
    if (vid.currentTime === undefined) return;

    var expected = expectedPositionNow(state, basePosition, atServerTime);
    var diff = vid.currentTime - expected;
    var absDiff = Math.abs(diff);

    if (absDiff > SYNC_HARD_SEEK_S) {
      expectedState.seek = expected;
      vid.currentTime = expected;
      clearRateAdjust(vid);
    } else if (absDiff > SYNC_TOLERANCE_S) {
      var raw = diff * SYNC_RATE_GAIN;
      var offset = Math.max(
        -SYNC_MAX_RATE_OFFSET,
        Math.min(SYNC_MAX_RATE_OFFSET, raw),
      );
      var newRate = 1 - offset;
      if (Math.abs(vid.playbackRate - newRate) > 0.005)
        vid.playbackRate = newRate;
      if (vid._lp_rate_timeout) clearTimeout(vid._lp_rate_timeout);
      vid._lp_rate_timeout = setTimeout(function () {
        vid._lp_rate_timeout = null;
        if (vid.playbackRate !== 1) vid.playbackRate = 1;
      }, SYNC_RATE_RESET_MS);
    } else {
      clearRateAdjust(vid);
    }

    if (state === "playing" && vid.paused) {
      expectedState.play = true;
      setTimeout(function () {
        expectedState.play = false;
      }, 500);
      if (typeof Lampa.PlayerVideo !== "undefined" && Lampa.PlayerVideo.play)
        Lampa.PlayerVideo.play();
      else {
        var p = vid.play();
        if (p && p.catch)
          p.catch(function () {
            expectedState.play = false;
          });
      }
    } else if (state === "paused" && !vid.paused) {
      expectedState.pause = true;
      setTimeout(function () {
        expectedState.pause = false;
      }, 500);
      if (vid.playbackRate !== 1) vid.playbackRate = 1;
      if (typeof Lampa.PlayerVideo !== "undefined" && Lampa.PlayerVideo.pause)
        Lampa.PlayerVideo.pause();
      else vid.pause();
    }
  }

  function sendWs(method, args) {
    if (ws && ws.readyState === 1) {
      ws.send(JSON.stringify({ method: method, args: args }));
    }
  }

  function sendSync(state, isAction) {
    if (!inRoom || initialSyncLock) return;
    var vid = getVideo();
    if (!vid) return;
    var method = isAction ? "watchtogether_action" : "watchtogether_sync";
    sendWs(method, [currentRoomId, unic_id, state, vid.currentTime || 0]);
  }

  function formatNotice(verb, who) {
    if (!who) return "";
    if (verb === "joined") return T.notice_joined(who);
    if (verb === "left") return T.notice_left(who);
    if (verb === "paused") return T.notice_paused(who);
    if (verb === "resumed" || verb === "playing") return T.notice_resumed(who);
    if (verb === "seeked") return T.notice_seeked(who);
    if (verb === "host_changed") return T.notice_host_changed(who);
    return who + " · " + verb;
  }

  function leaveRoomLocal() {
    clearInterval(syncInterval);
    $(".watchtogether-room-badge").remove();
    var vid = getVideo();
    if (vid) clearRateAdjust(vid);
    expectedState = { seek: -1, play: false, pause: false };
    initialSyncLock = false;
    targetInitialState = null;
    isSystemSyncing = false;
    inRoom = false;
    currentRoomId = null;
    currentRoomName = "";
    currentRoomOwnerUid = null;
    episodeSwitchPending = false;
    currentRoomMembers = [];
  }

  function connectWs() {
    if (!window.lampa_nws_url) {
      var backendIsHttps = localhost.indexOf("https://") === 0;
      var pageIsHttps = window.location.protocol === "https:";
      var protocol = backendIsHttps || pageIsHttps ? "wss:" : "ws:";
      var host = localhost
        .replace("https://", "")
        .replace("http://", "")
        .replace(/\/$/, "");
      window.lampa_nws_url = protocol + "//" + host + "/nws";
    }
    if (ws) {
      if (ws.readyState === 1) return;
      if (ws.readyState === 0 || ws.readyState === 2) {
        try {
          ws.close();
        } catch (err) {}
      }
    }

    pingPending = {};
    ws = new WebSocket(window.lampa_nws_url);

    ws.onopen = function () {
      startPingBurst();
      startPingPeriodic();
      startPingWatchdog();
      updateRoomBadge();
      if (inRoom && currentRoomId) {
        sendWs("watchtogether_join", [
          currentRoomId,
          unic_id,
          getDisplayName(),
        ]);
        var vid = getVideo();
        if (vid && !initialSyncLock) {
          var state = vid.paused ? "paused" : "playing";
          sendWs("watchtogether_sync", [
            currentRoomId,
            unic_id,
            state,
            vid.currentTime || 0,
          ]);
        }
      }
    };
    ws.onmessage = function (e) {
      try {
        var data = JSON.parse(e.data);
        if (!data.method || data.method.indexOf("watchtogether_") !== 0) return;

        if (data.method === "watchtogether_pong") {
          var t0 = (data.args && data.args[0]) || 0;
          var st = (data.args && data.args[1]) || 0;
          handlePong(t0, st);
          return;
        }

        if (data.method === "watchtogether_server_ping") {
          return;
        }

        if (data.method === "watchtogether_joined") {
          assignedDisplayName =
            (data.args && data.args[0]) || assignedDisplayName;
        } else if (data.method === "watchtogether_members") {
          currentRoomMembers = (data.args && data.args[1]) || [];
          updateRoomBadge();
        } else if (data.method === "watchtogether_notice") {
          var verb = (data.args && data.args[0]) || "";
          var who = (data.args && data.args[1]) || "";
          var text = formatNotice(verb, who);
          if (text) Lampa.Noty.show(text);
        } else if (data.method === "watchtogether_kicked") {
          Lampa.Noty.show(T.kicked);
          leaveRoomLocal();
        } else if (data.method === "watchtogether_host_left") {
          Lampa.Noty.show(T.host_left);
          leaveRoomLocal();
          try {
            if (typeof Lampa.Player !== "undefined" && Lampa.Player.close)
              Lampa.Player.close();
            else if (
              typeof Lampa.Activity !== "undefined" &&
              Lampa.Activity.backward
            )
              Lampa.Activity.backward();
          } catch (err) {}
        } else if (data.method === "watchtogether_host_changed") {
          var newOwnerUid = (data.args && data.args[0]) || null;
          if (newOwnerUid) currentRoomOwnerUid = newOwnerUid;
        } else if (data.method === "watchtogether_url_change") {
          if (!inRoom) return;
          var newUrl = (data.args && data.args[0]) || "";
          var newTitle = (data.args && data.args[1]) || "";
          if (!newUrl) return;
          episodeSwitchPending = true;
          try {
            Lampa.Player.play({
              url: newUrl,
              title: newTitle || currentRoomName,
              poster: "",
            });
          } catch (err) {}
        } else if (data.method === "watchtogether_error") {
          var err = (data.args && data.args[0]) || "";
          if (err === "room_not_found") Lampa.Noty.show(T.no_room);
        } else if (data.method === "watchtogether_sync_update") {
          if (!inRoom) return;
          var state = data.args[0];
          var position = data.args[1];
          var atServerTime = data.args[2] || 0;

          if (initialSyncLock) {
            targetInitialState = {
              state: state,
              position: position,
              atServerTime: atServerTime,
            };
            return;
          }
          var vid = getVideo();
          if (!vid) return;

          if (Date.now() - lastUserActionTime < 2000) {
            sendSync(vid.paused ? "paused" : "playing", false);
            return;
          }
          isSystemSyncing = true;
          applySync(vid, state, position, atServerTime);
          setTimeout(function () {
            isSystemSyncing = false;
          }, 500);
        }
      } catch (err) {}
    };
    ws.onclose = function () {
      stopPingTimers();
      updateRoomBadge();
      setTimeout(connectWs, 5000);
    };
    ws.onerror = function () {};
  }

  setInterval(function () {
    if (!inRoom) {
      $(".watchtogether-room-badge").remove();
      return;
    }
    var vid = getVideo();
    if (!vid) return;
    updateRoomBadge();

    if (vid._lp_hooked) return;
    vid._lp_hooked = true;

    var enforceInitial = function () {
      if (!initialSyncLock || !targetInitialState) return;
      var expected = expectedPositionNow(
        targetInitialState.state,
        targetInitialState.position,
        targetInitialState.atServerTime,
      );
      if (Math.abs(vid.currentTime - expected) > 1) {
        expectedState.seek = expected;
        vid.currentTime = expected;
      }
      if (targetInitialState.state === "paused") {
        expectedState.pause = true;
        if (typeof Lampa.PlayerVideo !== "undefined" && Lampa.PlayerVideo.pause)
          Lampa.PlayerVideo.pause();
        else vid.pause();
      } else {
        expectedState.play = true;
        if (typeof Lampa.PlayerVideo !== "undefined" && Lampa.PlayerVideo.play)
          Lampa.PlayerVideo.play();
        else {
          var p = vid.play();
          if (p) p.catch(function () {});
        }
      }
      if (!vid._lp_enforce_timeout) {
        vid._lp_enforce_timeout = setTimeout(function () {
          initialSyncLock = false;
          targetInitialState = null;
        }, 3000);
      }
    };

    if (vid.readyState >= 1) enforceInitial();
    else vid.addEventListener("loadedmetadata", enforceInitial);
    vid.addEventListener("canplay", enforceInitial);

    vid.addEventListener("waiting", function () {
      vid._lp_buffering = true;
    });
    vid.addEventListener("canplay", function () {
      vid._lp_buffering = false;
    });
    vid.addEventListener("playing", function () {
      vid._lp_buffering = false;
    });

    vid.addEventListener("play", function () {
      if (initialSyncLock) {
        if (targetInitialState && targetInitialState.state === "paused") {
          if (
            typeof Lampa.PlayerVideo !== "undefined" &&
            Lampa.PlayerVideo.pause
          )
            Lampa.PlayerVideo.pause();
          else vid.pause();
        }
        return;
      }
      var wasExpected = expectedState.play;
      expectedState.play = false;
      if (wasExpected) return;
      if (vid._lp_buffer_paused) {
        vid._lp_buffer_paused = false;
        return;
      }
      lastUserActionTime = Date.now();
      sendSync("playing", true);
    });

    vid.addEventListener("pause", function () {
      if (vid._lp_rate_timeout) {
        clearTimeout(vid._lp_rate_timeout);
        vid._lp_rate_timeout = null;
      }
      vid.playbackRate = 1;
      if (initialSyncLock) return;
      var wasExpected = expectedState.pause;
      expectedState.pause = false;
      if (wasExpected) return;
      if (vid._lp_buffering || vid.readyState < 3) {
        vid._lp_buffer_paused = true;
        return;
      }
      lastUserActionTime = Date.now();
      sendSync("paused", true);
    });

    vid.addEventListener("seeked", function () {
      if (!isSystemSyncing) {
        if (vid._lp_rate_timeout) {
          clearTimeout(vid._lp_rate_timeout);
          vid._lp_rate_timeout = null;
        }
        vid.playbackRate = 1;
      }
      if (initialSyncLock) {
        if (targetInitialState) {
          var expected = expectedPositionNow(
            targetInitialState.state,
            targetInitialState.position,
            targetInitialState.atServerTime,
          );
          if (Math.abs(vid.currentTime - expected) > 1) {
            expectedState.seek = expected;
            vid.currentTime = expected;
          }
        }
        return;
      }
      if (isSystemSyncing) return;
      if (expectedState.seek !== -1) {
        if (Math.abs(vid.currentTime - expectedState.seek) < 1) {
          expectedState.seek = -1;
          return;
        }
        vid.currentTime = expectedState.seek;
        return;
      }
      lastUserActionTime = Date.now();
      sendSync(vid.paused ? "paused" : "playing", true);
    });

    clearInterval(syncInterval);
    syncInterval = setInterval(function () {
      if (
        inRoom &&
        vid &&
        !vid.paused &&
        expectedState.seek === -1 &&
        !initialSyncLock &&
        !isSystemSyncing
      ) {
        sendSync("playing", false);
      }
    }, SYNC_HEARTBEAT_MS);
  }, 1000);

  function onPlayerStart(e) {
    if (!e || !e.url) return;
    lastStreamUrl = e.url;
    lastStreamTitle =
      (e.movie && (e.movie.title || e.movie.name)) || e.title || "";

    if (episodeSwitchPending && inRoom) {
      episodeSwitchPending = false;
      if (iAmHost()) {
        sendWs("watchtogether_url_change", [
          currentRoomId,
          unic_id,
          e.url,
          lastStreamTitle,
        ]);
      }
      return;
    }

    if (pendingShareCard) {
      var card = pendingShareCard;
      pendingShareCard = null;
      autoCreateRoomFromPending(card, e.url);
    }
  }
  if (typeof Lampa.PlayerVideo !== "undefined" && Lampa.PlayerVideo.listener) {
    Lampa.PlayerVideo.listener.follow("start", onPlayerStart);
  }
  if (typeof Lampa.Player !== "undefined" && Lampa.Player.listener) {
    Lampa.Player.listener.follow("start", onPlayerStart);
    Lampa.Player.listener.follow("destroy", function () {
      var vid = getVideo();
      if (vid) {
        vid._lp_hooked = false;
        clearRateAdjust(vid);
      }
      if (episodeSwitchPending) return;
      if (inRoom) sendWs("watchtogether_leave", [currentRoomId, unic_id]);
      leaveRoomLocal();
    });
  }

  if (
    typeof Lampa.PlayerPlaylist !== "undefined" &&
    Lampa.PlayerPlaylist.listener &&
    Lampa.PlayerPlaylist.listener.follow
  ) {
    Lampa.PlayerPlaylist.listener.follow("select", function () {
      if (inRoom && iAmHost()) {
        episodeSwitchPending = true;
      }
    });
  }

  window.addEventListener("beforeunload", function () {
    if (inRoom) sendWs("watchtogether_leave", [currentRoomId, unic_id]);
  });

  function createRoomFromPlayer() {
    if (inRoom) {
      Lampa.Noty.show(
        T.already_in_room(currentRoomName || currentRoomId || ""),
      );
      return;
    }
    if (!lastStreamUrl) {
      Lampa.Noty.show(T.need_url);
      return;
    }
    try {
      if (typeof Lampa.Controller !== "undefined" && Lampa.Controller.toggle) {
        var isMobile =
          Lampa.Platform &&
          Lampa.Platform.screen &&
          Lampa.Platform.screen("mobile");
        Lampa.Controller.toggle(isMobile ? "player" : "player_panel");
      }
    } catch (err) {}
    autoCreateRoomFromPending(lastViewedCard || {}, lastStreamUrl);
  }

  function copyWebLink() {
    if (!inRoom || !currentRoomId) return;
    var backendHost = localhost
      .replace("https://", "")
      .replace("http://", "")
      .replace(/\/$/, "");
    var protocol = window.location.protocol;
    if (localhost.indexOf("https://") === 0) protocol = "https:";
    else if (localhost.indexOf("http://") === 0) protocol = "http:";
    var link =
      protocol + "//" + backendHost + "/watch_together/" + currentRoomId;

    if (Lampa.Utils && Lampa.Utils.copyTextToClipboard) {
      Lampa.Utils.copyTextToClipboard(link, function () {
        Lampa.Noty.show(T.link_copied);
      });
    } else {
      Lampa.Input.edit(
        {
          title: T.copy_link,
          value: link,
          free: true,
          nosave: true,
        },
        function () {},
      );
    }
  }

  if (
    typeof Lampa.Select !== "undefined" &&
    Lampa.Select.listener &&
    Lampa.Select.listener.follow
  ) {
    Lampa.Select.listener.follow("preshow", function (e) {
      if (!e || !e.active || !Array.isArray(e.active.items)) return;
      var items = e.active.items;
      var hasPlayer = false,
        hasFileMenu = false,
        isPlayerSettings = false,
        alreadyInjected = false;
      for (var i = 0; i < items.length; i++) {
        var it = items[i];
        if (!it) continue;
        if (it.player) hasPlayer = true;
        if (it.mark || it.timeclear || it.clearmark) hasFileMenu = true;
        if (
          it.method === "size" ||
          it.method === "speed" ||
          it.method === "subs" ||
          it.method === "share" ||
          it.method === "segments"
        )
          isPlayerSettings = true;
        if (it.watchtogether_inject || it.watchtogether_inject_player)
          alreadyInjected = true;
      }
      if (alreadyInjected) return;

      if (hasPlayer && hasFileMenu) {
        if (inRoom) {
          items.push({
            title: T.menu_title + " (" + T.copy_link + ")",
            watchtogether_inject: true,
            is_copy: true,
          });
        } else {
          items.push({
            title: T.full_card_btn,
            player: "lampa",
            watchtogether_inject: true,
            is_copy: false,
          });
        }

        var originalOnSelect = e.active.onSelect;
        e.active.onSelect = function (a) {
          if (a && a.watchtogether_inject) {
            if (a.is_copy) {
              copyWebLink();
            } else {
              pendingShareCard = lastViewedCard || {};
              Lampa.Noty.show(T.pending_share);
            }
          }
          if (originalOnSelect) originalOnSelect(a);
        };
        return;
      }

      if (isPlayerSettings) {
        if (inRoom) {
          items.push({
            title: T.menu_title,
            subtitle: T.copy_link,
            method: "watchtogether_copy",
            watchtogether_inject_player: true,
            is_copy: true,
          });
        } else {
          items.push({
            title: T.full_card_btn,
            subtitle: T.player_create_descr,
            method: "watchtogether_create",
            watchtogether_inject_player: true,
            is_copy: false,
          });
        }

        var originalOnSelectP = e.active.onSelect;
        e.active.onSelect = function (a) {
          if (a && a.watchtogether_inject_player) {
            if (a.is_copy) {
              copyWebLink();
            } else {
              createRoomFromPlayer();
            }
            return;
          }
          if (originalOnSelectP) originalOnSelectP(a);
        };
      }
    });
  }

  registerSettings();
  connectWs();
})();
