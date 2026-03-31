"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
var __generator = (this && this.__generator) || function (thisArg, body) {
    var _ = { label: 0, sent: function() { if (t[0] & 1) throw t[1]; return t[1]; }, trys: [], ops: [] }, f, y, t, g = Object.create((typeof Iterator === "function" ? Iterator : Object).prototype);
    return g.next = verb(0), g["throw"] = verb(1), g["return"] = verb(2), typeof Symbol === "function" && (g[Symbol.iterator] = function() { return this; }), g;
    function verb(n) { return function (v) { return step([n, v]); }; }
    function step(op) {
        if (f) throw new TypeError("Generator is already executing.");
        while (g && (g = 0, op[0] && (_ = 0)), _) try {
            if (f = 1, y && (t = op[0] & 2 ? y["return"] : op[0] ? y["throw"] || ((t = y["return"]) && t.call(y), 0) : y.next) && !(t = t.call(y, op[1])).done) return t;
            if (y = 0, t) op = [op[0] & 2, t.value];
            switch (op[0]) {
                case 0: case 1: t = op; break;
                case 4: _.label++; return { value: op[1], done: false };
                case 5: _.label++; y = op[1]; op = [0]; continue;
                case 7: op = _.ops.pop(); _.trys.pop(); continue;
                default:
                    if (!(t = _.trys, t = t.length > 0 && t[t.length - 1]) && (op[0] === 6 || op[0] === 2)) { _ = 0; continue; }
                    if (op[0] === 3 && (!t || (op[1] > t[0] && op[1] < t[3]))) { _.label = op[1]; break; }
                    if (op[0] === 6 && _.label < t[1]) { _.label = t[1]; t = op; break; }
                    if (t && _.label < t[2]) { _.label = t[2]; _.ops.push(op); break; }
                    if (t[2]) _.ops.pop();
                    _.trys.pop(); continue;
            }
            op = body.call(thisArg, _);
        } catch (e) { op = [6, e]; y = 0; } finally { f = t = 0; }
        if (op[0] & 5) throw op[1]; return { value: op[0] ? op[1] : void 0, done: true };
    }
};
var pages = {};
var rootCustomtItems = null;
var rootDefaultItems = null;
fetch('/admin/init/default')
    .then(function (res) { return res.json(); })
    .then(function (ob) {
    rootDefaultItems = ob;
});
function navigate(page) {
    var content = document.getElementById('content');
    if (!content)
        return;
    if (page === 'users') {
        renderUsersPage('content');
    }
    else if (page === 'online') {
        renderOnlinePage('content');
    }
    else if (page === 'proxy') {
        renderProxiesPage('content');
    }
    else if (page === 'other') {
        renderOtherPage('content');
    }
    else {
        renderEditorPage('content');
    }
}
function saveCustomtItems() {
    var _this = this;
    var sendData = new FormData();
    sendData.append('json', JSON.stringify(rootCustomtItems, null, 2));
    fetch('/admin/init/save', {
        method: 'POST',
        body: sendData
    })
        .then(function (response) { return __awaiter(_this, void 0, void 0, function () {
        var data;
        return __generator(this, function (_a) {
            switch (_a.label) {
                case 0:
                    if (!response.ok) return [3 /*break*/, 2];
                    return [4 /*yield*/, response.json()];
                case 1:
                    data = _a.sent();
                    if (data && data.success === true) {
                        showToast('Данные успешно сохранены');
                    }
                    else {
                        alert('Ошибка: сервер не подтвердил сохранение');
                    }
                    return [3 /*break*/, 3];
                case 2:
                    alert('Ошибка при сохранении данных');
                    _a.label = 3;
                case 3: return [2 /*return*/];
            }
        });
    }); })
        .catch(function () {
        alert('Ошибка при отправке запроса');
    });
}
function escapeHtmlAttr(str) {
    if (str === null || str === undefined)
        return '';
    return String(str)
        .replace(/&/g, '&amp;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
}
function showToast(message) {
    // Создаём контейнер для тостов, если его нет
    var toastContainer = document.getElementById('toast-container');
    if (!toastContainer) {
        toastContainer = document.createElement('div');
        toastContainer.id = 'toast-container';
        toastContainer.style.position = 'fixed';
        toastContainer.style.bottom = '1rem';
        toastContainer.style.right = '1rem';
        toastContainer.style.zIndex = '1080';
        document.body.appendChild(toastContainer);
    }
    // Всегда зелёный цвет
    var bgClass = 'bg-success';
    var textClass = 'text-white';
    var toastId = 'toast-' + Date.now() + Math.floor(Math.random() * 1000);
    toastContainer.insertAdjacentHTML('beforeend', "\n        <div id=\"".concat(toastId, "\" class=\"toast align-items-center ").concat(bgClass, " ").concat(textClass, "\" role=\"alert\" aria-live=\"assertive\" aria-atomic=\"true\" data-bs-delay=\"3000\" style=\"min-width:220px;\">\n            <div class=\"d-flex\">\n                <div class=\"toast-body\">").concat(message, "</div>\n                <button type=\"button\" class=\"btn-close btn-close-white me-2 m-auto\" data-bs-dismiss=\"toast\" aria-label=\"\u0417\u0430\u043A\u0440\u044B\u0442\u044C\"></button>\n            </div>\n        </div>\n    "));
    // @ts-ignore
    var toastEl = document.getElementById(toastId);
    // @ts-ignore
    var toast = new bootstrap.Toast(toastEl);
    toast.show();
    // Удаляем DOM после скрытия
    if (toastEl) {
        toastEl.addEventListener('hidden.bs.toast', function () {
            toastEl.remove();
        });
    }
}
// Инициализация по умолчанию
window.onload = function () { return navigate('home'); };
var __spreadArray = (this && this.__spreadArray) || function (to, from, pack) {
    if (pack || arguments.length === 2) for (var i = 0, l = from.length, ar; i < l; i++) {
        if (ar || !(i in from)) {
            if (!ar) ar = Array.prototype.slice.call(from, 0, i);
            ar[i] = from[i];
        }
    }
    return to.concat(ar || Array.prototype.slice.call(from));
};
function formatDate(dateStr) {
    if (!dateStr)
        return '';
    var date = new Date(dateStr);
    if (isNaN(date.getTime()))
        return '';
    var day = String(date.getDate()).padStart(2, '0');
    var month = String(date.getMonth() + 1).padStart(2, '0');
    var year = date.getFullYear();
    return "".concat(day, ".").concat(month, ".").concat(year);
}
function getUserModalHtml(modalId, title, user, nextMonthDate, showDelete, showBanFields // новый параметр
) {
    if (showDelete === void 0) { showDelete = false; }
    if (showBanFields === void 0) { showBanFields = true; }
    return "\n    <div class=\"modal fade\" id=\"".concat(modalId, "\" tabindex=\"-1\" aria-labelledby=\"").concat(modalId, "Label\" aria-hidden=\"true\">\n      <div class=\"modal-dialog\">\n        <div class=\"modal-content\">\n          <div class=\"modal-header\">\n            <h5 class=\"modal-title\" id=\"").concat(modalId, "Label\">").concat(title, "</h5>\n            <button type=\"button\" class=\"btn-close\" data-bs-dismiss=\"modal\" aria-label=\"\u0417\u0430\u043A\u0440\u044B\u0442\u044C\"></button>\n          </div>\n          <div class=\"modal-body\">\n            <form id=\"").concat(modalId, "-form\">\n              <div class=\"mb-3\">\n                <label for=\"").concat(modalId, "-user-id\" class=\"form-label\">ID</label>\n                <input type=\"text\" class=\"form-control\" id=\"").concat(modalId, "-user-id\" value=\"").concat(user ? (user.id || '') : '', "\" placeholder=\"unic_id, token, email, \u043F\u0430\u0440\u043E\u043B\u044C\" required>\n              </div>\n              <div class=\"mb-3\">\n                <label for=\"").concat(modalId, "-user-ids\" class=\"form-label\">IDs (\u0447\u0435\u0440\u0435\u0437 \u0437\u0430\u043F\u044F\u0442\u0443\u044E)</label>\n                <input type=\"text\" class=\"form-control\" id=\"").concat(modalId, "-user-ids\" value=\"").concat(user ? (user.ids ? user.ids.join(', ') : '') : '', "\" placeholder=\"uid-1, uid-2, token\">\n              </div>\n              <div class=\"mb-3\">\n                <label for=\"").concat(modalId, "-user-expires\" class=\"form-label\">\u0414\u043E\u0441\u0442\u0443\u043F \u0434\u043E (\u0413\u0413\u0413\u0413-\u041C\u041C-\u0414\u0414)</label>\n                <input type=\"date\" class=\"form-control\" id=\"").concat(modalId, "-user-expires\" value=\"").concat(user ? (user.expires ? user.expires.substring(0, 10) : '') : (nextMonthDate || ''), "\" required>\n              </div>\n              <div class=\"mb-3\">\n                <label for=\"").concat(modalId, "-user-group\" class=\"form-label\">\u0413\u0440\u0443\u043F\u043F\u0430 \u0434\u043E\u0441\u0442\u0443\u043F\u0430</label>\n                <input type=\"number\" class=\"form-control\" id=\"").concat(modalId, "-user-group\" value=\"").concat(user ? user.group : 1, "\" required>\n              </div>\n              <div class=\"mb-3\">\n                <label for=\"").concat(modalId, "-user-comment\" class=\"form-label\">\u041A\u043E\u043C\u043C\u0435\u043D\u0442\u0430\u0440\u0438\u0439 \u0434\u043B\u044F \u0441\u0435\u0431\u044F</label>\n                <input type=\"text\" class=\"form-control\" id=\"").concat(modalId, "-user-comment\" value=\"").concat(user ? (user.comment || '') : '', "\" placeholder=\"\u043A\u043E\u043D\u0442\u0430\u043A\u0442\u044B \u043F\u043E\u043B\u044C\u0437\u043E\u0432\u0430\u0442\u0435\u043B\u044F, etc\">\n              </div>\n              ").concat(showBanFields ? "\n              <div class=\"mb-3 mt-5\">\n                <div class=\"form-check\">\n                  <input class=\"form-check-input\" type=\"checkbox\" id=\"".concat(modalId, "-user-ban\" ").concat(user && user.ban ? 'checked' : '', ">\n                  <label class=\"form-check-label\" for=\"").concat(modalId, "-user-ban\">\u0417\u0430\u0431\u043B\u043E\u043A\u0438\u0440\u043E\u0432\u0430\u0442\u044C \u0434\u043E\u0441\u0442\u0443\u043F</label>\n                </div>\n              </div>\n              <div class=\"mb-3\">\n                <input type=\"text\" class=\"form-control\" id=\"").concat(modalId, "-user-ban-msg\" value=\"").concat(user ? (user.ban_msg || '') : '', "\" placeholder=\"\u0441\u043E\u043E\u0431\u0449\u0435\u043D\u0438\u0435 \u043A\u043E\u0442\u043E\u0440\u043E\u0435 \u0432\u0438\u0434\u0438\u0442 \u043F\u043E\u043B\u044C\u0437\u043E\u0432\u0430\u0442\u0435\u043B\u044C\">\n              </div>\n              ") : '', "\n            </form>\n            ").concat(showDelete ? "<button type=\"button\" class=\"btn btn-warning\" id=\"".concat(modalId, "-params-btn\">params</button>") : '<span></span>', "\n          </div>\n          <div class=\"modal-footer d-flex justify-content-between\">\n            ").concat(showDelete ? "<button type=\"button\" class=\"btn btn-danger\" id=\"".concat(modalId, "-delete-btn\">\u0423\u0434\u0430\u043B\u0438\u0442\u044C</button>") : '<span></span>', "\n            <div>\n              <button type=\"button\" class=\"btn btn-secondary me-2\" data-bs-dismiss=\"modal\">\u0417\u0430\u043A\u0440\u044B\u0442\u044C</button>\n              <button type=\"button\" class=\"btn btn-primary\" id=\"").concat(modalId, "-save-btn\">\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C</button>\n            </div>\n          </div>\n        </div>\n      </div>\n    </div>\n    ");
}
function renderUsersPage(containerId) {
    var container = document.getElementById(containerId);
    if (!container)
        return;
    // Получаем дату +1 месяц от текущей
    function getNextMonthDate() {
        var now = new Date();
        var year = now.getFullYear();
        var month = now.getMonth() + 2;
        var nextYear = year;
        if (month > 12) {
            month = 1;
            nextYear++;
        }
        var day = String(now.getDate()).padStart(2, '0');
        return "".concat(nextYear, "-").concat(String(month).padStart(2, '0'), "-").concat(day);
    }
    var nextMonthDate = getNextMonthDate();
    // Модальное окно для добавления пользователя (без полей ban и ban_msg)
    var addModalHtml = getUserModalHtml('addUserModal', 'Добавить пользователя', undefined, nextMonthDate, false, false);
    container.innerHTML = "\n        ".concat(addModalHtml, "\n        <div id=\"edit-user-modal-container\"></div>\n        <div class=\"d-flex justify-content-between align-items-center mb-3\">\n            <h1 class=\"mb-0\">\u041F\u043E\u043B\u044C\u0437\u043E\u0432\u0430\u0442\u0435\u043B\u0438</h1>\n            <div>\n                <button type=\"button\" class=\"btn btn-success\" id=\"btn-add-user\">\u0414\u043E\u0431\u0430\u0432\u0438\u0442\u044C \u043F\u043E\u043B\u044C\u0437\u043E\u0432\u0430\u0442\u0435\u043B\u044F</button>\n                <button type=\"button\" class=\"btn btn-light ms-2\" id=\"btn-users-settings\" title=\"\u041D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438\" style=\"vertical-align: middle;\">\n                    <svg xmlns=\"http://www.w3.org/2000/svg\" width=\"18\" height=\"18\" fill=\"currentColor\" class=\"bi bi-gear\" viewBox=\"0 0 16 16\">\n                        <path d=\"M8 4.754a3.246 3.246 0 1 0 0 6.492 3.246 3.246 0 0 0 0-6.492zM5.754 8a2.246 2.246 0 1 1 4.492 0 2.246 2.246 0 0 1-4.492 0z\"/>\n                        <path d=\"M9.796 1.343c-.527-1.79-3.065-1.79-3.592 0l-.094.319a.873.873 0 0 1-1.255.52l-.292-.16c-1.64-.892-3.433.902-2.54 2.541l.159.292a.873.873 0 0 1-.52 1.255l-.319.094c-1.79.527-1.79 3.065 0 3.592l.319.094a.873.873 0 0 1 .52 1.255l-.16.292c-.892 1.64.901 3.434 2.541 2.54l.292-.159a.873.873 0 0 1 1.255.52l.094.319c.527 1.79 3.065 1.79 3.592 0l.094-.319a.873.873 0 0 1 1.255-.52l.292.16c1.64.893 3.434-.902 2.54-2.541l-.159-.292a.873.873 0 0 1 .52-1.255l.319-.094c1.79-.527 1.79-3.065 0-3.592l-.319-.094a.873.873 0 0 1-.52-1.255l.16-.292c.893-1.64-.902-3.433-2.541-2.54l-.292.159a.873.873 0 0 1-1.255-.52l-.094-.319zm-2.633.283c.246-.835 1.428-.835 1.674 0l.094.319a1.873 1.873 0 0 0 2.693 1.115l.291-.16c.764-.415 1.6.42 1.184 1.185l-.159.292a1.873 1.873 0 0 0 1.116 2.692l.318.094c.835.246.835 1.428 0 1.674l-.319.094a1.873 1.873 0 0 0-1.115 2.693l.16.291c.415.764-.42 1.6-1.185 1.184l-.291-.159a1.873 1.873 0 0 0-2.693 1.116l-.094.318c-.246.835-1.428.835-1.674 0l-.094-.319a1.873 1.873 0 0 0-2.692-1.115l-.292.16c-.764.415-1.6-.42-1.184-1.185l.159-.291A1.873 1.873 0 0 0 1.945 8.93l-.319-.094c-.835-.246-.835-1.428 0-1.674l.319-.094A1.873 1.873 0 0 0 3.06 4.377l-.16-.292c-.415-.764.42-1.6 1.185-1.184l.292.159a1.873 1.873 0 0 0 2.692-1.115l.094-.319z\"/>\n                    </svg>\n                </button>\n                <button type=\"button\" class=\"btn btn-primary\" id=\"btn-save-users\" style=\"display: none;\">\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C</button>\n            </div>\n        </div>\n        <div id=\"users-list\" class=\"row g-3\"></div>\n    ");
    loadAndRenderUsers('users-list');
    setTimeout(function () {
        // Кнопка "Сохранить"
        var btnSave = document.getElementById('btn-save-users');
        if (btnSave) {
            btnSave.onclick = function () {
                if (rootCustomtItems) {
                    saveCustomtItems();
                }
                else {
                    alert('Данные не загружены');
                }
            };
        }
        // Кнопка "Добавить"
        var btnAdd = document.getElementById('btn-add-user');
        if (btnAdd) {
            btnAdd.onclick = function () {
                // @ts-ignore
                var modal = new bootstrap.Modal(document.getElementById('addUserModal'));
                modal.show();
            };
        }
        // Кнопка "Сохранить" в модальном окне добавления
        var saveUserBtn = document.getElementById('addUserModal-save-btn');
        if (saveUserBtn) {
            saveUserBtn.onclick = function () {
                var id = document.getElementById('addUserModal-user-id').value.trim();
                var idsRaw = document.getElementById('addUserModal-user-ids').value.trim();
                var expires = document.getElementById('addUserModal-user-expires').value;
                var group = parseInt(document.getElementById('addUserModal-user-group').value, 10);
                var comment = document.getElementById('addUserModal-user-comment').value.trim();
                if (!id || !expires || isNaN(group)) {
                    alert('Пожалуйста, заполните все обязательные поля');
                    return;
                }
                var ids = idsRaw ? idsRaw.split(',').map(function (s) { return s.trim(); }).filter(Boolean) : [];
                var newUser = {
                    id: id,
                    ids: ids,
                    expires: expires + "T00:00:00",
                    group: group,
                    comment: comment
                };
                if (window.invc)
                    window.invc.newUser(newUser);
                if (rootCustomtItems && rootCustomtItems["accsdb"] && Array.isArray(rootCustomtItems["accsdb"]["users"])) {
                    rootCustomtItems["accsdb"]["users"].push(newUser);
                    // Перерисовать таблицу
                    var usersList = document.getElementById('users-list');
                    if (usersList) {
                        usersList.innerHTML = renderUsers(rootCustomtItems["accsdb"]["users"]);
                        attachEditHandlers(rootCustomtItems["accsdb"]["users"]);
                    }
                }
                // Закрыть модальное окно
                // @ts-ignore
                var modal = bootstrap.Modal.getInstance(document.getElementById('addUserModal'));
                if (modal)
                    modal.hide();
                // Автоматически кликнуть по кнопке "Сохранить"
                saveCustomtItems();
            };
        }
        // После первой загрузки пользователей навесить обработчики
        if (rootCustomtItems && rootCustomtItems["accsdb"] && Array.isArray(rootCustomtItems["accsdb"]["users"])) {
            attachEditHandlers(rootCustomtItems["accsdb"]["users"]);
        }
        // Модальное окно для настроек accsdb
        if (!document.getElementById('accsdb-settings-modal')) {
            var modalHtml = "\n                <div class=\"modal fade\" id=\"accsdb-settings-modal\" tabindex=\"-1\" aria-labelledby=\"accsdbSettingsLabel\" aria-hidden=\"true\">\n                  <div class=\"modal-dialog modal-lg\">\n                    <div class=\"modal-content\">\n                      <div class=\"modal-header\">\n                        <h5 class=\"modal-title\" id=\"accsdbSettingsLabel\">\u041D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438 accsdb</h5>\n                        <button type=\"button\" class=\"btn-close\" data-bs-dismiss=\"modal\" aria-label=\"\u0417\u0430\u043A\u0440\u044B\u0442\u044C\"></button>\n                      </div>\n                      <div class=\"modal-body\">\n                        <form id=\"accsdb-settings-form\">\n                          <div class=\"row g-2\">\n                            <div class=\"col-6 mb-2\">\n                              <div class=\"form-check\">\n                                <input class=\"form-check-input\" type=\"checkbox\" id=\"accsdb-enable\">\n                                <label class=\"form-check-label\" for=\"accsdb-enable\">\u0412\u043A\u043B\u044E\u0447\u0438\u0442\u044C \u0430\u0432\u0442\u043E\u0440\u0438\u0437\u0430\u0446\u0438\u044E</label>\n                              </div>\n                            </div>\n                            <div class=\"col-6 mb-2\">\n                              <label class=\"form-label\" for=\"accsdb-whitepattern\">whitepattern</label>\n                              <input type=\"text\" class=\"form-control\" id=\"accsdb-whitepattern\">\n                            </div>\n                            <div class=\"col-6 mb-2 mb-5\">\n                              <label class=\"form-label\" for=\"accsdb-premium_pattern\">premium_pattern</label>\n                              <input type=\"text\" class=\"form-control\" id=\"accsdb-premium_pattern\">\n                            </div>\n                            <div class=\"col-6 mb-2\">\n                              <label class=\"form-label\" for=\"accsdb-domainId_pattern\">domainId_pattern</label>\n                              <input type=\"text\" class=\"form-control\" id=\"accsdb-domainId_pattern\">\n                            </div>\n                            <div class=\"col-6 mb-2\">\n                              <label class=\"form-label\" for=\"accsdb-maxip_hour\">\u041B\u0438\u043C\u0438\u0442 ip \u0432 \u0447\u0430\u0441</label>\n                              <input type=\"number\" class=\"form-control\" id=\"accsdb-maxip_hour\">\n                            </div>\n                            <div class=\"col-6 mb-2\">\n                              <label class=\"form-label\" for=\"accsdb-maxrequest_hour\">\u041B\u0438\u043C\u0438\u0442 \u0437\u0430\u043F\u0440\u043E\u0441\u043E\u0432 \u0432 \u0447\u0430\u0441</label>\n                              <input type=\"number\" class=\"form-control\" id=\"accsdb-maxrequest_hour\">\n                            </div>\n                            <div class=\"col-12 mb-2\">\n                              <label class=\"form-label\" for=\"accsdb-maxlock_day\">\u041B\u0438\u043C\u0438\u0442 \u0431\u043B\u043E\u043A\u0438\u0440\u043E\u0432\u043E\u043A \u0432 \u0441\u0443\u0442\u043A\u0438 (maxlock_day)</label>\n                              <input type=\"number\" class=\"form-control\" id=\"accsdb-maxlock_day\">\n                            </div>\n                            <div class=\"col-12 mb-2\">\n                              <label class=\"form-label\" for=\"accsdb-blocked_hour\">\u041D\u0430 \u0441\u043A\u043E\u043B\u044C\u043A\u043E \u0447\u0430\u0441\u043E\u0432 \u0431\u043B\u043E\u043A\u0438\u0440\u043E\u0432\u0430\u0442\u044C \u043F\u0440\u0438 \u0434\u043E\u0441\u0442\u0438\u0436\u0435\u043D\u0438\u0438 \u043B\u0438\u043C\u0438\u0442\u0430 maxlock_day</label>\n                              <input type=\"number\" class=\"form-control\" id=\"accsdb-blocked_hour\">\n                            </div>\n                            <div class=\"col-12 mb-2 mt-5\">\n                              <label class=\"form-label\" for=\"accsdb-authMesage\">\u041E\u0448\u0438\u0431\u043A\u0430 - \u043D\u0435\u0442\u0443 \u0438\u0434\u0435\u043D\u0442\u0438\u0444\u0438\u043A\u0430\u0442\u043E\u0440\u0430 uid, token, email</label>\n                              <input type=\"text\" class=\"form-control\" id=\"accsdb-authMesage\">\n                            </div>\n                            <div class=\"col-12 mb-2\">\n                              <label class=\"form-label\" for=\"accsdb-denyMesage\">\u041E\u0448\u0438\u0431\u043A\u0430 - \u043D\u0435\u0442\u0443 \u0434\u043E\u0441\u0442\u0443\u043F\u0430</label>\n                              <input type=\"text\" class=\"form-control\" id=\"accsdb-denyMesage\">\n                            </div>\n                            <div class=\"col-12 mb-2\">\n                              <label class=\"form-label\" for=\"accsdb-denyGroupMesage\">\u041E\u0448\u0438\u0431\u043A\u0430 - \u0433\u0440\u0443\u043F\u043F\u0430 \u043F\u043E\u043B\u044C\u0437\u043E\u0432\u0430\u0442\u0435\u043B\u044F \u043D\u0438\u0436\u0435 group \u0431\u0430\u043B\u0430\u043D\u0441\u0435\u0440\u0430</label>\n                              <input type=\"text\" class=\"form-control\" id=\"accsdb-denyGroupMesage\">\n                            </div>\n                            <div class=\"col-12 mb-2\">\n                              <label class=\"form-label\" for=\"accsdb-expiresMesage\">\u041E\u0448\u0438\u0431\u043A\u0430 - \u0437\u0430\u043A\u043E\u043D\u0447\u0438\u043B\u0441\u044F \u0434\u043E\u0441\u0442\u0443\u043F</label>\n                              <input type=\"text\" class=\"form-control\" id=\"accsdb-expiresMesage\">\n                            </div>\n                          </div>\n                        </form>\n                      </div>\n                      <div class=\"modal-footer\">\n                        <button type=\"button\" class=\"btn btn-secondary me-2\" data-bs-dismiss=\"modal\">\u0417\u0430\u043A\u0440\u044B\u0442\u044C</button>\n                        <button type=\"button\" class=\"btn btn-primary\" id=\"accsdb-settings-save-btn\">\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C</button>\n                      </div>\n                    </div>\n                  </div>\n                </div>\n                ";
            document.body.insertAdjacentHTML('beforeend', modalHtml);
        }
        // Открытие модального окна и заполнение полей
        var btnSettings = document.getElementById('btn-users-settings');
        if (btnSettings) {
            btnSettings.onclick = function () {
                var _a, _b, _c, _d, _e, _f, _g, _h, _j, _k, _l, _m, _o, _p, _q, _r, _s, _t, _u, _v, _w, _x;
                var accsdb = rootCustomtItems && rootCustomtItems.accsdb ? rootCustomtItems.accsdb : {};
                document.getElementById('accsdb-enable').checked = !!accsdb.enable;
                document.getElementById('accsdb-whitepattern').value = (_a = accsdb.whitepattern) !== null && _a !== void 0 ? _a : '';
                document.getElementById('accsdb-premium_pattern').value = (_b = accsdb.premium_pattern) !== null && _b !== void 0 ? _b : '';
                document.getElementById('accsdb-domainId_pattern').value = (_c = accsdb.domainId_pattern) !== null && _c !== void 0 ? _c : '';
                document.getElementById('accsdb-maxip_hour').value = (_d = accsdb.maxip_hour) !== null && _d !== void 0 ? _d : '';
                document.getElementById('accsdb-maxrequest_hour').value = (_e = accsdb.maxrequest_hour) !== null && _e !== void 0 ? _e : '';
                document.getElementById('accsdb-maxlock_day').value = (_f = accsdb.maxlock_day) !== null && _f !== void 0 ? _f : '';
                document.getElementById('accsdb-blocked_hour').value = (_g = accsdb.blocked_hour) !== null && _g !== void 0 ? _g : '';
                document.getElementById('accsdb-authMesage').value = (_h = accsdb.authMesage) !== null && _h !== void 0 ? _h : '';
                document.getElementById('accsdb-denyMesage').value = (_j = accsdb.denyMesage) !== null && _j !== void 0 ? _j : '';
                document.getElementById('accsdb-denyGroupMesage').value = (_k = accsdb.denyGroupMesage) !== null && _k !== void 0 ? _k : '';
                document.getElementById('accsdb-expiresMesage').value = (_l = accsdb.expiresMesage) !== null && _l !== void 0 ? _l : '';
                var accsDefault = rootDefaultItems && rootDefaultItems.accsdb ? rootDefaultItems.accsdb : {};
                document.getElementById('accsdb-whitepattern').placeholder = (_m = accsDefault.whitepattern) !== null && _m !== void 0 ? _m : '';
                document.getElementById('accsdb-premium_pattern').placeholder = (_o = accsDefault.premium_pattern) !== null && _o !== void 0 ? _o : '';
                document.getElementById('accsdb-domainId_pattern').placeholder = (_p = accsDefault.domainId_pattern) !== null && _p !== void 0 ? _p : '';
                document.getElementById('accsdb-maxip_hour').placeholder = (_q = accsDefault.maxip_hour) !== null && _q !== void 0 ? _q : '';
                document.getElementById('accsdb-maxrequest_hour').placeholder = (_r = accsDefault.maxrequest_hour) !== null && _r !== void 0 ? _r : '';
                document.getElementById('accsdb-maxlock_day').placeholder = (_s = accsDefault.maxlock_day) !== null && _s !== void 0 ? _s : '';
                document.getElementById('accsdb-blocked_hour').placeholder = (_t = accsDefault.blocked_hour) !== null && _t !== void 0 ? _t : '';
                document.getElementById('accsdb-authMesage').placeholder = (_u = accsDefault.authMesage) !== null && _u !== void 0 ? _u : '';
                document.getElementById('accsdb-denyMesage').placeholder = (_v = accsDefault.denyMesage) !== null && _v !== void 0 ? _v : '';
                document.getElementById('accsdb-denyGroupMesage').placeholder = (_w = accsDefault.denyGroupMesage) !== null && _w !== void 0 ? _w : '';
                document.getElementById('accsdb-expiresMesage').placeholder = (_x = accsDefault.expiresMesage) !== null && _x !== void 0 ? _x : '';
                // @ts-ignore
                var modal = new bootstrap.Modal(document.getElementById('accsdb-settings-modal'));
                modal.show();
            };
        }
        // Сохранение изменений
        var accsdbSaveBtn = document.getElementById('accsdb-settings-save-btn');
        if (accsdbSaveBtn) {
            accsdbSaveBtn.onclick = function () {
                if (!rootCustomtItems)
                    return;
                if (!rootCustomtItems.accsdb)
                    rootCustomtItems.accsdb = {};
                var accsdb = rootCustomtItems.accsdb;
                accsdb.enable = document.getElementById('accsdb-enable').checked;
                accsdb.whitepattern = document.getElementById('accsdb-whitepattern').value;
                accsdb.premium_pattern = document.getElementById('accsdb-premium_pattern').value || null;
                accsdb.domainId_pattern = document.getElementById('accsdb-domainId_pattern').value || null;
                accsdb.maxip_hour = parseInt(document.getElementById('accsdb-maxip_hour').value, 10) || 0;
                accsdb.maxrequest_hour = parseInt(document.getElementById('accsdb-maxrequest_hour').value, 10) || 0;
                accsdb.maxlock_day = parseInt(document.getElementById('accsdb-maxlock_day').value, 10) || 0;
                accsdb.blocked_hour = parseInt(document.getElementById('accsdb-blocked_hour').value, 10) || 0;
                accsdb.authMesage = document.getElementById('accsdb-authMesage').value;
                accsdb.denyMesage = document.getElementById('accsdb-denyMesage').value;
                accsdb.denyGroupMesage = document.getElementById('accsdb-denyGroupMesage').value;
                accsdb.expiresMesage = document.getElementById('accsdb-expiresMesage').value;
                // Удаление переменных с пустыми или дефолтными значениями
                var defaults = {
                    enable: false,
                    whitepattern: '',
                    premium_pattern: null,
                    domainId_pattern: null,
                    maxip_hour: 0,
                    maxrequest_hour: 0,
                    maxlock_day: 0,
                    blocked_hour: 0,
                    authMesage: '',
                    denyMesage: '',
                    denyGroupMesage: '',
                    expiresMesage: ''
                };
                Object.keys(defaults).forEach(function (key) {
                    var value = accsdb[key];
                    var def = defaults[key];
                    if (value === undefined ||
                        value === null ||
                        (typeof def === 'string' && (value === '' || value === def)) ||
                        (typeof def === 'number' && value === def) ||
                        (typeof def === 'boolean' && value === def)) {
                        delete accsdb[key];
                    }
                });
                // @ts-ignore
                var modal = bootstrap.Modal.getInstance(document.getElementById('accsdb-settings-modal'));
                if (modal)
                    modal.hide();
                saveCustomtItems();
            };
        }
    }, 0);
}
function attachEditHandlers(users) {
    users.forEach(function (user, idx) {
        var btn = document.getElementById("edit-user-btn-".concat(idx));
        if (btn) {
            btn.onclick = function () {
                var editModalId = "editUserModal-".concat(idx);
                var editModalHtml = getUserModalHtml(editModalId, 'Редактировать пользователя', user, undefined, true);
                var editModalContainer = document.getElementById('edit-user-modal-container');
                if (editModalContainer) {
                    editModalContainer.innerHTML = editModalHtml;
                }
                // @ts-ignore
                var modal = new bootstrap.Modal(document.getElementById(editModalId));
                modal.show();
                setTimeout(function () {
                    var editParamsBtn = document.getElementById("".concat(editModalId, "-params-btn"));
                    if (editParamsBtn) {
                        editParamsBtn.onclick = function () {
                            if (window.invc)
                                window.invc.editUser(user);
                        };
                    }
                    var saveBtn = document.getElementById("".concat(editModalId, "-save-btn"));
                    if (saveBtn) {
                        saveBtn.onclick = function () {
                            var id = document.getElementById("".concat(editModalId, "-user-id")).value.trim();
                            var idsRaw = document.getElementById("".concat(editModalId, "-user-ids")).value.trim();
                            var expires = document.getElementById("".concat(editModalId, "-user-expires")).value;
                            var group = parseInt(document.getElementById("".concat(editModalId, "-user-group")).value, 10);
                            var comment = document.getElementById("".concat(editModalId, "-user-comment")).value.trim();
                            var ban = document.getElementById("".concat(editModalId, "-user-ban")).checked;
                            var ban_msg = document.getElementById("".concat(editModalId, "-user-ban-msg")).value.trim();
                            if (!id || !expires || isNaN(group)) {
                                alert('Пожалуйста, заполните все обязательные поля');
                                return;
                            }
                            var ids = idsRaw ? idsRaw.split(',').map(function (s) { return s.trim(); }).filter(Boolean) : [];
                            user.id = id;
                            user.ids = ids;
                            user.expires = expires + "T00:00:00";
                            user.group = group;
                            user.comment = comment;
                            user.ban = ban;
                            user.ban_msg = ban_msg;
                            var usersList = document.getElementById('users-list');
                            if (usersList) {
                                usersList.innerHTML = renderUsers(rootCustomtItems["accsdb"]["users"]);
                                attachEditHandlers(rootCustomtItems["accsdb"]["users"]);
                            }
                            // @ts-ignore
                            var modal = bootstrap.Modal.getInstance(document.getElementById(editModalId));
                            if (modal)
                                modal.hide();
                            saveCustomtItems();
                        };
                    }
                    // Обработчик для кнопки "Удалить"
                    var deleteBtn = document.getElementById("".concat(editModalId, "-delete-btn"));
                    if (deleteBtn) {
                        deleteBtn.onclick = function () {
                            if (confirm('Удалить пользователя?')) {
                                if (rootCustomtItems && rootCustomtItems["accsdb"] && Array.isArray(rootCustomtItems["accsdb"]["users"])) {
                                    var usersArr = rootCustomtItems["accsdb"]["users"];
                                    var userIdx = usersArr.indexOf(user);
                                    if (userIdx !== -1) {
                                        usersArr.splice(userIdx, 1);
                                        var usersList = document.getElementById('users-list');
                                        if (usersList) {
                                            usersList.innerHTML = renderUsers(usersArr);
                                            attachEditHandlers(usersArr);
                                        }
                                        // @ts-ignore
                                        var modal_1 = bootstrap.Modal.getInstance(document.getElementById(editModalId));
                                        if (modal_1)
                                            modal_1.hide();
                                        saveCustomtItems();
                                    }
                                }
                            }
                        };
                    }
                }, 0);
            };
        }
    });
}
function loadAndRenderUsers(containerId) {
    var container = document.getElementById(containerId);
    if (!container)
        return;
    // Сначала загружаем custom, затем current
    fetch('/admin/init/custom')
        .then(function (res) { return res.json(); })
        .then(function (ob) {
        rootCustomtItems = ob;
        // Теперь загружаем current
        return fetch('/admin/init/current');
    })
        .then(function (res) { return res.json(); })
        .then(function (ob) {
        // Проверяем наличие accsdb
        if (!rootCustomtItems.accsdb)
            rootCustomtItems.accsdb = {};
        rootCustomtItems.accsdb.users = ob.accsdb && Array.isArray(ob.accsdb.users) ? ob.accsdb.users : [];
        // Сортировка по user.expires (по возрастанию даты, пустые в конце)
        rootCustomtItems.accsdb.users = __spreadArray([], rootCustomtItems.accsdb.users, true).sort(function (a, b) {
            var aDate = a.expires ? new Date(a.expires).getTime() : Infinity;
            var bDate = b.expires ? new Date(b.expires).getTime() : Infinity;
            return aDate - bDate;
        });
        container.innerHTML = renderUsers(rootCustomtItems.accsdb.users);
        // После рендера таблицы навесить обработчики на кнопки "Редактировать"
        setTimeout(function () {
            if (rootCustomtItems && rootCustomtItems.accsdb && Array.isArray(rootCustomtItems.accsdb.users)) {
                attachEditHandlers(rootCustomtItems.accsdb.users);
            }
        }, 0);
    })
        .catch(function () {
        container.innerHTML = '<div class="alert alert-danger">Ошибка загрузки пользователей</div>';
    });
}
function renderUsers(users) {
    // Получаем текущую дату без времени для сравнения
    var now = new Date();
    now.setHours(0, 0, 0, 0);
    return "\n        <table class=\"table table-bordered table-striped align-middle\">\n            <thead>\n                <tr>\n                    <th style=\"width:48px; text-align:center;\"></th>\n                    <th>ID</th>\n                    <th>IDs</th>\n                    <th>\u0418\u0441\u0442\u0435\u043A\u0430\u0435\u0442</th>\n                    <th>\u0413\u0440\u0443\u043F\u043F\u0430</th>\n                    <th>\u041A\u043E\u043C\u043C\u0435\u043D\u0442\u0430\u0440\u0438\u0439</th>\n                    <th>\u0417\u0430\u0431\u043B\u043E\u043A\u0438\u0440\u043E\u0432\u0430\u043D</th>\n                    <th>ban_msg</th>\n                </tr>\n            </thead>\n            <tbody>\n                ".concat(users.map(function (user, idx) {
        // Проверка на просроченность и "оранжевый" статус
        var isExpired = false;
        var isWarning = false;
        if (user.expires) {
            var expiresDate = new Date(user.expires);
            expiresDate.setHours(0, 0, 0, 0);
            isExpired = expiresDate < now;
            // Проверка на "оранжевый" (меньше 30 дней, но не просрочен)
            var diffDays = Math.ceil((expiresDate.getTime() - now.getTime()) / (1000 * 60 * 60 * 24));
            isWarning = !isExpired && diffDays <= 30;
        }
        // Проверка на бан
        var isBanned = !!user.ban;
        // Классы для выделения
        var expiresClass = '';
        if (isExpired) {
            expiresClass = 'style="background:#ffdddd;"';
        }
        else if (isWarning) {
            expiresClass = 'style="background:#fff3cd;"'; // Bootstrap warning (light orange)
        }
        var banClass = isBanned ? 'style="background:#ffdddd;"' : '';
        return "\n                        <tr>\n                            <td style=\"width:48px; text-align:center;\">\n                                <button type=\"button\" class=\"btn btn-sm btn-light p-1\" id=\"edit-user-btn-".concat(idx, "\" title=\"\u0420\u0435\u0434\u0430\u043A\u0442\u0438\u0440\u043E\u0432\u0430\u0442\u044C\" style=\"width:32px; height:32px;\">\n                                    <svg xmlns=\"http://www.w3.org/2000/svg\" width=\"18\" height=\"18\" fill=\"currentColor\" class=\"bi bi-gear\" viewBox=\"0 0 16 16\">\n                                      <path d=\"M8 4.754a3.246 3.246 0 1 0 0 6.492 3.246 3.246 0 0 0 0-6.492zM5.754 8a2.246 2.246 0 1 1 4.492 0 2.246 2.246 0 0 1-4.492 0z\"/>\n                                      <path d=\"M9.796 1.343c-.527-1.79-3.065-1.79-3.592 0l-.094.319a.873.873 0 0 1-1.255.52l-.292-.16c-1.64-.892-3.433.902-2.54 2.541l.159.292a.873.873 0 0 1-.52 1.255l-.319.094c-1.79.527-1.79 3.065 0 3.592l.319.094a.873.873 0 0 1 .52 1.255l-.16.292c-.892 1.64.901 3.434 2.541 2.54l.292-.159a.873.873 0 0 1 1.255.52l.094.319c.527 1.79 3.065 1.79 3.592 0l.094-.319a.873.873 0 0 1 1.255-.52l.292.16c1.64.893 3.434-.902 2.54-2.541l-.159-.292a.873.873 0 0 1 .52-1.255l.319-.094c1.79-.527 1.79-3.065 0-3.592l-.319-.094a.873.873 0 0 1-.52-1.255l.16-.292c.893-1.64-.902-3.433-2.541-2.54l-.292.159a.873.873 0 0 1-1.255-.52l-.094-.319zm-2.633.283c.246-.835 1.428-.835 1.674 0l.094.319a1.873 1.873 0 0 0 2.693 1.115l.291-.16c.764-.415 1.6.42 1.184 1.185l-.159.292a1.873 1.873 0 0 0 1.116 2.692l.318.094c.835.246.835 1.428 0 1.674l-.319.094a1.873 1.873 0 0 0-1.115 2.693l.16.291c.415.764-.42 1.6-1.185 1.184l-.291-.159a1.873 1.873 0 0 0-2.693 1.116l-.094.318c-.246.835-1.428.835-1.674 0l-.094-.319a1.873 1.873 0 0 0-2.692-1.115l-.292.16c-.764.415-1.6-.42-1.184-1.185l.159-.291A1.873 1.873 0 0 0 1.945 8.93l-.319-.094c-.835-.246-.835-1.428 0-1.674l.319-.094A1.873 1.873 0 0 0 3.06 4.377l-.16-.292c-.415-.764.42-1.6 1.185-1.184l.292.159a1.873 1.873 0 0 0 2.692-1.115l.094-.319z\"/>\n                                    </svg>\n                                </button>\n                            </td>\n                            <td>").concat(user.id || 'не указан', "</td>\n                            <td>").concat(Array.isArray(user.ids) ? user.ids.join('<br>') : '', "</td>\n                            <td ").concat(expiresClass, ">").concat(user.expires ? formatDate(user.expires) : '', "</td>\n                            <td>").concat(user.group || 0, "</td>\n                            <td>").concat(user.comment || '', "</td>\n                            <td ").concat(banClass, ">").concat(user.ban ? 'Да' : 'Нет', "</td>\n                            <td>").concat(user.ban_msg || '', "</td>\n                        </tr>\n                    ");
    }).join(''), "\n            </tbody>\n        </table>\n    ");
}
function renderEditorPage(containerId) {
    var _this = this;
    var container = document.getElementById(containerId);
    if (!container)
        return;
    // Список пунктов меню
    var menuItems = [
        { key: "custom", label: "custom" },
        { key: "current", label: "current" },
        { key: "default", label: "default" }
    ];
    // Кэш для данных редактора
    var editorDataCache = {};
    // Формируем HTML для бокового меню в стиле online.ts
    var sidebarHtml = "\n        <div class=\"list-group\" id=\"editor-sidebar\">\n            ".concat(menuItems.map(function (item, idx) { return "\n                <a href=\"#\" class=\"list-group-item list-group-item-action".concat(idx === 0 ? ' active' : '', "\" data-type=\"").concat(item.key, "\">").concat(item.label, "</a>\n            "); }).join(''), "\n        </div>\n    ");
    // Добавляем кастомный стиль для активного пункта (как в online.ts)
    var styleId = 'editor-sidebar-active-style';
    if (!document.getElementById(styleId)) {
        var style = document.createElement('style');
        style.id = styleId;
        style.innerHTML = "\n            #editor-sidebar .list-group-item.active {\n                background: linear-gradient(90deg, #0d6efd 60%, #3a8bfd 100%);\n                color: #fff;\n                font-weight: bold;\n                border-color: #0d6efd;\n                box-shadow: 0 2px 8px rgba(13,110,253,0.10);\n            }\n            #editor-sidebar .list-group-item.active:focus, \n            #editor-sidebar .list-group-item.active:hover {\n                background: linear-gradient(90deg, #0b5ed7 60%, #2563eb 100%);\n                color: #fff;\n            }\n        ";
        document.head.appendChild(style);
    }
    container.innerHTML = "\n        <div class=\"d-flex justify-content-between align-items-center mb-3\">\n            <h1 class=\"mb-0\">\u0420\u0435\u0434\u0430\u043A\u0442\u043E\u0440</h1>\n            <button id=\"editor-save-btn\" class=\"btn btn-primary\">\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C</button>\n        </div>\n        <div class=\"row\">\n            <div class=\"col-12 col-md-2 mb-2 mb-md-0\">\n                ".concat(sidebarHtml, "\n            </div>\n            <div class=\"col-12 col-md-10\" style=\"min-height:400px;\">\n                <div id=\"editor-codemirror\" style=\"\n                    border:1px solid #ced4da;\n                    border-radius:0.375rem;\n                    width:100%;\n                    min-height:400px;\n                    height:calc(100vh - 160px);\n                    box-sizing:border-box;\n                \"></div>\n            </div>\n        </div>\n    ");
    var editor = null;
    function loadEditorData(type) {
        // Если данные уже есть в кэше, используем их
        if (type === 'default')
            editorDataCache[type] = rootDefaultItems;
        if (editorDataCache[type]) {
            var jsonText = JSON.stringify(editorDataCache[type], null, 2);
            if (editor) {
                editor.setValue(jsonText);
            }
            else {
                // @ts-ignore
                editor = CodeMirror(document.getElementById('editor-codemirror'), {
                    value: jsonText,
                    mode: { name: "javascript", json: true },
                    lineNumbers: true,
                    lineWrapping: true,
                    theme: "default",
                    viewportMargin: Infinity,
                });
                var wrapper = editor.getWrapperElement();
                wrapper.style.height = "100%";
                wrapper.style.minHeight = "400px";
            }
            return;
        }
        // Если нет в кэше — делаем fetch
        fetch("/admin/init/".concat(type))
            .then(function (response) {
            if (!response.ok)
                throw new Error('Ошибка загрузки данных');
            return response.json();
        })
            .then(function (data) {
            editorDataCache[type] = data; // сохраняем в кэш
            var jsonText = JSON.stringify(data, null, 2);
            if (editor) {
                editor.setValue(jsonText);
            }
            else {
                // @ts-ignore
                editor = CodeMirror(document.getElementById('editor-codemirror'), {
                    value: jsonText,
                    mode: { name: "javascript", json: true },
                    lineNumbers: true,
                    lineWrapping: true,
                    theme: "default",
                    viewportMargin: Infinity,
                });
                var wrapper = editor.getWrapperElement();
                wrapper.style.height = "100%";
                wrapper.style.minHeight = "400px";
            }
        })
            .catch(function (error) {
            var errorText = "\u041E\u0448\u0438\u0431\u043A\u0430: ".concat(error.message);
            if (editor) {
                editor.setValue(errorText);
            }
            else {
                // @ts-ignore
                editor = CodeMirror(document.getElementById('editor-codemirror'), {
                    value: errorText,
                    mode: { name: "javascript", json: true },
                    lineNumbers: true,
                    lineWrapping: true,
                    theme: "default",
                    viewportMargin: Infinity,
                });
                var wrapper = editor.getWrapperElement();
                wrapper.style.height = "100%";
                wrapper.style.minHeight = "400px";
            }
        });
    }
    // Инициализация редактора с "custom"
    loadEditorData('custom');
    // Обработчик кликов по боковому меню
    setTimeout(function () {
        var sidebar = document.getElementById('editor-sidebar');
        var saveBtn = document.getElementById('editor-save-btn');
        var currentType = "custom";
        if (sidebar) {
            sidebar.querySelectorAll('a[data-type]').forEach(function (link) {
                link.addEventListener('click', function (e) {
                    e.preventDefault();
                    // Снимаем активный класс со всех
                    sidebar.querySelectorAll('.list-group-item').forEach(function (item) { return item.classList.remove('active'); });
                    // Добавляем активный класс текущему
                    e.currentTarget.classList.add('active');
                    var type = e.currentTarget.getAttribute('data-type');
                    if (type) {
                        currentType = type;
                        loadEditorData(type);
                        // Показываем кнопку только для custom
                        if (saveBtn) {
                            saveBtn.style.display = (type === "custom") ? "" : "none";
                        }
                    }
                });
            });
        }
        // Скрыть кнопку если выбран не custom при инициализации
        if (saveBtn) {
            saveBtn.style.display = (currentType === "custom") ? "" : "none";
            saveBtn.onclick = function () {
                if (!editor)
                    return;
                var json;
                try {
                    json = JSON.stringify(JSON.parse(editor.getValue()), null, 2);
                }
                catch (e) {
                    alert(e);
                    return;
                }
                var formData = new FormData();
                formData.append('json', json);
                fetch('/admin/init/save', {
                    method: 'POST',
                    body: formData
                })
                    .then(function (response) { return __awaiter(_this, void 0, void 0, function () {
                    var data;
                    return __generator(this, function (_a) {
                        switch (_a.label) {
                            case 0:
                                if (!response.ok) return [3 /*break*/, 2];
                                return [4 /*yield*/, response.json()];
                            case 1:
                                data = _a.sent();
                                if (data && data.success === true) {
                                    // Обновляем кэш для custom
                                    try {
                                        editorDataCache["custom"] = JSON.parse(json);
                                    }
                                    catch (_b) { }
                                    showToast('Данные успешно сохранены');
                                }
                                else if (data && data.ex) {
                                    alert(data.ex);
                                }
                                else {
                                    alert('Ошибка: сервер не подтвердил сохранение');
                                }
                                return [3 /*break*/, 3];
                            case 2:
                                alert('Ошибка при сохранении данных');
                                _a.label = 3;
                            case 3: return [2 /*return*/];
                        }
                    });
                }); })
                    .catch(function () {
                    alert('Ошибка при отправке запроса');
                });
            };
        }
    }, 0);
}
var __assign = (this && this.__assign) || function () {
    __assign = Object.assign || function(t) {
        for (var s, i = 1, n = arguments.length; i < n; i++) {
            s = arguments[i];
            for (var p in s) if (Object.prototype.hasOwnProperty.call(s, p))
                t[p] = s[p];
        }
        return t;
    };
    return __assign.apply(this, arguments);
};
// Массив балансеров будет формироваться динамически
var balancers = [];
function loadCustomAndCurrent(container, onLoaded) {
    function checkLoaded() {
        if (onLoaded) {
            // Формируем balancers только после загрузки rootDefaultItems
            if (rootDefaultItems) {
                balancers = Object.keys(rootDefaultItems).filter(function (key) { return rootDefaultItems[key] && typeof rootDefaultItems[key].kit !== 'undefined' && rootDefaultItems[key].rip !== true; });
            }
            else {
                balancers = [];
            }
            onLoaded();
        }
    }
    fetch('/admin/init/custom')
        .then(function (res) { return res.json(); })
        .then(function (ob) {
        rootCustomtItems = ob;
        checkLoaded();
    })
        .catch(function () {
        if (container) {
            container.innerHTML = "<div class=\"alert alert-danger\">\u041E\u0448\u0438\u0431\u043A\u0430 \u0437\u0430\u0433\u0440\u0443\u0437\u043A\u0438 rootCustomtItems</div>";
        }
    });
}
function renderOnlinePage(containerId) {
    var container = document.getElementById(containerId);
    if (!container)
        return;
    // Базовые поля по умолчанию
    var defaultBalancer = {
        host: "",
        apihost: "",
        scheme: "",
        cookie: "",
        token: "",
        overridehost: "",
        overridehosts: [""],
        overridepasswd: "",
        displayname: "",
        displayindex: 0,
        webcorshost: "",
        globalnameproxy: "",
        geostreamproxy: [""],
        group: 0,
        geo_hide: [""],
        client_type: "",
        cache_time: 0,
        rhub_geo_disable: [""],
        priorityBrowser: "",
        vast: {
            url: "",
            msg: ""
        },
        headers: {},
        headers_stream: {},
    };
    // Ключи, которые нужно игнорировать при выводе и сохранении
    var ignoreKeys = ['rip', 'proxy', 'plugin', 'apn'];
    var ignoreSaveKeys = ['headers', 'headers_stream'];
    // Функция для рендера формы по balancer
    function renderBalancerForm(balancer) {
        var balancerDefaults = __assign({}, defaultBalancer);
        if (balancer === "FilmixTV") {
            balancerDefaults = __assign({ user_apitv: "", passwd_apitv: "", tokens: [""] }, balancerDefaults);
        }
        else if (balancer === "FilmixPartner") {
            balancerDefaults = __assign(__assign({ APIKEY: "", APISECRET: "", user_name: "", user_passw: "", lowlevel_api_passw: "" }, balancerDefaults), { tokens: [""] });
        }
        else if (balancer === "Rezka" || balancer === "RezkaPrem") {
            balancerDefaults = __assign({ login: "", passwd: "" }, balancerDefaults);
        }
        else if (balancer === "VideoCDN") {
            balancerDefaults = __assign({ clientId: "", iframehost: "", username: "", password: "", domain: "" }, balancerDefaults);
        }
        else if (balancer === "KinoPub") {
            balancerDefaults = __assign({ filetype: "", tokens: [""] }, balancerDefaults);
        }
        else if (balancer === "Alloha" || balancer === "Mirage" || balancer === "Kodik") {
            balancerDefaults = __assign({ secret_token: "", linkhost: "" }, balancerDefaults);
        }
        var current = (rootDefaultItems && rootDefaultItems[balancer]) ? rootDefaultItems[balancer] : {};
        var custom = (rootCustomtItems && rootCustomtItems[balancer]) ? rootCustomtItems[balancer] : {};
        var allKeys = Array.from(new Set(__spreadArray(__spreadArray(__spreadArray([], Object.keys(balancerDefaults), true), Object.keys(current), true), Object.keys(custom), true))).filter(function (key) { return !ignoreKeys.includes(key); });
        var data = __assign(__assign({}, balancerDefaults), current);
        var mainContent = document.getElementById('online-main-content');
        if (!mainContent)
            return;
        function renderObjectAsText(obj) {
            if (!obj)
                return '';
            return Object.entries(obj)
                .map(function (_a) {
                var k = _a[0], v = _a[1];
                return "<b>".concat(k, "</b>: ").concat(JSON.stringify(v));
            })
                .join(',<br>');
        }
        // Ключи, для которых нужно добавить "- через запятую"
        var arrayCommaKeys = ['rhub_geo_disable', 'geo_hide', 'overridehosts', 'geostreamproxy', 'tokens'];
        mainContent.innerHTML = "\n            <div class=\"d-flex justify-content-between align-items-center mb-3\">\n                <h1 class=\"mb-0\">".concat(balancer, "</h1>\n                <button type=\"button\" class=\"btn btn-primary\" id=\"balancer-save-btn\">\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C</button>\n            </div>\n            <form id=\"balancer-form\">\n                ").concat(allKeys
            .sort(function (a, b) {
            var aIsBool = typeof data[a] === 'boolean';
            var bIsBool = typeof data[b] === 'boolean';
            if (aIsBool === bIsBool)
                return 0;
            return aIsBool ? 1 : -1;
        })
            .map(function (key) {
            var _a, _b, _c, _d, _e;
            var defValue = balancerDefaults[key];
            var value = current[key];
            var customValue = custom[key];
            if (key === 'headers' || key === 'headers_stream') {
                // Не выводить, если пустой объект или undefined/null
                var obj = __assign({}, (customValue || value || defValue));
                var balancerKey_1 = balancer; // для замыкания
                var modalId_1 = 'online-modal';
                // Генерация строк для редактирования
                var rows_1 = Object.entries(obj)
                    .map(function (_a, idx) {
                    var k = _a[0], v = _a[1];
                    return "\n                                <div class=\"row mb-2 align-items-center\" data-row>\n                                    <div class=\"col-5\">\n                                        <input type=\"text\" class=\"form-control form-control-sm\" name=\"header-key\" value=\"".concat(k, "\" data-idx=\"").concat(idx, "\">\n                                    </div>\n                                    <div class=\"col-5\">\n                                        <input type=\"text\" class=\"form-control form-control-sm\" name=\"header-value\" value=\"").concat(escapeHtmlAttr(typeof v === 'object' ? JSON.stringify(v) : v), "\" data-idx=\"").concat(idx, "\">\n                                    </div>\n                                    <div class=\"col-2 text-end\">\n                                        <button type=\"button\" class=\"btn btn-danger btn-sm\" data-remove-row>&times;</button>\n                                    </div>\n                                </div>\n                            ");
                }).join('');
                // Добавляем модальное окно (один раз)
                if (!document.getElementById(modalId_1)) {
                    var modalHtml = "\n                                <div class=\"modal fade\" id=\"".concat(modalId_1, "\" tabindex=\"-1\" aria-labelledby=\"online-modal-label\" aria-hidden=\"true\">\n                                  <div class=\"modal-dialog modal-xl\">\n                                    <div class=\"modal-content\">\n                                      <div class=\"modal-header\">\n                                        <h5 class=\"modal-title\" id=\"online-modal-label\">\u0420\u0435\u0434\u0430\u043A\u0442\u0438\u0440\u043E\u0432\u0430\u0442\u044C ").concat(key, "</h5>\n                                        <button type=\"button\" class=\"btn-close\" data-bs-dismiss=\"modal\" aria-label=\"\u0417\u0430\u043A\u0440\u044B\u0442\u044C\"></button>\n                                      </div>\n                                      <div class=\"modal-body\" id=\"online-modal-body\"></div>\n                                      <div class=\"modal-footer\">\n                                        <button type=\"button\" class=\"btn btn-secondary me-2\" data-bs-dismiss=\"modal\">\u0417\u0430\u043A\u0440\u044B\u0442\u044C</button>\n                                        <button type=\"button\" class=\"btn btn-primary\" id=\"modal-save-btn\">\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C</button>\n                                      </div>\n                                    </div>\n                                  </div>\n                                </div>\n                            ");
                    document.body.insertAdjacentHTML('beforeend', modalHtml);
                }
                // Кнопка для показа модального окна
                setTimeout(function () {
                    var btn = document.getElementById("show-modal-".concat(key));
                    if (btn) {
                        btn.onclick = function () {
                            var modalBody = document.getElementById('online-modal-body');
                            if (modalBody) {
                                modalBody.innerHTML = "\n                                                <form id=\"modal-headers-form\">\n                                                    <div id=\"modal-headers-rows\">\n                                                        ".concat(rows_1, "\n                                                    </div>\n                                                    <button type=\"button\" class=\"btn btn-success btn-sm mt-2\" id=\"add-header-row\">\u0414\u043E\u0431\u0430\u0432\u0438\u0442\u044C</button>\n                                                </form>\n                                            ");
                                // Добавить новую строку
                                var addBtn = document.getElementById('add-header-row');
                                if (addBtn) {
                                    addBtn.onclick = function () {
                                        var rowsDiv = document.getElementById('modal-headers-rows');
                                        if (rowsDiv) {
                                            rowsDiv.insertAdjacentHTML('beforeend', "\n                                                        <div class=\"row mb-2 align-items-center\" data-row>\n                                                            <div class=\"col-5\">\n                                                                <input type=\"text\" class=\"form-control form-control-sm\" name=\"header-key\" value=\"\">\n                                                            </div>\n                                                            <div class=\"col-5\">\n                                                                <input type=\"text\" class=\"form-control form-control-sm\" name=\"header-value\" value=\"\">\n                                                            </div>\n                                                            <div class=\"col-2 text-end\">\n                                                                <button type=\"button\" class=\"btn btn-danger btn-sm\" data-remove-row>&times;</button>\n                                                            </div>\n                                                        </div>\n                                                    ");
                                        }
                                    };
                                }
                                // Удаление строки
                                modalBody.addEventListener('click', function (e) {
                                    var target = e.target;
                                    if (target && target.hasAttribute('data-remove-row')) {
                                        var row = target.closest('[data-row]');
                                        if (row)
                                            row.remove();
                                    }
                                });
                                // Сохранение
                                var saveBtn_1 = document.getElementById('modal-save-btn');
                                if (saveBtn_1) {
                                    saveBtn_1.onclick = function () {
                                        var form = document.getElementById('modal-headers-form');
                                        if (!form)
                                            return;
                                        var keys = Array.from(form.querySelectorAll('input[name="header-key"]'));
                                        var values = Array.from(form.querySelectorAll('input[name="header-value"]'));
                                        var newObj = {};
                                        for (var i = 0; i < keys.length; i++) {
                                            var k = keys[i].value.trim();
                                            var v = values[i].value;
                                            if (!k)
                                                continue;
                                            // Попытка распарсить JSON, иначе строка
                                            try {
                                                v = JSON.parse(v);
                                            }
                                            catch ( /* оставить строкой */_a) { /* оставить строкой */ }
                                            newObj[k] = v;
                                        }
                                        // Обновляем rootCustomtItems
                                        if (!rootCustomtItems)
                                            rootCustomtItems = {};
                                        if (!rootCustomtItems[balancerKey_1])
                                            rootCustomtItems[balancerKey_1] = {};
                                        rootCustomtItems[balancerKey_1][key] = newObj;
                                        // Закрыть модалку
                                        // @ts-ignore
                                        var modal = bootstrap.Modal.getInstance(document.getElementById(modalId_1));
                                        if (modal)
                                            modal.hide();
                                        // Перерисовать форму
                                        renderBalancerForm(balancerKey_1);
                                        // Автоматически нажимаем на кнопку "Сохранить" основной формы
                                        var balancerSaveBtn = document.getElementById('balancer-save-btn');
                                        if (balancerSaveBtn) {
                                            balancerSaveBtn.click();
                                        }
                                    };
                                }
                            }
                            // Открываем модальное окно через Bootstrap API
                            // @ts-ignore
                            var modal = new bootstrap.Modal(document.getElementById(modalId_1));
                            modal.show();
                        };
                    }
                }, 0);
                return "\n                            <div class=\"mb-2\">\n                                <b id=\"show-modal-".concat(key, "\" style=\"cursor:pointer; color:#0d6efd;\">").concat(key, "</b>\n                            </div>\n                            <div class=\"mb-3\" style=\"font-family:monospace; background:#f8f9fa; border-radius:4px; padding:8px;\">\n                                ").concat(renderObjectAsText(obj), "\n                            </div>\n                        ");
            }
            else if (key === 'vast') {
                return "\n                                <div class=\"mb-2\"><b>vast</b></div>\n                                <div class=\"mb-3 ms-3\">\n                                    <label class=\"form-label\">url</label>\n                                    <input type=\"text\" class=\"form-control\" name=\"vast.url\" value=\"".concat((customValue && customValue.url) ? customValue.url : '', "\" placeholder=\"").concat((_a = (value && value.url)) !== null && _a !== void 0 ? _a : '', "\">\n                                </div>\n                                <div class=\"mb-3 ms-3\">\n                                    <label class=\"form-label\">msg</label>\n                                    <input type=\"text\" class=\"form-control\" name=\"vast.msg\" value=\"").concat((customValue && customValue.msg) ? customValue.msg : '', "\" placeholder=\"").concat((_b = (value && value.msg)) !== null && _b !== void 0 ? _b : '', "\">\n                                </div>\n                            ");
            }
            else if (typeof ((_c = customValue !== null && customValue !== void 0 ? customValue : value) !== null && _c !== void 0 ? _c : defValue) === 'object' && !Array.isArray((_d = customValue !== null && customValue !== void 0 ? customValue : value) !== null && _d !== void 0 ? _d : defValue))
                return '';
            else if (Array.isArray((_e = customValue !== null && customValue !== void 0 ? customValue : value) !== null && _e !== void 0 ? _e : defValue)) {
                var commaLabel = arrayCommaKeys.includes(key) ? ' <span class="text-muted">- через запятую</span>' : '';
                return "\n                                <div class=\"mb-3\">\n                                    <label class=\"form-label\">".concat(key).concat(commaLabel, "</label>\n                                    <input type=\"text\" class=\"form-control\" name=\"").concat(key, "\" value=\"").concat(Array.isArray(customValue) ? customValue.join(', ') : '', "\" placeholder=\"").concat(Array.isArray(value) ? value.join(', ') : '', "\">\n                                </div>\n                            ");
            }
            else if (typeof value === 'boolean') {
                // Для boolean: если customValue определён, используем его, иначе value
                var checked = (typeof customValue === 'boolean' ? customValue : value) ? 'checked' : '';
                var notDefault = (typeof customValue === 'boolean') ? ' <span class="text-warning">(not default)</span>' : '';
                return "\n                            <div class=\"form-check mb-3\">\n                                <input class=\"form-check-input\" type=\"checkbox\" name=\"".concat(key, "\" id=\"balancer-").concat(key, "\" ").concat(checked, ">\n                                <label class=\"form-check-label\" for=\"balancer-").concat(key, "\">").concat(key).concat(notDefault, "</label>\n                            </div>\n                        ");
            }
            return "\n                            <div class=\"mb-3\">\n                                <label class=\"form-label\">".concat(key, "</label>\n                                <input type=\"text\" class=\"form-control\" name=\"").concat(key, "\" value=\"").concat(customValue !== undefined && customValue !== null ? customValue : '', "\" placeholder=\"").concat(value !== undefined && value !== null ? value : '', "\">\n                            </div>\n                        ");
        }).join(''), "\n            <button type=\"button\" class=\"btn btn-primary mt-4\" id=\"balancer-save-btn2\">\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C</button></form>\n        ");
        var saveBtn2 = document.getElementById('balancer-save-btn2');
        if (saveBtn2) {
            saveBtn2.onclick = function () {
                var saveBtn = document.getElementById('balancer-save-btn');
                if (saveBtn) {
                    saveBtn.click();
                }
            };
        }
        // Обработчик кнопки "Сохранить"
        var saveBtn = document.getElementById('balancer-save-btn');
        if (saveBtn) {
            saveBtn.onclick = function () {
                var form = document.getElementById('balancer-form');
                if (!form)
                    return;
                // Собираем значения из формы
                var formData = new FormData(form);
                var updated = {};
                allKeys.forEach(function (key) {
                    var _a;
                    if (ignoreSaveKeys.includes(key))
                        return;
                    // vast
                    if (key === 'vast') {
                        updated.vast = {
                            url: formData.get('vast.url') || "",
                            msg: formData.get('vast.msg') || ""
                        };
                        return;
                    }
                    // boolean
                    if (typeof current[key] === 'boolean') {
                        updated[key] = formData.get(key) === 'on';
                        return;
                    }
                    // int (сохраняем как число, если в rootDefaultItems тип number и целое)
                    if (typeof current[key] === 'number' &&
                        Number.isInteger(current[key])) {
                        var val = formData.get(key);
                        updated[key] = val !== null && val !== undefined && val !== ""
                            ? parseInt(val, 10)
                            : 0;
                        return;
                    }
                    // массивы
                    if (Array.isArray(defaultBalancer[key]) || Array.isArray(current[key])) {
                        var val = formData.get(key) || "";
                        updated[key] = val.split(',').map(function (s) { return s.trim(); }).filter(Boolean);
                        return;
                    }
                    // обычные поля
                    updated[key] = (_a = formData.get(key)) !== null && _a !== void 0 ? _a : "";
                });
                // Обновляем rootCustomtItems
                if (!rootCustomtItems)
                    rootCustomtItems = {};
                rootCustomtItems[balancer] = __assign(__assign({}, rootCustomtItems[balancer]), updated);
                // Удаляем из rootCustomtItems[balancer] пустые поля и поля, совпадающие с rootDefaultItems
                if (rootCustomtItems[balancer]) {
                    var curr_1 = rootDefaultItems && rootDefaultItems[balancer] ? rootDefaultItems[balancer] : {};
                    Object.keys(rootCustomtItems[balancer]).forEach(function (key) {
                        var val = rootCustomtItems[balancer][key];
                        // Специальная проверка для vast: если vast.url пустой, удалить весь vast
                        if (key === "vast" &&
                            val &&
                            typeof val === "object" &&
                            ("url" in val) &&
                            (val.url === undefined || val.url === null || val.url === "")) {
                            delete rootCustomtItems[balancer][key];
                            return;
                        }
                        // Удаляем пустые строки, пустые массивы, пустые объекты
                        if (val === "" ||
                            (Array.isArray(val) && (val.length === 0 || (val.length === 1 && val[0] === ""))) ||
                            (typeof val === "object" && val !== null && !Array.isArray(val) && Object.keys(val).length === 0)) {
                            delete rootCustomtItems[balancer][key];
                            return;
                        }
                        // Удаляем если совпадает с rootDefaultItems
                        if (typeof val === "object" && val !== null && !Array.isArray(val) && curr_1[key] &&
                            JSON.stringify(val) === JSON.stringify(curr_1[key])) {
                            delete rootCustomtItems[balancer][key];
                            return;
                        }
                        if (Array.isArray(val) && Array.isArray(curr_1[key]) &&
                            JSON.stringify(val) === JSON.stringify(curr_1[key])) {
                            delete rootCustomtItems[balancer][key];
                            return;
                        }
                        if ((typeof val === "string" || typeof val === "number" || typeof val === "boolean") &&
                            val === curr_1[key]) {
                            delete rootCustomtItems[balancer][key];
                            return;
                        }
                    });
                    // Если после очистки не осталось полей — удаляем весь объект
                    if (Object.keys(rootCustomtItems[balancer]).length === 0) {
                        delete rootCustomtItems[balancer];
                    }
                }
                // Отправляем на сервер
                saveCustomtItems();
            };
        }
    }
    // Рендерим страницу после загрузки данных и формирования balancers
    loadCustomAndCurrent(container, function () {
        // Формируем HTML для бокового меню динамически
        var sidebarHtml = "\n            <div class=\"list-group\" id=\"balancer-sidebar\">\n                ".concat(balancers.map(function (balancer, idx) { return "\n                    <a href=\"#\" class=\"list-group-item list-group-item-action".concat(idx === 0 ? ' active' : '', "\" data-balancer=\"").concat(balancer, "\">").concat(balancer, "</a>\n                "); }).join(''), "\n            </div>\n        ");
        // Добавляем кастомный стиль для активного пункта
        var styleId = 'online-balancer-active-style';
        if (!document.getElementById(styleId)) {
            var style = document.createElement('style');
            style.id = styleId;
            style.innerHTML = "\n                #balancer-sidebar .list-group-item.active {\n                    background: linear-gradient(90deg, #0d6efd 60%, #3a8bfd 100%);\n                    color: #fff;\n                    font-weight: bold;\n                    border-color: #0d6efd;\n                    box-shadow: 0 2px 8px rgba(13,110,253,0.10);\n                }\n                #balancer-sidebar .list-group-item.active:focus, \n                #balancer-sidebar .list-group-item.active:hover {\n                    background: linear-gradient(90deg, #0b5ed7 60%, #2563eb 100%);\n                    color: #fff;\n                }\n            ";
            document.head.appendChild(style);
        }
        container.innerHTML = "\n            <div class=\"row\">\n                <div class=\"col-12 col-md-2 mb-3 mb-md-0\">\n                    ".concat(sidebarHtml, "\n                </div>\n                <div class=\"col-12 col-md-10\" id=\"online-main-content\" style=\"margin-bottom: 1.2em; background-color: #fdfdfd; padding: 10px; border-radius: 8px; box-shadow: 0 18px 24px rgb(175 175 175 / 30%);\"></div>\n            </div>\n        ");
        // По умолчанию показываем первый balancer, если есть
        if (balancers.length > 0) {
            renderBalancerForm(balancers[0]);
        }
        // Навешиваем обработчик на меню после рендера
        setTimeout(function () {
            var sidebar = document.getElementById('balancer-sidebar');
            if (sidebar) {
                sidebar.querySelectorAll('a[data-balancer]').forEach(function (link) {
                    link.addEventListener('click', function (e) {
                        e.preventDefault();
                        // Снимаем активный класс со всех
                        sidebar.querySelectorAll('.list-group-item').forEach(function (item) { return item.classList.remove('active'); });
                        // Добавляем активный класс текущему
                        e.currentTarget.classList.add('active');
                        var balancer = e.currentTarget.getAttribute('data-balancer');
                        if (!balancer)
                            return;
                        renderBalancerForm(balancer);
                    });
                });
            }
        }, 0);
    });
}
var proxyDefaults = {
    name: '',
    pattern: '',
    useAuth: false,
    BypassOnLocal: false,
    username: '',
    password: '',
    pattern_auth: '',
    maxRequestError: 2,
    file: '',
    url: '',
    list: [],
    refresh_uri: ''
};
function getProxyModalHtml(modalId, title, proxy, showDelete) {
    if (showDelete === void 0) { showDelete = false; }
    return "\n    <div class=\"modal fade\" id=\"".concat(modalId, "\" tabindex=\"-1\" aria-labelledby=\"").concat(modalId, "Label\" aria-hidden=\"true\">\n      <div class=\"modal-dialog modal-lg\">\n        <div class=\"modal-content\">\n          <div class=\"modal-header\">\n            <h5 class=\"modal-title\" id=\"").concat(modalId, "Label\">").concat(title, "</h5>\n            <button type=\"button\" class=\"btn-close\" data-bs-dismiss=\"modal\" aria-label=\"\u0417\u0430\u043A\u0440\u044B\u0442\u044C\"></button>\n          </div>\n          <div class=\"modal-body\">\n            <form id=\"").concat(modalId, "-form\">\n              <div class=\"mb-3\">\n                <label for=\"").concat(modalId, "-proxy-name\" class=\"form-label\">\u0418\u043C\u044F \u0434\u043B\u044F \u0434\u043E\u0441\u0442\u0443\u043F\u0430 \u0447\u0435\u0440\u0435\u0437 - globalnameproxy</label>\n                <input type=\"text\" class=\"form-control\" id=\"").concat(modalId, "-proxy-name\" value=\"").concat(proxy ? (proxy.name || '') : '', "\" placeholder=\"tor\" required>\n              </div>\n              <div class=\"mb-3\">\n                <label for=\"").concat(modalId, "-proxy-pattern\" class=\"form-label\">\u0418\u0441\u043F\u043E\u043B\u044C\u0437\u043E\u0432\u0430\u0442\u044C \u043F\u0440\u043E\u043A\u0441\u0438 \u0441 \u043F\u043E\u0434\u0445\u043E\u0434\u044F\u0449\u0438\u043C regex (\u043D\u0435 \u043E\u0431\u044F\u0437\u0430\u0442\u0435\u043B\u044C\u043D\u043E)</label>\n                <input type=\"text\" class=\"form-control\" id=\"").concat(modalId, "-proxy-pattern\" value=\"").concat(escapeHtmlAttr(proxy ? (proxy.pattern || '') : ''), "\" placeholder=\"\\\\.onion\">\n              </div>\n              <div class=\"mb-3 mt-5 d-flex align-items-center\">\n                <div class=\"form-check me-5\">\n                  <input class=\"form-check-input\" type=\"checkbox\" id=\"").concat(modalId, "-proxy-useAuth\" ").concat(proxy && proxy.useAuth ? 'checked' : '', ">\n                  <label class=\"form-check-label\" for=\"").concat(modalId, "-proxy-useAuth\">\u0418\u0441\u043F\u043E\u043B\u044C\u0437\u043E\u0432\u0430\u0442\u044C \u0430\u0432\u0442\u043E\u0440\u0438\u0437\u0430\u0446\u0438\u044E</label>\n                </div>\n                <div class=\"form-check\">\n                  <input class=\"form-check-input\" type=\"checkbox\" id=\"").concat(modalId, "-proxy-BypassOnLocal\" ").concat(proxy && proxy.BypassOnLocal ? 'checked' : '', ">\n                  <label class=\"form-check-label\" for=\"").concat(modalId, "-proxy-BypassOnLocal\">\u0418\u0433\u043D\u043E\u0440\u0438\u0440\u043E\u0432\u0430\u0442\u044C localhost</label>\n                </div>\n              </div>\n              <div class=\"mb-3\">\n                <label for=\"").concat(modalId, "-proxy-username\" class=\"form-label\">username</label>\n                <input type=\"text\" class=\"form-control\" id=\"").concat(modalId, "-proxy-username\" value=\"").concat(proxy ? (proxy.username || '') : '', "\" placeholder=\"\u043D\u0435 \u043E\u0431\u044F\u0437\u0430\u0442\u0435\u043B\u044C\u043D\u043E\">\n              </div>\n              <div class=\"mb-3\">\n                <label for=\"").concat(modalId, "-proxy-password\" class=\"form-label\">password</label>\n                <input type=\"text\" class=\"form-control\" id=\"").concat(modalId, "-proxy-password\" value=\"").concat(proxy ? (proxy.password || '') : '', "\" placeholder=\"\u043D\u0435 \u043E\u0431\u044F\u0437\u0430\u0442\u0435\u043B\u044C\u043D\u043E\">\n              </div>\n              <div class=\"mb-3\">\n                <label for=\"").concat(modalId, "-proxy-pattern_auth\" class=\"form-label\">pattern_auth</label>\n                <input type=\"text\" class=\"form-control\" id=\"").concat(modalId, "-proxy-pattern_auth\" value=\"").concat(escapeHtmlAttr(proxy ? (proxy.pattern_auth || '') : ''), "\" placeholder=\"^(?<sheme>[^/]+//)?(?<username>[^:/]+):(?<password>[^@]+)@(?<host>.*)\">\n              </div>\n              <div class=\"mb-3 mt-5\">\n                <label for=\"").concat(modalId, "-proxy-list\" class=\"form-label\">\u0421\u043F\u0438\u0441\u043E\u043A (\u0447\u0435\u0440\u0435\u0437 \u0437\u0430\u043F\u044F\u0442\u0443\u044E)</label>\n                <input type=\"text\" class=\"form-control\" id=\"").concat(modalId, "-proxy-list\" value=\"").concat(proxy && Array.isArray(proxy.list) ? proxy.list.join(', ') : '', "\" placeholder=\"socks5://127.0.0.1:9050, http://127.0.0.1:5481\">\n              </div>\n              <div class=\"mb-3\">\n                <label for=\"").concat(modalId, "-proxy-file\" class=\"form-label\">\u0424\u0430\u0439\u043B \u0441\u043F\u0438\u0441\u043A\u043E\u043C</label>\n                <input type=\"text\" class=\"form-control\" id=\"").concat(modalId, "-proxy-file\" value=\"").concat(proxy ? (proxy.file || '') : '', "\" placeholder=\"myproxy/pl.txt\">\n              </div>\n              <div class=\"mb-3\">\n                <label for=\"").concat(modalId, "-proxy-url\" class=\"form-label\">URL \u043D\u0430 \u0441\u043F\u0438\u0441\u043E\u043A</label>\n                <input type=\"text\" class=\"form-control\" id=\"").concat(modalId, "-proxy-url\" value=\"").concat(proxy ? (proxy.url || '') : '', "\" placeholder=\"https://asocks-list.org/userid.txt?type=res&country=UA\">\n              </div>\n              <div class=\"mb-3\">\n                <label for=\"").concat(modalId, "-proxy-refresh_uri\" class=\"form-label\">Refresh URI</label>\n                <input type=\"text\" class=\"form-control\" id=\"").concat(modalId, "-proxy-refresh_uri\" value=\"").concat(proxy ? (proxy.refresh_uri || '') : '', "\" placeholder=\"http://example.com/refresh\">\n              </div>\n              <div class=\"mb-3\">\n                <label for=\"").concat(modalId, "-proxy-maxRequestError\" class=\"form-label\">\u041A\u043E\u043B\u0438\u0447\u0435\u0441\u0442\u0432\u043E \u043E\u0448\u0438\u0431\u043E\u043A \u043F\u043E\u0434\u0440\u044F\u0434 \u0434\u043B\u044F \u0441\u043C\u0435\u043D\u044B \u043F\u0440\u043E\u043A\u0441\u0438</label>\n                <input type=\"number\" class=\"form-control\" id=\"").concat(modalId, "-proxy-maxRequestError\" value=\"").concat(proxy ? proxy.maxRequestError : 2, "\">\n              </div>\n            </form>\n          </div>\n          <div class=\"modal-footer d-flex justify-content-between\">\n            ").concat(showDelete ? "<button type=\"button\" class=\"btn btn-danger\" id=\"".concat(modalId, "-delete-btn\">\u0423\u0434\u0430\u043B\u0438\u0442\u044C</button>") : '<span></span>', "\n            <div>\n              <button type=\"button\" class=\"btn btn-secondary me-2\" data-bs-dismiss=\"modal\">\u0417\u0430\u043A\u0440\u044B\u0442\u044C</button>\n              <button type=\"button\" class=\"btn btn-primary\" id=\"").concat(modalId, "-save-btn\">\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C</button>\n            </div>\n          </div>\n        </div>\n      </div>\n    </div>\n    ");
}
function renderProxiesPage(containerId) {
    var container = document.getElementById(containerId);
    if (!container)
        return;
    var addModalHtml = getProxyModalHtml('addProxyModal', 'Добавить прокси');
    container.innerHTML = "\n        ".concat(addModalHtml, "\n        <div id=\"edit-proxy-modal-container\"></div>\n        <div class=\"d-flex justify-content-between align-items-center mb-3\">\n            <h1 class=\"mb-0\">\u041F\u0440\u043E\u043A\u0441\u0438</h1>\n            <div>\n                <button type=\"button\" class=\"btn btn-success\" id=\"btn-add-proxy\">\u0414\u043E\u0431\u0430\u0432\u0438\u0442\u044C \u043F\u0440\u043E\u043A\u0441\u0438</button>\n                <button type=\"button\" class=\"btn btn-primary\" id=\"btn-save-proxies\" style=\"display: none;\">\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C</button>\n            </div>\n        </div>\n        <div id=\"proxies-list\" class=\"row g-3\"></div>\n    ");
    loadAndRenderProxies('proxies-list');
    setTimeout(function () {
        var btnSave = document.getElementById('btn-save-proxies');
        if (btnSave) {
            btnSave.onclick = function () {
                if (rootCustomtItems) {
                    saveCustomtItems();
                }
                else {
                    alert('Данные не загружены');
                }
            };
        }
        var btnAdd = document.getElementById('btn-add-proxy');
        if (btnAdd) {
            btnAdd.onclick = function () {
                // @ts-ignore
                var modal = new bootstrap.Modal(document.getElementById('addProxyModal'));
                modal.show();
            };
        }
        var saveProxyBtn = document.getElementById('addProxyModal-save-btn');
        if (saveProxyBtn) {
            saveProxyBtn.onclick = function () {
                var name = document.getElementById('addProxyModal-proxy-name').value.trim();
                var pattern = document.getElementById('addProxyModal-proxy-pattern').value.trim();
                var useAuth = document.getElementById('addProxyModal-proxy-useAuth').checked;
                var BypassOnLocal = document.getElementById('addProxyModal-proxy-BypassOnLocal').checked;
                var username = document.getElementById('addProxyModal-proxy-username').value.trim();
                var password = document.getElementById('addProxyModal-proxy-password').value.trim();
                var pattern_auth = document.getElementById('addProxyModal-proxy-pattern_auth').value.trim();
                var maxRequestError = parseInt(document.getElementById('addProxyModal-proxy-maxRequestError').value, 10) || 0;
                var file = document.getElementById('addProxyModal-proxy-file').value.trim();
                var url = document.getElementById('addProxyModal-proxy-url').value.trim();
                var listRaw = document.getElementById('addProxyModal-proxy-list').value.trim();
                var refresh_uri = document.getElementById('addProxyModal-proxy-refresh_uri').value.trim();
                if (!name && !pattern) {
                    alert('Заполните обязательное поле "Имя" или "pattern"');
                    return;
                }
                var list = listRaw ? listRaw.split(',').map(function (s) { return s.trim(); }).filter(Boolean) : [];
                var newProxy = {
                    name: name,
                    pattern: pattern,
                    useAuth: useAuth,
                    BypassOnLocal: BypassOnLocal,
                    username: username,
                    password: password,
                    pattern_auth: pattern_auth,
                    maxRequestError: maxRequestError,
                    file: file,
                    url: url,
                    list: list,
                    refresh_uri: refresh_uri
                };
                // Удаление пустых и дефолтных значений из proxy
                Object.keys(proxyDefaults).forEach(function (key) {
                    var value = newProxy[key];
                    var def = proxyDefaults[key];
                    if (value === undefined ||
                        value === null ||
                        (typeof def === 'string' && (value === '' || value === def)) ||
                        (typeof def === 'number' && value === def) ||
                        (typeof def === 'boolean' && value === def) ||
                        (Array.isArray(def) && Array.isArray(value) && value.length === 0)) {
                        delete newProxy[key];
                    }
                });
                if (rootCustomtItems && rootCustomtItems["globalproxy"] && Array.isArray(rootCustomtItems["globalproxy"])) {
                    rootCustomtItems["globalproxy"].push(newProxy);
                    var proxiesList = document.getElementById('proxies-list');
                    if (proxiesList) {
                        proxiesList.innerHTML = renderProxies(rootCustomtItems["globalproxy"]);
                        attachEditProxyHandlers(rootCustomtItems["globalproxy"]);
                    }
                }
                // @ts-ignore
                var modal = bootstrap.Modal.getInstance(document.getElementById('addProxyModal'));
                if (modal)
                    modal.hide();
                var btnSave = document.getElementById('btn-save-proxies');
                if (btnSave) {
                    btnSave.click();
                }
            };
        }
        if (rootCustomtItems && rootCustomtItems["globalproxy"] && Array.isArray(rootCustomtItems["globalproxy"])) {
            attachEditProxyHandlers(rootCustomtItems["globalproxy"]);
        }
    }, 0);
}
function attachEditProxyHandlers(proxies) {
    proxies.forEach(function (proxy, idx) {
        var btn = document.getElementById("edit-proxy-btn-".concat(idx));
        if (btn) {
            btn.onclick = function () {
                var editModalId = "editProxyModal-".concat(idx);
                var editModalHtml = getProxyModalHtml(editModalId, 'Редактировать прокси', proxy, true);
                var editModalContainer = document.getElementById('edit-proxy-modal-container');
                if (editModalContainer) {
                    editModalContainer.innerHTML = editModalHtml;
                }
                // @ts-ignore
                var modal = new bootstrap.Modal(document.getElementById(editModalId));
                modal.show();
                setTimeout(function () {
                    var saveBtn = document.getElementById("".concat(editModalId, "-save-btn"));
                    if (saveBtn) {
                        saveBtn.onclick = function () {
                            var name = document.getElementById("".concat(editModalId, "-proxy-name")).value.trim();
                            var pattern = document.getElementById("".concat(editModalId, "-proxy-pattern")).value.trim();
                            var useAuth = document.getElementById("".concat(editModalId, "-proxy-useAuth")).checked;
                            var BypassOnLocal = document.getElementById("".concat(editModalId, "-proxy-BypassOnLocal")).checked;
                            var username = document.getElementById("".concat(editModalId, "-proxy-username")).value.trim();
                            var password = document.getElementById("".concat(editModalId, "-proxy-password")).value.trim();
                            var pattern_auth = document.getElementById("".concat(editModalId, "-proxy-pattern_auth")).value.trim();
                            var maxRequestError = parseInt(document.getElementById("".concat(editModalId, "-proxy-maxRequestError")).value, 10) || 0;
                            var file = document.getElementById("".concat(editModalId, "-proxy-file")).value.trim();
                            var url = document.getElementById("".concat(editModalId, "-proxy-url")).value.trim();
                            var listRaw = document.getElementById("".concat(editModalId, "-proxy-list")).value.trim();
                            var refresh_uri = document.getElementById("".concat(editModalId, "-proxy-refresh_uri")).value.trim();
                            if (!name && !pattern) {
                                alert('Заполните обязательное поле "Имя" или "pattern"');
                                return;
                            }
                            var list = listRaw ? listRaw.split(',').map(function (s) { return s.trim(); }).filter(Boolean) : [];
                            proxy.name = name;
                            proxy.pattern = pattern;
                            proxy.useAuth = useAuth;
                            proxy.BypassOnLocal = BypassOnLocal;
                            proxy.username = username;
                            proxy.password = password;
                            proxy.pattern_auth = pattern_auth;
                            proxy.maxRequestError = maxRequestError;
                            proxy.file = file;
                            proxy.url = url;
                            proxy.list = list;
                            proxy.refresh_uri = refresh_uri;
                            // Удаление пустых и дефолтных значений из proxy
                            Object.keys(proxyDefaults).forEach(function (key) {
                                var value = proxy[key];
                                var def = proxyDefaults[key];
                                if (value === undefined ||
                                    value === null ||
                                    (typeof def === 'string' && (value === '' || value === def)) ||
                                    (typeof def === 'number' && value === def) ||
                                    (typeof def === 'boolean' && value === def) ||
                                    (Array.isArray(def) && Array.isArray(value) && value.length === 0)) {
                                    delete proxy[key];
                                }
                            });
                            var proxiesList = document.getElementById('proxies-list');
                            if (proxiesList) {
                                proxiesList.innerHTML = renderProxies(rootCustomtItems["globalproxy"]);
                                attachEditProxyHandlers(rootCustomtItems["globalproxy"]);
                            }
                            // @ts-ignore
                            var modal = bootstrap.Modal.getInstance(document.getElementById(editModalId));
                            if (modal)
                                modal.hide();
                            var btnSave = document.getElementById('btn-save-proxies');
                            if (btnSave) {
                                btnSave.click();
                            }
                        };
                    }
                    var deleteBtn = document.getElementById("".concat(editModalId, "-delete-btn"));
                    if (deleteBtn) {
                        deleteBtn.onclick = function () {
                            if (confirm('Удалить прокси?')) {
                                if (rootCustomtItems && rootCustomtItems["globalproxy"] && Array.isArray(rootCustomtItems["globalproxy"])) {
                                    var proxiesArr = rootCustomtItems["globalproxy"];
                                    var proxyIdx = proxiesArr.indexOf(proxy);
                                    if (proxyIdx !== -1) {
                                        proxiesArr.splice(proxyIdx, 1);
                                        var proxiesList = document.getElementById('proxies-list');
                                        if (proxiesList) {
                                            proxiesList.innerHTML = renderProxies(proxiesArr);
                                            attachEditProxyHandlers(proxiesArr);
                                        }
                                        // @ts-ignore
                                        var modal_2 = bootstrap.Modal.getInstance(document.getElementById(editModalId));
                                        if (modal_2)
                                            modal_2.hide();
                                        var btnSave = document.getElementById('btn-save-proxies');
                                        if (btnSave) {
                                            btnSave.click();
                                        }
                                    }
                                }
                            }
                        };
                    }
                }, 0);
            };
        }
    });
}
function loadAndRenderProxies(containerId) {
    var container = document.getElementById(containerId);
    if (!container)
        return;
    fetch('/admin/init/custom')
        .then(function (res) { return res.json(); })
        .then(function (ob) {
        rootCustomtItems = ob;
        return fetch('/admin/init/current');
    })
        .then(function (res) { return res.json(); })
        .then(function (ob) {
        if (!rootCustomtItems.globalproxy)
            rootCustomtItems.globalproxy = [];
        rootCustomtItems.globalproxy = Array.isArray(ob.globalproxy) ? ob.globalproxy : [];
        container.innerHTML = renderProxies(rootCustomtItems.globalproxy);
        setTimeout(function () {
            if (rootCustomtItems && rootCustomtItems.globalproxy && Array.isArray(rootCustomtItems.globalproxy)) {
                attachEditProxyHandlers(rootCustomtItems.globalproxy);
            }
        }, 0);
    })
        .catch(function () {
        container.innerHTML = '<div class="alert alert-danger">Ошибка загрузки прокси</div>';
    });
}
function renderProxies(proxies) {
    return "\n        <table class=\"table table-bordered table-striped align-middle\">\n            <thead>\n                <tr>\n                    <th style=\"width:48px; text-align:center;\"></th>\n                    <th>\u0418\u043C\u044F</th>\n                    <th>Pattern</th>\n                    <th>auth</th>\n                    <th>list</th>\n                    <th>url</th>\n                    <th>file</th>\n                    <th>refresh_uri</th>\n                </tr>\n            </thead>\n            <tbody>\n                ".concat(proxies.map(function (proxy, idx) {
        return "\n                        <tr>\n                            <td style=\"width:48px; text-align:center;\">\n                                <button type=\"button\" class=\"btn btn-sm btn-light p-1\" id=\"edit-proxy-btn-".concat(idx, "\" title=\"\u0420\u0435\u0434\u0430\u043A\u0442\u0438\u0440\u043E\u0432\u0430\u0442\u044C\" style=\"width:32px; height:32px;\">\n                                    <svg xmlns=\"http://www.w3.org/2000/svg\" width=\"18\" height=\"18\" fill=\"currentColor\" class=\"bi bi-gear\" viewBox=\"0 0 16 16\">\n                                      <path d=\"M8 4.754a3.246 3.246 0 1 0 0 6.492 3.246 3.246 0 0 0 0-6.492zM5.754 8a2.246 2.246 0 1 1 4.492 0 2.246 2.246 0 0 1-4.492 0z\"/>\n                                      <path d=\"M9.796 1.343c-.527-1.79-3.065-1.79-3.592 0l-.094.319a.873.873 0 0 1-1.255.52l-.292-.16c-1.64-.892-3.433.902-2.54 2.541l.159.292a.873.873 0 0 1-.52 1.255l-.319.094c-1.79.527-1.79 3.065 0 3.592l.319.094a.873.873 0 0 1 .52 1.255l-.16.292c-.892 1.64.901 3.434 2.541 2.54l.292-.159a.873.873 0 0 1 1.255.52l.094.319c.527 1.79 3.065 1.79 3.592 0l.094-.319a.873.873 0 0 1 1.255-.52l.292.16c1.64.893 3.434-.902 2.54-2.541l-.159-.292a.873.873 0 0 1 .52-1.255l.319-.094c1.79-.527 1.79-3.065 0-3.592l-.319-.094a.873.873 0 0 1-.52-1.255l.16-.292c.893-1.64-.902-3.433-2.541-2.54l-.292.159a.873.873 0 0 1-1.255-.52l-.094-.319zm-2.633.283c.246-.835 1.428-.835 1.674 0l.094.319a1.873 1.873 0 0 0 2.693 1.115l.291-.16c.764-.415 1.6.42 1.184 1.185l-.159.292a1.873 1.873 0 0 0 1.116 2.692l.318.094c.835.246.835 1.428 0 1.674l-.319.094a1.873 1.873 0 0 0-1.115 2.693l.16.291c.415.764-.42 1.6-1.185 1.184l-.291-.159a1.873 1.873 0 0 0-2.693 1.116l-.094.318c-.246.835-1.428.835-1.674 0l-.094-.319a1.873 1.873 0 0 0-2.692-1.115l-.292.16c-.764.415-1.6-.42-1.184-1.185l.159-.291A1.873 1.873 0 0 0 1.945 8.93l-.319-.094c-.835-.246-.835-1.428 0-1.674l.319-.094A1.873 1.873 0 0 0 3.06 4.377l-.16-.292c-.415-.764.42-1.6 1.185-1.184l.292.159a1.873 1.873 0 0 0 2.692-1.115l.094-.319z\"/>\n                                    </svg>\n                                </button>\n                            </td>\n                            <td>").concat(proxy.name || '', "</td>\n                            <td>").concat(escapeHtmlAttr(proxy.pattern || ''), "</td>\n                            <td>").concat(proxy.useAuth ? 'Да' : 'Нет', "</td>\n                            <td>").concat(Array.isArray(proxy.list) ? proxy.list.join('<br>') : '', "</td>\n                            <td>").concat(proxy.url || '', "</td>\n                            <td>").concat(proxy.file || '', "</td>\n                            <td>").concat(proxy.refresh_uri || '', "</td>\n                        </tr>\n                    ");
    }).join(''), "\n            </tbody>\n        </table>\n    ");
}
function loadCustomAndDefault(container, onLoaded) {
    var loaded = 0;
    var hasError = false;
    function checkLoaded() {
        loaded++;
        if (loaded === 2 && !hasError && onLoaded) {
            onLoaded();
        }
    }
    fetch('/admin/init/custom')
        .then(function (res) { return res.json(); })
        .then(function (data) {
        rootCustomtItems = data;
        checkLoaded();
    })
        .catch(function () {
        hasError = true;
        if (container) {
            container.innerHTML = "<div class=\"alert alert-danger\">\u041E\u0448\u0438\u0431\u043A\u0430 \u0437\u0430\u0433\u0440\u0443\u0437\u043A\u0438 customItems</div>";
        }
    });
    checkLoaded();
}
function renderBaseForm() {
    var _a, _b, _c, _d, _e, _f, _g, _h, _j, _k, _l, _m, _o, _p, _q, _r, _s, _t, _u, _v, _w, _x, _y, _z, _0, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10, _11, _12;
    var BASE_FIXED_VALUES = {
        "multiaccess": (_b = (_a = rootCustomtItems === null || rootCustomtItems === void 0 ? void 0 : rootCustomtItems.multiaccess) !== null && _a !== void 0 ? _a : rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.multiaccess) !== null && _b !== void 0 ? _b : false,
        "mikrotik": (_d = (_c = rootCustomtItems === null || rootCustomtItems === void 0 ? void 0 : rootCustomtItems.mikrotik) !== null && _c !== void 0 ? _c : rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.mikrotik) !== null && _d !== void 0 ? _d : false,
        "typecache": (_e = rootCustomtItems === null || rootCustomtItems === void 0 ? void 0 : rootCustomtItems.typecache) !== null && _e !== void 0 ? _e : "",
        "imagelibrary": (_f = rootCustomtItems === null || rootCustomtItems === void 0 ? void 0 : rootCustomtItems.imagelibrary) !== null && _f !== void 0 ? _f : "",
        "pirate_store": (_h = (_g = rootCustomtItems === null || rootCustomtItems === void 0 ? void 0 : rootCustomtItems.pirate_store) !== null && _g !== void 0 ? _g : rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.pirate_store) !== null && _h !== void 0 ? _h : true,
        "apikey": (_j = rootCustomtItems === null || rootCustomtItems === void 0 ? void 0 : rootCustomtItems.apikey) !== null && _j !== void 0 ? _j : "",
        "litejac": (_l = (_k = rootCustomtItems === null || rootCustomtItems === void 0 ? void 0 : rootCustomtItems.litejac) !== null && _k !== void 0 ? _k : rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.litejac) !== null && _l !== void 0 ? _l : true,
        "filelog": (_o = (_m = rootCustomtItems === null || rootCustomtItems === void 0 ? void 0 : rootCustomtItems.filelog) !== null && _m !== void 0 ? _m : rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.filelog) !== null && _o !== void 0 ? _o : false,
        "disableEng": (_q = (_p = rootCustomtItems === null || rootCustomtItems === void 0 ? void 0 : rootCustomtItems.disableEng) !== null && _p !== void 0 ? _p : rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.disableEng) !== null && _q !== void 0 ? _q : false,
        "anticaptchakey": (_r = rootCustomtItems === null || rootCustomtItems === void 0 ? void 0 : rootCustomtItems.anticaptchakey) !== null && _r !== void 0 ? _r : "",
        "omdbapi_key": (_s = rootCustomtItems === null || rootCustomtItems === void 0 ? void 0 : rootCustomtItems.omdbapi_key) !== null && _s !== void 0 ? _s : "",
        "playerInner": (_t = rootCustomtItems === null || rootCustomtItems === void 0 ? void 0 : rootCustomtItems.playerInner) !== null && _t !== void 0 ? _t : "",
        "defaultOn": (_u = rootCustomtItems === null || rootCustomtItems === void 0 ? void 0 : rootCustomtItems.defaultOn) !== null && _u !== void 0 ? _u : "enable",
        "real_ip_cf": (_w = (_v = rootCustomtItems === null || rootCustomtItems === void 0 ? void 0 : rootCustomtItems.real_ip_cf) !== null && _v !== void 0 ? _v : rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.real_ip_cf) !== null && _w !== void 0 ? _w : false,
        "corsehost": (_x = rootCustomtItems === null || rootCustomtItems === void 0 ? void 0 : rootCustomtItems.corsehost) !== null && _x !== void 0 ? _x : ""
    };
    var BASE_DEFAULT_VALUES = {
        "multiaccess": (_y = rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.multiaccess) !== null && _y !== void 0 ? _y : false,
        "mikrotik": (_z = rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.mikrotik) !== null && _z !== void 0 ? _z : false,
        "typecache": (_0 = rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.typecache) !== null && _0 !== void 0 ? _0 : "",
        "imagelibrary": (_1 = rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.imagelibrary) !== null && _1 !== void 0 ? _1 : "",
        "pirate_store": (_2 = rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.pirate_store) !== null && _2 !== void 0 ? _2 : true,
        "apikey": (_3 = rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.apikey) !== null && _3 !== void 0 ? _3 : "",
        "litejac": (_4 = rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.litejac) !== null && _4 !== void 0 ? _4 : true,
        "filelog": (_5 = rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.filelog) !== null && _5 !== void 0 ? _5 : false,
        "disableEng": (_6 = rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.disableEng) !== null && _6 !== void 0 ? _6 : false,
        "anticaptchakey": (_7 = rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.anticaptchakey) !== null && _7 !== void 0 ? _7 : "",
        "omdbapi_key": (_8 = rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.omdbapi_key) !== null && _8 !== void 0 ? _8 : "",
        "playerInner": (_9 = rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.playerInner) !== null && _9 !== void 0 ? _9 : "",
        "defaultOn": (_10 = rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.defaultOn) !== null && _10 !== void 0 ? _10 : "enable",
        "real_ip_cf": (_11 = rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.real_ip_cf) !== null && _11 !== void 0 ? _11 : false,
        "corsehost": (_12 = rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems.corsehost) !== null && _12 !== void 0 ? _12 : ""
    };
    var html = "<form id=\"base-form\">";
    Object.entries(BASE_FIXED_VALUES).forEach(function (_a) {
        var _b;
        var field = _a[0], defValue = _a[1];
        var defType = typeof defValue;
        var value = defValue;
        var placeholder = (_b = BASE_DEFAULT_VALUES[field]) !== null && _b !== void 0 ? _b : '';
        if (defType === 'boolean') {
            html += "\n                <div class=\"form-check mb-3\">\n                    <input class=\"form-check-input\" type=\"checkbox\" id=\"field-base-".concat(field, "\" name=\"base.").concat(field, "\" ").concat(value ? 'checked' : '', ">\n                    <label class=\"form-check-label\" for=\"field-base-").concat(field, "\">").concat(field, "</label>\n                </div>\n            ");
        }
        else if (defType === 'number') {
            html += "\n                <div class=\"mb-3\">\n                    <label class=\"form-label\" for=\"field-base-".concat(field, "\">").concat(field, "</label>\n                    <input type=\"number\" class=\"form-control\" id=\"field-base-").concat(field, "\" name=\"base.").concat(field, "\" value=\"").concat(value, "\" placeholder=\"").concat(placeholder, "\">\n                </div>\n            ");
        }
        else {
            html += "\n                <div class=\"mb-3\">\n                    <label class=\"form-label\" for=\"field-base-".concat(field, "\">").concat(field, "</label>\n                    <input type=\"text\" class=\"form-control\" id=\"field-base-").concat(field, "\" name=\"base.").concat(field, "\" value=\"").concat(value, "\" placeholder=\"").concat(placeholder, "\">\n                </div>\n            ");
        }
    });
    html += "<button type=\"button\" class=\"btn btn-primary\" id=\"base-save-btn\">\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C</button></form>";
    var mainContent = document.getElementById('other-main-content');
    if (mainContent)
        mainContent.innerHTML = html;
    var saveBtn = document.getElementById('base-save-btn');
    if (saveBtn) {
        saveBtn.onclick = function () {
            var form = document.getElementById('base-form');
            if (!form)
                return;
            var formData = new FormData(form);
            var updated = {};
            Object.entries(BASE_FIXED_VALUES).forEach(function (_a) {
                var field = _a[0], defValue = _a[1];
                var defType = typeof defValue;
                var val = formData.get("base.".concat(field));
                if (defType === 'boolean') {
                    var el = form.querySelector("[name=\"base.".concat(field, "\"]"));
                    val = el ? el.checked : false;
                }
                else if (defType === 'number') {
                    val = val !== null && val !== undefined && val !== '' ? parseInt(val, 10) : defValue;
                }
                else {
                    val = val !== null && val !== void 0 ? val : '';
                }
                // Удаляем пустые значения и значения, совпадающие с rootDefaultItems
                var defaultVal = rootDefaultItems === null || rootDefaultItems === void 0 ? void 0 : rootDefaultItems[field];
                var isEmpty = (defType === 'string' && val === '') ||
                    (defType === 'number' && (val === '' || isNaN(val)));
                var isDefault = defaultVal !== undefined && val === defaultVal;
                if (!isEmpty && !isDefault) {
                    updated[field] = val;
                }
            });
            // Обновляем rootCustomtItems без вложенности "base"
            // Удаляем старые base-поля
            Object.keys(BASE_FIXED_VALUES).forEach(function (field) {
                if (field in rootCustomtItems) {
                    delete rootCustomtItems[field];
                }
            });
            // Добавляем новые значения
            rootCustomtItems = __assign(__assign({}, rootCustomtItems), updated);
            saveCustomtItems();
        };
    }
}
function renderOnterModalContext(html, custom, def) {
    var _a, _b, _c, _d, _e, _f, _g;
    html += "\n        <button type=\"button\" class=\"btn btn-secondary mb-2 mt-3\" id=\"context-modal-btn\">context</button>\n        <div class=\"modal\" tabindex=\"-1\" id=\"context-modal\" style=\"display:none; background:rgba(0,0,0,0.3); position:fixed; top:0; left:0; width:100vw; height:100vh; z-index:1050;\">\n            <div class=\"modal-dialog\" style=\"margin:10vh auto; max-width:400px;\">\n                <div class=\"modal-content\">\n                    <div class=\"modal-header\">\n                        <h5 class=\"modal-title\">\u0420\u0435\u0434\u0430\u043A\u0442\u0438\u0440\u043E\u0432\u0430\u0442\u044C context</h5>\n                        <button type=\"button\" class=\"btn-close\" id=\"context-modal-close\" aria-label=\"\u0417\u0430\u043A\u0440\u044B\u0442\u044C\"></button>\n                    </div>\n                    <div class=\"modal-body\">\n                        <form id=\"context-form\">\n                            <div class=\"form-check mb-3\">\n                                <input class=\"form-check-input\" type=\"checkbox\" id=\"context-keepopen\" name=\"keepopen\" ".concat(((_a = custom.keepopen) !== null && _a !== void 0 ? _a : def.keepopen) ? 'checked' : '', ">\n                                <label class=\"form-check-label\" for=\"context-keepopen\">keepopen</label>\n                            </div>\n                            <div class=\"mb-3\">\n                                <label class=\"form-label\" for=\"context-keepalive\">keepalive</label>\n                                <input type=\"number\" class=\"form-control\" id=\"context-keepalive\" name=\"keepalive\" value=\"").concat((_b = custom.keepalive) !== null && _b !== void 0 ? _b : '', "\" placeholder=\"").concat((_c = def.keepalive) !== null && _c !== void 0 ? _c : '', "\">\n                            </div>\n                            <div class=\"mb-3\">\n                                <label class=\"form-label\" for=\"context-min\">min</label>\n                                <input type=\"number\" class=\"form-control\" id=\"context-min\" name=\"min\" value=\"").concat((_d = custom.min) !== null && _d !== void 0 ? _d : '', "\" placeholder=\"").concat((_e = def.min) !== null && _e !== void 0 ? _e : '', "\">\n                            </div>\n                            <div class=\"mb-3\">\n                                <label class=\"form-label\" for=\"context-max\">max</label>\n                                <input type=\"number\" class=\"form-control\" id=\"context-max\" name=\"max\" value=\"").concat((_f = custom.max) !== null && _f !== void 0 ? _f : '', "\" placeholder=\"").concat((_g = def.max) !== null && _g !== void 0 ? _g : '', "\">\n                            </div>\n                        </form>\n                    </div>\n                    <div class=\"modal-footer\">\n                        <button type=\"button\" class=\"btn btn-secondary\" id=\"context-modal-cancel\">\u041E\u0442\u043C\u0435\u043D\u0430</button>\n                        <button type=\"button\" class=\"btn btn-primary\" id=\"context-modal-save\">\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C</button>\n                    </div>\n                </div>\n            </div>\n        </div>\n    ");
    return html;
}
function initOnterModalContext(key, def) {
    var modal = document.getElementById('context-modal');
    var openBtn = document.getElementById('context-modal-btn');
    var closeBtn = document.getElementById('context-modal-close');
    var cancelBtn = document.getElementById('context-modal-cancel');
    var saveBtn = document.getElementById('context-modal-save');
    var closeModal = function () {
        if (modal)
            modal.style.display = 'none';
    };
    var openModal = function () {
        if (modal)
            modal.style.display = 'block';
    };
    if (openBtn)
        openBtn.onclick = openModal;
    if (closeBtn)
        closeBtn.onclick = closeModal;
    if (cancelBtn)
        cancelBtn.onclick = closeModal;
    if (saveBtn) {
        saveBtn.onclick = function () {
            var form = document.getElementById('context-form');
            if (!form)
                return;
            var formData = new FormData(form);
            var updatedContext = {
                keepopen: (formData.get('keepopen') === 'on'),
                keepalive: parseInt(formData.get('keepalive'), 10) || 0,
                min: parseInt(formData.get('min'), 10) || 0,
                max: parseInt(formData.get('max'), 10) || 0
            };
            // Удаляем переменные с 0 или совпадающие с дефолтными
            Object.keys(updatedContext).forEach(function (key) {
                if (updatedContext[key] === 0 ||
                    (def && def[key] !== undefined && updatedContext[key] === def[key])) {
                    delete updatedContext[key];
                }
            });
            if (key === 'chromium') {
                rootCustomtItems = __assign(__assign({}, rootCustomtItems), { chromium: __assign(__assign({}, (rootCustomtItems.chromium || {})), { context: updatedContext }) });
            }
            else {
                rootCustomtItems = __assign(__assign({}, rootCustomtItems), { firefox: __assign(__assign({}, (rootCustomtItems.firefox || {})), { context: updatedContext }) });
            }
            closeModal();
            saveCustomtItems();
        };
    }
}
function renderOnterModalImage(html, custom, def) {
    var _a, _b, _c, _d, _e, _f;
    html += "\n        <button type=\"button\" class=\"btn btn-secondary mb-2 mt-3\" id=\"image-modal-btn\">image</button>\n        <div class=\"modal\" tabindex=\"-1\" id=\"image-modal\" style=\"display:none; background:rgba(0,0,0,0.3); position:fixed; top:0; left:0; width:100vw; height:100vh; z-index:1050;\">\n            <div class=\"modal-dialog\" style=\"margin:10vh auto; max-width:400px;\">\n                <div class=\"modal-content\">\n                    <div class=\"modal-header\">\n                        <h5 class=\"modal-title\">\u0420\u0435\u0434\u0430\u043A\u0442\u0438\u0440\u043E\u0432\u0430\u0442\u044C image</h5>\n                        <button type=\"button\" class=\"btn-close\" id=\"image-modal-close\" aria-label=\"\u0417\u0430\u043A\u0440\u044B\u0442\u044C\"></button>\n                    </div>\n                    <div class=\"modal-body\">\n                        <form id=\"image-form\">\n                            <div class=\"form-check mb-3\">\n                                <input class=\"form-check-input\" type=\"checkbox\" id=\"image-cache\" name=\"cache\" ".concat(((_a = custom.cache) !== null && _a !== void 0 ? _a : def.cache) ? 'checked' : '', ">\n                                <label class=\"form-check-label\" for=\"image-cache\">cache</label>\n                            </div>\n                            <div class=\"form-check mb-3\">\n                                <input class=\"form-check-input\" type=\"checkbox\" id=\"image-cache_rsize\" name=\"cache_rsize\" ").concat(((_b = custom.cache_rsize) !== null && _b !== void 0 ? _b : def.cache_rsize) ? 'checked' : '', ">\n                                <label class=\"form-check-label\" for=\"image-cache_rsize\">cache_rsize</label>\n                            </div>\n                            <div class=\"form-check mb-3\">\n                                <input class=\"form-check-input\" type=\"checkbox\" id=\"image-useproxy\" name=\"useproxy\" ").concat(((_c = custom.useproxy) !== null && _c !== void 0 ? _c : def.useproxy) ? 'checked' : '', ">\n                                <label class=\"form-check-label\" for=\"image-useproxy\">useproxy</label>\n                            </div>\n                            <div class=\"form-check mb-3\">\n                                <input class=\"form-check-input\" type=\"checkbox\" id=\"image-useproxystream\" name=\"useproxystream\" ").concat(((_d = custom.useproxystream) !== null && _d !== void 0 ? _d : def.useproxystream) ? 'checked' : '', ">\n                                <label class=\"form-check-label\" for=\"image-useproxystream\">useproxystream</label>\n                            </div>\n                            <div class=\"mb-3\">\n                                <label class=\"form-label\" for=\"image-globalnameproxy\">globalnameproxy</label>\n                                <input type=\"text\" class=\"form-control\" id=\"image-globalnameproxy\" name=\"globalnameproxy\" value=\"").concat((_e = custom.globalnameproxy) !== null && _e !== void 0 ? _e : '', "\" placeholder=\"").concat((_f = def.globalnameproxy) !== null && _f !== void 0 ? _f : '', "\">\n                            </div>\n                        </form>\n                    </div>\n                    <div class=\"modal-footer\">\n                        <button type=\"button\" class=\"btn btn-secondary\" id=\"image-modal-cancel\">\u041E\u0442\u043C\u0435\u043D\u0430</button>\n                        <button type=\"button\" class=\"btn btn-primary\" id=\"image-modal-save\">\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C</button>\n                    </div>\n                </div>\n            </div>\n        </div>\n    ");
    return html;
}
function initOnterModalImage(key, def) {
    var modal = document.getElementById('image-modal');
    var openBtn = document.getElementById('image-modal-btn');
    var closeBtn = document.getElementById('image-modal-close');
    var cancelBtn = document.getElementById('image-modal-cancel');
    var saveBtn = document.getElementById('image-modal-save');
    var closeModal = function () {
        if (modal)
            modal.style.display = 'none';
    };
    var openModal = function () {
        if (modal)
            modal.style.display = 'block';
    };
    if (openBtn)
        openBtn.onclick = openModal;
    if (closeBtn)
        closeBtn.onclick = closeModal;
    if (cancelBtn)
        cancelBtn.onclick = closeModal;
    if (saveBtn) {
        saveBtn.onclick = function () {
            var _a;
            var _b;
            var form = document.getElementById('image-form');
            if (!form)
                return;
            var formData = new FormData(form);
            var updatedImage = {
                cache: (formData.get('cache') === 'on'),
                cache_rsize: (formData.get('cache_rsize') === 'on'),
                useproxy: (formData.get('useproxy') === 'on'),
                useproxystream: (formData.get('useproxystream') === 'on'),
                globalnameproxy: (_b = formData.get('globalnameproxy')) !== null && _b !== void 0 ? _b : ''
            };
            // Удаляем переменные, совпадающие с дефолтными
            Object.keys(updatedImage).forEach(function (field) {
                if ((def && def[field] !== undefined && updatedImage[field] === def[field]) ||
                    (typeof updatedImage[field] === 'string' && updatedImage[field] === '')) {
                    delete updatedImage[field];
                }
            });
            rootCustomtItems = __assign(__assign({}, rootCustomtItems), (_a = {}, _a[key] = __assign(__assign({}, (rootCustomtItems[key] || {})), { image: updatedImage }), _a));
            closeModal();
            saveCustomtItems();
        };
    }
}
function renderOnterModalBuffering(html, custom, def) {
    var _a, _b, _c, _d, _e, _f, _g;
    html += "\n        <button type=\"button\" class=\"btn btn-secondary mb-2 mt-3\" id=\"buffering-modal-btn\">buffering</button>\n        <div class=\"modal\" tabindex=\"-1\" id=\"buffering-modal\" style=\"display:none; background:rgba(0,0,0,0.3); position:fixed; top:0; left:0; width:100vw; height:100vh; z-index:1050;\">\n            <div class=\"modal-dialog\" style=\"margin:10vh auto; max-width:400px;\">\n                <div class=\"modal-content\">\n                    <div class=\"modal-header\">\n                        <h5 class=\"modal-title\">\u0420\u0435\u0434\u0430\u043A\u0442\u0438\u0440\u043E\u0432\u0430\u0442\u044C buffering</h5>\n                        <button type=\"button\" class=\"btn-close\" id=\"buffering-modal-close\" aria-label=\"\u0417\u0430\u043A\u0440\u044B\u0442\u044C\"></button>\n                    </div>\n                    <div class=\"modal-body\">\n                        <form id=\"buffering-form\">\n                            <div class=\"form-check mb-3\">\n                                <input class=\"form-check-input\" type=\"checkbox\" id=\"buffering-enable\" name=\"enable\" ".concat(((_a = custom.enable) !== null && _a !== void 0 ? _a : def.enable) ? 'checked' : '', ">\n                                <label class=\"form-check-label\" for=\"buffering-enable\">enable</label>\n                            </div>\n                            <div class=\"mb-3\">\n                                <label class=\"form-label\" for=\"buffering-rent\">rent</label>\n                                <input type=\"number\" class=\"form-control\" id=\"buffering-rent\" name=\"rent\" value=\"").concat((_b = custom.rent) !== null && _b !== void 0 ? _b : '', "\" placeholder=\"").concat((_c = def.rent) !== null && _c !== void 0 ? _c : '', "\">\n                            </div>\n                            <div class=\"mb-3\">\n                                <label class=\"form-label\" for=\"buffering-length\">length</label>\n                                <input type=\"number\" class=\"form-control\" id=\"buffering-length\" name=\"length\" value=\"").concat((_d = custom.length) !== null && _d !== void 0 ? _d : '', "\" placeholder=\"").concat((_e = def.length) !== null && _e !== void 0 ? _e : '', "\">\n                            </div>\n                            <div class=\"mb-3\">\n                                <label class=\"form-label\" for=\"buffering-millisecondsTimeout\">millisecondsTimeout</label>\n                                <input type=\"number\" class=\"form-control\" id=\"buffering-millisecondsTimeout\" name=\"millisecondsTimeout\" value=\"").concat((_f = custom.millisecondsTimeout) !== null && _f !== void 0 ? _f : '', "\" placeholder=\"").concat((_g = def.millisecondsTimeout) !== null && _g !== void 0 ? _g : '', "\">\n                            </div>\n                        </form>\n                    </div>\n                    <div class=\"modal-footer\">\n                        <button type=\"button\" class=\"btn btn-secondary\" id=\"buffering-modal-cancel\">\u041E\u0442\u043C\u0435\u043D\u0430</button>\n                        <button type=\"button\" class=\"btn btn-primary\" id=\"buffering-modal-save\">\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C</button>\n                    </div>\n                </div>\n            </div>\n        </div>\n    ");
    return html;
}
function initOnterModalBuffering(key, def) {
    var modal = document.getElementById('buffering-modal');
    var openBtn = document.getElementById('buffering-modal-btn');
    var closeBtn = document.getElementById('buffering-modal-close');
    var cancelBtn = document.getElementById('buffering-modal-cancel');
    var saveBtn = document.getElementById('buffering-modal-save');
    var closeModal = function () {
        if (modal)
            modal.style.display = 'none';
    };
    var openModal = function () {
        if (modal)
            modal.style.display = 'block';
    };
    if (openBtn)
        openBtn.onclick = openModal;
    if (closeBtn)
        closeBtn.onclick = closeModal;
    if (cancelBtn)
        cancelBtn.onclick = closeModal;
    if (saveBtn) {
        saveBtn.onclick = function () {
            var _a;
            var form = document.getElementById('buffering-form');
            if (!form)
                return;
            var formData = new FormData(form);
            var updatedBuffering = {
                enable: (formData.get('enable') === 'on'),
                rent: parseInt(formData.get('rent'), 10) || 0,
                length: parseInt(formData.get('length'), 10) || 0,
                millisecondsTimeout: parseInt(formData.get('millisecondsTimeout'), 10) || 0
            };
            // Удаляем переменные с 0 или совпадающие с дефолтными
            Object.keys(updatedBuffering).forEach(function (field) {
                if (updatedBuffering[field] === 0 ||
                    (def && def[field] !== undefined && updatedBuffering[field] === def[field])) {
                    delete updatedBuffering[field];
                }
            });
            rootCustomtItems = __assign(__assign({}, rootCustomtItems), (_a = {}, _a[key] = __assign(__assign({}, (rootCustomtItems[key] || {})), { buffering: updatedBuffering }), _a));
            closeModal();
            saveCustomtItems();
        };
    }
}
function renderOnterModalDlna(html, custom, def) {
    var _a, _b, _c, _d, _e, _f, _g, _h, _j, _k, _l, _m, _o, _p, _q;
    html += "\n        <button type=\"button\" class=\"btn btn-secondary mb-2 mt-3\" id=\"dlna-modal-btn\">cover</button>\n        <div class=\"modal\" tabindex=\"-1\" id=\"dlna-modal\" style=\"display:none; background:rgba(0,0,0,0.3); position:fixed; top:0; left:0; width:100vw; height:100vh; z-index:1050;\">\n            <div class=\"modal-dialog\" style=\"margin:10vh auto; max-width:400px;\">\n                <div class=\"modal-content\">\n                    <div class=\"modal-header\">\n                        <h5 class=\"modal-title\">\u0420\u0435\u0434\u0430\u043A\u0442\u0438\u0440\u043E\u0432\u0430\u0442\u044C cover</h5>\n                        <button type=\"button\" class=\"btn-close\" id=\"dlna-modal-close\" aria-label=\"\u0417\u0430\u043A\u0440\u044B\u0442\u044C\"></button>\n                    </div>\n                    <div class=\"modal-body\">\n                        <form id=\"dlna-form\">\n                            <div class=\"form-check mb-3\">\n                                <input class=\"form-check-input\" type=\"checkbox\" id=\"dlna-enable\" name=\"enable\" ".concat(((_a = custom.enable) !== null && _a !== void 0 ? _a : def.enable) ? 'checked' : '', ">\n                                <label class=\"form-check-label\" for=\"dlna-enable\">enable</label>\n                            </div>\n                            <div class=\"form-check mb-3\">\n                                <input class=\"form-check-input\" type=\"checkbox\" id=\"dlna-consoleLog\" name=\"consoleLog\" ").concat(((_b = custom.consoleLog) !== null && _b !== void 0 ? _b : def.consoleLog) ? 'checked' : '', ">\n                                <label class=\"form-check-label\" for=\"dlna-consoleLog\">consoleLog</label>\n                            </div>\n                            <div class=\"form-check mb-3\">\n                                <input class=\"form-check-input\" type=\"checkbox\" id=\"dlna-preview\" name=\"preview\" ").concat(((_c = custom.preview) !== null && _c !== void 0 ? _c : def.preview) ? 'checked' : '', ">\n                                <label class=\"form-check-label\" for=\"dlna-preview\">preview</label>\n                            </div>\n                            <div class=\"mb-3\">\n                                <label class=\"form-label\" for=\"dlna-timeout\">timeout</label>\n                                <input type=\"number\" class=\"form-control\" id=\"dlna-timeout\" name=\"timeout\" value=\"").concat((_d = custom.timeout) !== null && _d !== void 0 ? _d : '', "\" placeholder=\"").concat((_e = def.timeout) !== null && _e !== void 0 ? _e : '', "\">\n                            </div>\n                            <div class=\"mb-3\">\n                                <label class=\"form-label\" for=\"dlna-skipModificationTime\">skipModificationTime</label>\n                                <input type=\"number\" class=\"form-control\" id=\"dlna-skipModificationTime\" name=\"skipModificationTime\" value=\"").concat((_f = custom.skipModificationTime) !== null && _f !== void 0 ? _f : '', "\" placeholder=\"").concat((_g = def.skipModificationTime) !== null && _g !== void 0 ? _g : '', "\">\n                            </div>\n                            <div class=\"mb-3\">\n                                <label class=\"form-label\" for=\"dlna-extension\">extension</label>\n                                <input type=\"text\" class=\"form-control\" id=\"dlna-extension\" name=\"extension\" value=\"").concat(escapeHtmlAttr((_h = custom.extension) !== null && _h !== void 0 ? _h : ''), "\" placeholder=\"").concat(escapeHtmlAttr((_j = def.extension) !== null && _j !== void 0 ? _j : ''), "\">\n                            </div>\n                            <div class=\"mb-3\">\n                                <label class=\"form-label\" for=\"dlna-coverComand\">coverComand</label>\n                                <input type=\"text\" class=\"form-control\" id=\"dlna-coverComand\" name=\"coverComand\" value=\"").concat(escapeHtmlAttr((_k = custom.coverComand) !== null && _k !== void 0 ? _k : ''), "\" placeholder=\"").concat(escapeHtmlAttr((_l = def.coverComand) !== null && _l !== void 0 ? _l : ''), "\">\n                            </div>\n                            <div class=\"mb-3\">\n                                <label class=\"form-label\" for=\"dlna-previewComand\">previewComand</label>\n                                <input type=\"text\" class=\"form-control\" id=\"dlna-previewComand\" name=\"previewComand\" value=\"").concat(escapeHtmlAttr((_m = custom.previewComand) !== null && _m !== void 0 ? _m : ''), "\" placeholder=\"").concat(escapeHtmlAttr((_o = def.previewComand) !== null && _o !== void 0 ? _o : ''), "\">\n                            </div>\n                            <div class=\"mb-3\">\n                                <label class=\"form-label\" for=\"dlna-priorityClass\">priorityClass</label>\n                                <input type=\"number\" class=\"form-control\" id=\"dlna-priorityClass\" name=\"priorityClass\" value=\"").concat(escapeHtmlAttr((_p = custom.priorityClass) !== null && _p !== void 0 ? _p : ''), "\" placeholder=\"").concat(escapeHtmlAttr((_q = def.priorityClass) !== null && _q !== void 0 ? _q : ''), "\">\n                            </div>\n                        </form>\n                    </div>\n                    <div class=\"modal-footer\">\n                        <button type=\"button\" class=\"btn btn-secondary\" id=\"dlna-modal-cancel\">\u041E\u0442\u043C\u0435\u043D\u0430</button>\n                        <button type=\"button\" class=\"btn btn-primary\" id=\"dlna-modal-save\">\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C</button>\n                    </div>\n                </div>\n            </div>\n        </div>\n    ");
    return html;
}
function initOnterModalDlna(key, def) {
    var modal = document.getElementById('dlna-modal');
    var openBtn = document.getElementById('dlna-modal-btn');
    var closeBtn = document.getElementById('dlna-modal-close');
    var cancelBtn = document.getElementById('dlna-modal-cancel');
    var saveBtn = document.getElementById('dlna-modal-save');
    var closeModal = function () {
        if (modal)
            modal.style.display = 'none';
    };
    var openModal = function () {
        if (modal)
            modal.style.display = 'block';
    };
    if (openBtn)
        openBtn.onclick = openModal;
    if (closeBtn)
        closeBtn.onclick = closeModal;
    if (cancelBtn)
        cancelBtn.onclick = closeModal;
    if (saveBtn) {
        saveBtn.onclick = function () {
            var _a;
            var _b, _c, _d;
            var form = document.getElementById('dlna-form');
            if (!form)
                return;
            var formData = new FormData(form);
            var updatedDlna = {
                enable: (formData.get('enable') === 'on'),
                consoleLog: (formData.get('consoleLog') === 'on'),
                preview: (formData.get('preview') === 'on'),
                timeout: parseInt(formData.get('timeout'), 10) || 0,
                skipModificationTime: parseInt(formData.get('skipModificationTime'), 10) || 0,
                extension: (_b = formData.get('extension')) !== null && _b !== void 0 ? _b : '',
                coverComand: (_c = formData.get('coverComand')) !== null && _c !== void 0 ? _c : '',
                previewComand: (_d = formData.get('previewComand')) !== null && _d !== void 0 ? _d : '',
                priorityClass: parseInt(formData.get('priorityClass'), 10) || 0
            };
            // Удаляем переменные с 0 или пустые строки, либо совпадающие с дефолтными
            Object.keys(updatedDlna).forEach(function (field) {
                if ((typeof updatedDlna[field] === 'boolean' && def && def[field] !== undefined && updatedDlna[field] === def[field]) ||
                    (typeof updatedDlna[field] === 'number' && (updatedDlna[field] === 0 || (def && def[field] !== undefined && updatedDlna[field] === def[field]))) ||
                    (typeof updatedDlna[field] === 'string' && (updatedDlna[field] === '' || (def && def[field] !== undefined && updatedDlna[field] === def[field])))) {
                    delete updatedDlna[field];
                }
            });
            rootCustomtItems = __assign(__assign({}, rootCustomtItems), (_a = {}, _a[key] = __assign(__assign({}, (rootCustomtItems[key] || {})), { cover: updatedDlna }), _a));
            closeModal();
            saveCustomtItems();
        };
    }
}
function renderOnterModalInitPlugins(html, custom, def) {
    html += "\n        <button type=\"button\" class=\"btn btn-secondary mb-2 mt-3\" id=\"initplugins-modal-btn\">initPlugins</button>\n        <div class=\"modal\" tabindex=\"-1\" id=\"initplugins-modal\" style=\"display:none; background:rgba(0,0,0,0.3); position:fixed; top:0; left:0; width:100vw; height:100vh; z-index:1050;\">\n            <div class=\"modal-dialog\" style=\"margin:10vh auto; max-width:400px;\">\n                <div class=\"modal-content\">\n                    <div class=\"modal-header\">\n                        <h5 class=\"modal-title\">\u0420\u0435\u0434\u0430\u043A\u0442\u0438\u0440\u043E\u0432\u0430\u0442\u044C initPlugins</h5>\n                        <button type=\"button\" class=\"btn-close\" id=\"initplugins-modal-close\" aria-label=\"\u0417\u0430\u043A\u0440\u044B\u0442\u044C\"></button>\n                    </div>\n                    <div class=\"modal-body\">\n                        <form id=\"initplugins-form\">\n                            ".concat(['dlna', 'tracks', 'tmdbProxy', 'online', 'sisi', 'timecode', 'torrserver', 'backup', 'sync'].map(function (field) {
        var _a;
        return "\n                                <div class=\"form-check mb-2\">\n                                    <input class=\"form-check-input\" type=\"checkbox\" id=\"initplugins-".concat(field, "\" name=\"").concat(field, "\" ").concat(((_a = custom[field]) !== null && _a !== void 0 ? _a : def[field]) ? 'checked' : '', ">\n                                    <label class=\"form-check-label\" for=\"initplugins-").concat(field, "\">").concat(field, "</label>\n                                </div>\n                            ");
    }).join(''), "\n                        </form>\n                    </div>\n                    <div class=\"modal-footer\">\n                        <button type=\"button\" class=\"btn btn-secondary\" id=\"initplugins-modal-cancel\">\u041E\u0442\u043C\u0435\u043D\u0430</button>\n                        <button type=\"button\" class=\"btn btn-primary\" id=\"initplugins-modal-save\">\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C</button>\n                    </div>\n                </div>\n            </div>\n        </div>\n    ");
    return html;
}
function initOnterModalInitPlugins(key, def) {
    var modal = document.getElementById('initplugins-modal');
    var openBtn = document.getElementById('initplugins-modal-btn');
    var closeBtn = document.getElementById('initplugins-modal-close');
    var cancelBtn = document.getElementById('initplugins-modal-cancel');
    var saveBtn = document.getElementById('initplugins-modal-save');
    var closeModal = function () {
        if (modal)
            modal.style.display = 'none';
    };
    var openModal = function () {
        if (modal)
            modal.style.display = 'block';
    };
    if (openBtn)
        openBtn.onclick = openModal;
    if (closeBtn)
        closeBtn.onclick = closeModal;
    if (cancelBtn)
        cancelBtn.onclick = closeModal;
    if (saveBtn) {
        saveBtn.onclick = function () {
            var _a;
            var form = document.getElementById('initplugins-form');
            if (!form)
                return;
            var formData = new FormData(form);
            var fields = ['dlna', 'tracks', 'tmdbProxy', 'online', 'sisi', 'timecode', 'torrserver', 'backup', 'sync'];
            var updatedInitPlugins = {};
            fields.forEach(function (field) {
                var el = form.querySelector("[name=\"".concat(field, "\"]"));
                var checked = el ? el.checked : false;
                if (def && def[field] !== undefined && checked === def[field]) {
                    // Совпадает с дефолтным, не сохраняем
                    return;
                }
                updatedInitPlugins[field] = checked;
            });
            rootCustomtItems = __assign(__assign({}, rootCustomtItems), (_a = {}, _a[key] = __assign(__assign({}, (rootCustomtItems[key] || {})), { initPlugins: updatedInitPlugins }), _a));
            closeModal();
            saveCustomtItems();
        };
    }
}
function renderOnterModalBookmarks(html, custom, def) {
    var _a, _b;
    html += "\n        <button type=\"button\" class=\"btn btn-secondary mb-2 mt-3\" id=\"bookmarks-modal-btn\">bookmarks</button>\n        <div class=\"modal\" tabindex=\"-1\" id=\"bookmarks-modal\" style=\"display:none; background:rgba(0,0,0,0.3); position:fixed; top:0; left:0; width:100vw; height:100vh; z-index:1050;\">\n            <div class=\"modal-dialog\" style=\"margin:10vh auto; max-width:400px;\">\n                <div class=\"modal-content\">\n                    <div class=\"modal-header\">\n                        <h5 class=\"modal-title\">\u0420\u0435\u0434\u0430\u043A\u0442\u0438\u0440\u043E\u0432\u0430\u0442\u044C bookmarks</h5>\n                        <button type=\"button\" class=\"btn-close\" id=\"bookmarks-modal-close\" aria-label=\"\u0417\u0430\u043A\u0440\u044B\u0442\u044C\"></button>\n                    </div>\n                    <div class=\"modal-body\">\n                        <form id=\"bookmarks-form\">\n                            <div class=\"form-check mb-3\">\n                                <input class=\"form-check-input\" type=\"checkbox\" id=\"bookmarks-saveimage\" name=\"saveimage\" ".concat(((_a = custom.saveimage) !== null && _a !== void 0 ? _a : def.saveimage) ? 'checked' : '', ">\n                                <label class=\"form-check-label\" for=\"bookmarks-saveimage\">saveimage</label>\n                            </div>\n                            <div class=\"form-check mb-3\">\n                                <input class=\"form-check-input\" type=\"checkbox\" id=\"bookmarks-savepreview\" name=\"savepreview\" ").concat(((_b = custom.savepreview) !== null && _b !== void 0 ? _b : def.savepreview) ? 'checked' : '', ">\n                                <label class=\"form-check-label\" for=\"bookmarks-savepreview\">savepreview</label>\n                            </div>\n                        </form>\n                    </div>\n                    <div class=\"modal-footer\">\n                        <button type=\"button\" class=\"btn btn-secondary\" id=\"bookmarks-modal-cancel\">\u041E\u0442\u043C\u0435\u043D\u0430</button>\n                        <button type=\"button\" class=\"btn btn-primary\" id=\"bookmarks-modal-save\">\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C</button>\n                    </div>\n                </div>\n            </div>\n        </div>\n    ");
    return html;
}
function initOnterModalBookmarks(key, def) {
    var modal = document.getElementById('bookmarks-modal');
    var openBtn = document.getElementById('bookmarks-modal-btn');
    var closeBtn = document.getElementById('bookmarks-modal-close');
    var cancelBtn = document.getElementById('bookmarks-modal-cancel');
    var saveBtn = document.getElementById('bookmarks-modal-save');
    var closeModal = function () {
        if (modal)
            modal.style.display = 'none';
    };
    var openModal = function () {
        if (modal)
            modal.style.display = 'block';
    };
    if (openBtn)
        openBtn.onclick = openModal;
    if (closeBtn)
        closeBtn.onclick = closeModal;
    if (cancelBtn)
        cancelBtn.onclick = closeModal;
    if (saveBtn) {
        saveBtn.onclick = function () {
            var _a;
            var form = document.getElementById('bookmarks-form');
            if (!form)
                return;
            var formData = new FormData(form);
            var updatedBookmarks = {
                saveimage: (formData.get('saveimage') === 'on'),
                savepreview: (formData.get('savepreview') === 'on')
            };
            // Удаляем переменные, совпадающие с дефолтными
            Object.keys(updatedBookmarks).forEach(function (field) {
                if ((def && def[field] !== undefined && updatedBookmarks[field] === def[field])) {
                    delete updatedBookmarks[field];
                }
            });
            rootCustomtItems = __assign(__assign({}, rootCustomtItems), (_a = {}, _a[key] = __assign(__assign({}, (rootCustomtItems[key] || {})), { bookmarks: updatedBookmarks }), _a));
            closeModal();
            saveCustomtItems();
        };
    }
}
function renderOtherPage(containerId) {
    var container = document.getElementById(containerId);
    if (!container)
        return;
    var keys = ['base', 'listen', 'WAF', 'tmdb', 'cub', 'LampaWeb', 'dlna', 'online', 'sisi', 'chromium', 'firefox', 'serverproxy', 'weblog', 'openstat', 'posterApi', 'rch', 'storage', 'ffprobe', 'fileCacheInactive', 'vast', 'apn', 'kit', 'sync'];
    loadCustomAndDefault(container, function () {
        // Боковое меню
        var sidebarHtml = "\n            <div class=\"list-group\" id=\"other-sidebar\">\n                ".concat(keys.map(function (key, idx) { return "\n                    <a href=\"#\" class=\"list-group-item list-group-item-action".concat(idx === 0 ? ' active' : '', "\" data-key=\"").concat(key, "\">").concat(key, "</a>\n                "); }).join(''), "\n            </div>\n        ");
        container.innerHTML = "\n            <div class=\"row\">\n                <div class=\"col-12 col-md-2 mb-3 mb-md-0\">\n                    ".concat(sidebarHtml, "\n                </div>\n                <div class=\"col-12 col-md-10\" id=\"other-main-content\" style=\"margin-bottom: 2em; background-color: #fdfdfd; padding: 10px; border-radius: 8px; box-shadow: 0 18px 24px rgb(175 175 175 / 30%);\"></div>\n            </div>\n        ");
        function renderKeyForm(key) {
            if (key === 'base') {
                renderBaseForm();
                return;
            }
            var def = rootDefaultItems && rootDefaultItems[key] ? rootDefaultItems[key] : {};
            var custom = rootCustomtItems && rootCustomtItems[key] ? rootCustomtItems[key] : {};
            var excludeFields = ['proxy', 'override_conf', 'appReplace', 'cache_hls', 'headersDeny', // в пизду
                'context', 'image', 'buffering', 'cover', 'initPlugins', 'bookmarks']; // адаптировано
            var arrayEmptyFields = ['Args', 'geo', 'with_search', 'rsize_disable', 'proxyimg_disable', 'ipsDeny', 'ipsAllow', 'countryDeny', 'countryAllow'];
            var html = "<form id=\"other-form\">";
            Object.keys(def)
                .filter(function (field) { return !excludeFields.includes(field); })
                .forEach(function (field) {
                var _a;
                var placeholder = (_a = def[field]) !== null && _a !== void 0 ? _a : '';
                var value = custom[field] !== undefined ? custom[field] : '';
                if (value === null || value === undefined)
                    value = '';
                var defType = typeof def[field];
                if (arrayEmptyFields.includes(field)) {
                    // Массив, который должен быть [""] если пусто
                    var arrValue = Array.isArray(value) ? value : [];
                    if (arrValue.length === 0)
                        arrValue = [''];
                    html += "\n                            <div class=\"mb-3\">\n                                <label class=\"form-label\" for=\"field-".concat(key, "-").concat(field, "\">").concat(field, " (\u0447\u0435\u0440\u0435\u0437 \u0437\u0430\u043F\u044F\u0442\u0443\u044E)</label>\n                                <input type=\"text\" class=\"form-control\" id=\"field-").concat(key, "-").concat(field, "\" name=\"").concat(key, ".").concat(field, "\" value=\"").concat(arrValue.join(', '), "\" placeholder=\"").concat(Array.isArray(placeholder) ? placeholder.join(', ') : '', "\">\n                            </div>\n                        ");
                }
                else if (defType === 'boolean') {
                    html += "\n                            <div class=\"form-check mb-3\">\n                                <input class=\"form-check-input\" type=\"checkbox\" id=\"field-".concat(key, "-").concat(field, "\" name=\"").concat(key, ".").concat(field, "\" ").concat((value !== '' ? value : def[field]) ? 'checked' : '', ">\n                                <label class=\"form-check-label\" for=\"field-").concat(key, "-").concat(field, "\">").concat(field, "</label>\n                            </div>\n                        ");
                }
                else if (defType === 'number' && Number.isInteger(def[field])) {
                    html += "\n                            <div class=\"mb-3\">\n                                <label class=\"form-label\" for=\"field-".concat(key, "-").concat(field, "\">").concat(field, "</label>\n                                <input type=\"number\" class=\"form-control\" id=\"field-").concat(key, "-").concat(field, "\" name=\"").concat(key, ".").concat(field, "\" value=\"").concat(value, "\" placeholder=\"").concat(placeholder, "\">\n                            </div>\n                        ");
                }
                else {
                    html += "\n                            <div class=\"mb-3\">\n                                <label class=\"form-label\" for=\"field-".concat(key, "-").concat(field, "\">").concat(field, "</label>\n                                <input type=\"text\" class=\"form-control\" id=\"field-").concat(key, "-").concat(field, "\" name=\"").concat(key, ".").concat(field, "\" value=\"").concat(value, "\" placeholder=\"").concat(placeholder, "\">\n                            </div>\n                        ");
                }
            });
            html += "<button type=\"button\" class=\"btn btn-primary\" id=\"other-save-btn\">\u0421\u043E\u0445\u0440\u0430\u043D\u0438\u0442\u044C</button></form>";
            if (def) {
                if (def['context']) {
                    html = renderOnterModalContext(html, (custom && custom['context']) ? custom['context'] : {}, def['context']);
                }
                if (def['image']) {
                    html = renderOnterModalImage(html, (custom && custom['image']) ? custom['image'] : {}, def['image']);
                }
                if (def['buffering']) {
                    html = renderOnterModalBuffering(html, (custom && custom['buffering']) ? custom['buffering'] : {}, def['buffering']);
                }
                if (def['cover']) {
                    html = renderOnterModalDlna(html, (custom && custom['cover']) ? custom['cover'] : {}, def['cover']);
                }
                if (def['initPlugins']) {
                    html = renderOnterModalInitPlugins(html, (custom && custom['initPlugins']) ? custom['initPlugins'] : {}, def['initPlugins']);
                }
                if (def['bookmarks']) {
                    html = renderOnterModalBookmarks(html, (custom && custom['bookmarks']) ? custom['bookmarks'] : {}, def['bookmarks']);
                }
            }
            var mainContent = document.getElementById('other-main-content');
            if (mainContent)
                mainContent.innerHTML = html;
            if (def) {
                if (def['context']) {
                    initOnterModalContext(key, def['context']);
                }
                if (def['image']) {
                    initOnterModalImage(key, def['image']);
                }
                if (def['buffering']) {
                    initOnterModalBuffering(key, def['buffering']);
                }
                if (def['cover']) {
                    initOnterModalDlna(key, def['cover']);
                }
                if (def['initPlugins']) {
                    initOnterModalInitPlugins(key, def['initPlugins']);
                }
                if (def['bookmarks']) {
                    initOnterModalBookmarks(key, def['bookmarks']);
                }
            }
            var saveBtn = document.getElementById('other-save-btn');
            if (saveBtn) {
                saveBtn.onclick = function () {
                    var form = document.getElementById('other-form');
                    if (!form)
                        return;
                    var formData = new FormData(form);
                    var updated = {};
                    updated[key] = {};
                    Object.keys(def)
                        .filter(function (field) { return !excludeFields.includes(field); })
                        .forEach(function (field) {
                        var defType = typeof def[field];
                        var val = formData.get("".concat(key, ".").concat(field));
                        if (arrayEmptyFields.includes(field)) {
                            var arr = val
                                .split(',')
                                .map(function (s) { return s.trim(); })
                                .filter(function (s) { return s !== ''; });
                            if (arr.length > 0) {
                                updated[key][field] = arr;
                            }
                            else {
                                delete updated[key][field];
                            }
                        }
                        else if (defType === 'boolean') {
                            var el = form.querySelector("[name=\"".concat(key, ".").concat(field, "\"]"));
                            val = el ? el.checked : false;
                            if (val !== def[field])
                                updated[key][field] = val;
                        }
                        else if (defType === 'number' && Number.isInteger(def[field])) {
                            val = val !== null && val !== undefined && val !== '' ? parseInt(val, 10) : 0;
                            if (val !== 0 && val !== def[field])
                                updated[key][field] = val;
                        }
                        else if (val !== null && val !== undefined && val !== '') {
                            updated[key][field] = val;
                        }
                    });
                    // Обновляем rootCustomtItems
                    rootCustomtItems = __assign(__assign({}, rootCustomtItems), updated);
                    // Удаляем [key], если он пустой
                    if (Object.keys(rootCustomtItems[key]).length === 0) {
                        delete rootCustomtItems[key];
                    }
                    saveCustomtItems();
                };
            }
        }
        // По умолчанию показываем первый ключ
        renderKeyForm(keys[0]);
        // Навешиваем обработчик на меню
        setTimeout(function () {
            var sidebar = document.getElementById('other-sidebar');
            if (sidebar) {
                sidebar.querySelectorAll('a[data-key]').forEach(function (link) {
                    link.addEventListener('click', function (e) {
                        e.preventDefault();
                        // Снимаем активный класс со всех
                        sidebar.querySelectorAll('.list-group-item').forEach(function (item) { return item.classList.remove('active'); });
                        // Добавляем активный класс текущему
                        e.currentTarget.classList.add('active');
                        var key = e.currentTarget.getAttribute('data-key');
                        if (!key)
                            return;
                        renderKeyForm(key);
                    });
                });
            }
        }, 0);
    });
}
//# sourceMappingURL=app.js.map