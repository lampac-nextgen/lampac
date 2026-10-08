/**
 * Three languages, no libraries. Auto-detection picks between Ukrainian and English
 * only: Russian is in the list but is never selected without an explicit choice.
 *
 * A dictionary value is either a string or an object of plural forms
 * (one/few/many/other) resolved through Intl.PluralRules.
 */

export const LANGS = [
  { code: "uk", name: "Українська" },
  { code: "en", name: "English" },
  { code: "ru", name: "Русский" },
];

const DICT = {
  uk: {
    tagline: "Спільний перегляд із Lampa",
    lang_label: "Мова",

    join_heading: "Увійти за кодом",
    code_ph: "Код кімнати",
    pwd_ph: "Пароль",
    join_btn: "Увійти",

    rooms_heading: "Відкриті кімнати",
    refresh_btn: "Оновити",
    searching: "Шукаємо…",
    empty_title: "Поки жодної кімнати",
    empty_hint:
      "Кімната з’явиться тут, щойно хтось у Lampa створить її з увімкненою публікацією. Якщо код уже є — введіть його вище.",

    create_heading: "Створити кімнату",
    url_ph: "Посилання на потік (.m3u8 або .mp4)",
    title_ph: "Назва",
    create_btn: "Створити",
    url_hint: "Потрібне https-посилання, і сервер має дозволяти CORS.",
    cancel_btn: "Скасувати",
    done_btn: "Готово",

    settings_heading: "Налаштування",
    name_ph: "Ваше ім’я",
    regenerate_name: "Згенерувати інше ім’я",
    relay_label: "Реле",
    publish_label: "Показувати мою кімнату у списку",

    leave_btn: "Вийти",
    host_badge: "ви хост",
    switch_ph: "Нове посилання на потік",
    switch_btn: "Перемкнути всім",
    status_connecting: "З’єднання…",
    status_online: "На зв’язку",
    status_offline: "Зв’язок втрачено",
    viewers: {
      one: "{n} глядач",
      few: "{n} глядачі",
      many: "{n} глядачів",
      other: "{n} глядача",
    },

    room_created: "Кімнату створено · код {id}",
    join_ok: "Ви в кімнаті {name}",
    joined: "{name} приєднався",
    left_room: "{name} вийшов",
    host_changed: "Новий хост: {name}",
    you_are_host: "Тепер ви хост",
    notice_hold: "Пауза — чекаємо, поки всі завантажать буфер",
    notice_go: "Усі готові — продовжуємо",
    act_resumed: "{name} продовжив",
    act_paused: "{name} поставив на паузу",
    act_seeked: "{name} перемотав",
    someone: "учасник",

    connecting: "Підключення…",
    password_prompt: "Пароль кімнати {name}",
    need_code: "Введіть код кімнати",
    need_url: "Потрібне посилання на потік",
    need_pwd_public:
      "Кімната у списку потребує пароля — задайте його або вимкніть публікацію",
    already_in_room: "Ви вже в кімнаті {name}",
    create_failed: "Не вдалося створити кімнату",
    join_failed: "Кімнату не знайдено або пароль невірний",
    kicked: "Ви підключилися з іншого місця",
    no_stream: "У кімнаті немає потоку",
    relay_error: "Реле: {text}",
    autoplay_blocked: "Браузер заблокував автозапуск — натисніть play",
    mixed_content:
      "Потік віддається по http, а сторінка по https — браузер це блокує",
    cors_error:
      "Потік не завантажується: схоже, сервер не віддає Access-Control-Allow-Origin",
    playback_error: "Помилка відтворення: {details}",
    media_error:
      "Браузер не зміг відкрити потік (формат, CORS або mixed content)",
  },

  en: {
    tagline: "Watch together with Lampa",
    lang_label: "Language",

    join_heading: "Join with a code",
    code_ph: "Room code",
    pwd_ph: "Password",
    join_btn: "Join",

    rooms_heading: "Open rooms",
    refresh_btn: "Refresh",
    searching: "Searching…",
    empty_title: "No rooms right now",
    empty_hint:
      "A room appears here as soon as someone in Lampa creates one with publishing enabled. If you already have a code, enter it above.",

    create_heading: "Create a room",
    url_ph: "Stream link (.m3u8 or .mp4)",
    title_ph: "Title",
    create_btn: "Create",
    url_hint: "Needs an https link from a server that allows CORS.",
    cancel_btn: "Cancel",
    done_btn: "Done",

    settings_heading: "Settings",
    name_ph: "Your name",
    regenerate_name: "Generate another name",
    relay_label: "Relay",
    publish_label: "List my room publicly",

    leave_btn: "Leave",
    host_badge: "you host",
    switch_ph: "New stream link",
    switch_btn: "Switch for everyone",
    status_connecting: "Connecting…",
    status_online: "Connected",
    status_offline: "Connection lost",
    viewers: { one: "{n} viewer", other: "{n} viewers" },

    room_created: "Room created · code {id}",
    join_ok: "You are in {name}",
    joined: "{name} joined",
    left_room: "{name} left",
    host_changed: "New host: {name}",
    you_are_host: "You are the host now",
    notice_hold: "Paused — waiting for everyone to buffer",
    notice_go: "Everyone is ready — resuming",
    act_resumed: "{name} resumed",
    act_paused: "{name} paused",
    act_seeked: "{name} skipped",
    someone: "someone",

    connecting: "Connecting…",
    password_prompt: "Password for {name}",
    need_code: "Enter a room code",
    need_url: "A stream link is required",
    need_pwd_public:
      "A publicly listed room needs a password — set one or turn off publishing",
    already_in_room: "You are already in {name}",
    create_failed: "Could not create the room",
    join_failed: "Room not found, or the password is wrong",
    kicked: "You connected from somewhere else",
    no_stream: "The room has no stream",
    relay_error: "Relay: {text}",
    autoplay_blocked: "The browser blocked autoplay — press play",
    mixed_content:
      "The stream is served over http while this page is https — the browser blocks that",
    cors_error:
      "The stream will not load: the server likely omits Access-Control-Allow-Origin",
    playback_error: "Playback error: {details}",
    media_error:
      "The browser could not open the stream (format, CORS or mixed content)",
  },

  ru: {
    tagline: "Совместный просмотр с Lampa",
    lang_label: "Язык",

    join_heading: "Войти по коду",
    code_ph: "Код комнаты",
    pwd_ph: "Пароль",
    join_btn: "Войти",

    rooms_heading: "Открытые комнаты",
    refresh_btn: "Обновить",
    searching: "Ищем…",
    empty_title: "Пока ни одной комнаты",
    empty_hint:
      "Комната появится здесь, как только кто-то в Lampa создаст её с включённой публикацией. Если код уже есть — введите его выше.",

    create_heading: "Создать комнату",
    url_ph: "Ссылка на поток (.m3u8 или .mp4)",
    title_ph: "Название",
    create_btn: "Создать",
    url_hint: "Нужна https-ссылка, и сервер должен разрешать CORS.",
    cancel_btn: "Отмена",
    done_btn: "Готово",

    settings_heading: "Настройки",
    name_ph: "Ваше имя",
    regenerate_name: "Сгенерировать другое имя",
    relay_label: "Реле",
    publish_label: "Показывать мою комнату в списке",

    leave_btn: "Выйти",
    host_badge: "вы хост",
    switch_ph: "Новая ссылка на поток",
    switch_btn: "Переключить всем",
    status_connecting: "Соединение…",
    status_online: "На связи",
    status_offline: "Связь потеряна",
    viewers: {
      one: "{n} зритель",
      few: "{n} зрителя",
      many: "{n} зрителей",
      other: "{n} зрителя",
    },

    room_created: "Комната создана · код {id}",
    join_ok: "Вы в комнате {name}",
    joined: "{name} присоединился",
    left_room: "{name} вышел",
    host_changed: "Новый хост: {name}",
    you_are_host: "Теперь вы хост",
    notice_hold: "Пауза — ждём, пока все загрузят буфер",
    notice_go: "Все готовы — продолжаем",
    act_resumed: "{name} продолжил",
    act_paused: "{name} поставил на паузу",
    act_seeked: "{name} перемотал",
    someone: "участник",

    connecting: "Подключение…",
    password_prompt: "Пароль комнаты {name}",
    need_code: "Введите код комнаты",
    need_url: "Нужна ссылка на поток",
    need_pwd_public:
      "Комната в списке требует пароля — задайте его или отключите публикацию",
    already_in_room: "Вы уже в комнате {name}",
    create_failed: "Не удалось создать комнату",
    join_failed: "Комната не найдена или пароль неверный",
    kicked: "Вы подключились из другого места",
    no_stream: "В комнате нет потока",
    relay_error: "Реле: {text}",
    autoplay_blocked: "Браузер заблокировал автозапуск — нажмите play",
    mixed_content:
      "Поток отдаётся по http, а страница по https — браузер это блокирует",
    cors_error:
      "Поток не загружается: похоже, сервер не отдаёт Access-Control-Allow-Origin",
    playback_error: "Ошибка воспроизведения: {details}",
    media_error:
      "Браузер не смог открыть поток (формат, CORS или mixed content)",
  },
};

export const DICTS = DICT; // exposed so tests can check the dictionaries are complete

const STORE_KEY = "lparty_lang";
const listeners = new Set();
let current = "uk";

/** Russian is deliberately not reachable here: it is a manual choice only. */
export function detectLang() {
  for (const tag of navigator.languages || [navigator.language || ""]) {
    const code = String(tag).toLowerCase().split("-")[0];
    if (code === "uk") return "uk";
    if (code === "en") return "en";
  }
  return "uk";
}

export function getLang() {
  return current;
}

export function setLang(code) {
  if (!DICT[code]) return;
  current = code;
  try {
    localStorage.setItem(STORE_KEY, code);
  } catch (err) {}
  document.documentElement.lang = code;
  listeners.forEach((fn) => fn(code));
}

export function initLang() {
  let saved = null;
  try {
    saved = localStorage.getItem(STORE_KEY);
  } catch (err) {}
  current = DICT[saved] ? saved : detectLang();
  document.documentElement.lang = current;
  return current;
}

export function onLangChange(fn) {
  listeners.add(fn);
}

export function t(key, params) {
  let value = DICT[current][key];
  if (value === undefined) return key; // visible immediately; silence would be worse

  if (typeof value === "object") {
    const n = Number(params && params.n);
    const form = new Intl.PluralRules(current).select(n);
    value = value[form] ?? value.other;
  }

  return params
    ? value.replace(/\{(\w+)\}/g, (m, name) =>
        params[name] === undefined ? m : params[name],
      )
    : value;
}

/**
 * Repaints static nodes. data-i18n sets textContent, data-i18n-ph sets the
 * placeholder, data-i18n-label sets aria-label.
 */
export function applyTranslations(root = document) {
  root.querySelectorAll("[data-i18n]").forEach((el) => {
    el.textContent = t(el.dataset.i18n);
  });
  root.querySelectorAll("[data-i18n-ph]").forEach((el) => {
    el.placeholder = t(el.dataset.i18nPh);
  });
  root.querySelectorAll("[data-i18n-label]").forEach((el) => {
    el.setAttribute("aria-label", t(el.dataset.i18nLabel));
  });
}
