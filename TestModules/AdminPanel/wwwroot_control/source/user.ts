function formatDate(dateStr: string): string {
    if (!dateStr) return '';
    const date = new Date(dateStr);
    if (isNaN(date.getTime())) return '';
    const day = String(date.getDate()).padStart(2, '0');
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const year = date.getFullYear();
    return `${day}.${month}.${year}`;
}

function getUserModalHtml(
    modalId: string,
    title: string,
    user?: any,
    nextMonthDate?: string,
    showDelete = false,
    showBanFields = true // новый параметр
): string {
    return `
    <div class="modal fade" id="${modalId}" tabindex="-1" aria-labelledby="${modalId}Label" aria-hidden="true">
      <div class="modal-dialog">
        <div class="modal-content">
          <div class="modal-header">
            <h5 class="modal-title" id="${modalId}Label">${title}</h5>
            <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Закрыть"></button>
          </div>
          <div class="modal-body">
            <form id="${modalId}-form">
              <div class="mb-3">
                <label for="${modalId}-user-id" class="form-label">ID</label>
                <input type="text" class="form-control" id="${modalId}-user-id" value="${user ? (user.id || '') : ''}" placeholder="unic_id, token, email, пароль" required>
              </div>
              <div class="mb-3">
                <label for="${modalId}-user-ids" class="form-label">IDs (через запятую)</label>
                <input type="text" class="form-control" id="${modalId}-user-ids" value="${user ? (user.ids ? user.ids.join(', ') : '') : ''}" placeholder="uid-1, uid-2, token">
              </div>
              <div class="mb-3">
                <label for="${modalId}-user-expires" class="form-label">Доступ до (ГГГГ-ММ-ДД)</label>
                <input type="date" class="form-control" id="${modalId}-user-expires" value="${user ? (user.expires ? user.expires.substring(0, 10) : '') : (nextMonthDate || '')}" required>
              </div>
              <div class="mb-3">
                <label for="${modalId}-user-group" class="form-label">Группа доступа</label>
                <input type="number" class="form-control" id="${modalId}-user-group" value="${user ? user.group : 1}" required>
              </div>
              <div class="mb-3">
                <label for="${modalId}-user-comment" class="form-label">Комментарий для себя</label>
                <input type="text" class="form-control" id="${modalId}-user-comment" value="${user ? (user.comment || '') : ''}" placeholder="контакты пользователя, etc">
              </div>
              ${showBanFields ? `
              <div class="mb-3 mt-5">
                <div class="form-check">
                  <input class="form-check-input" type="checkbox" id="${modalId}-user-ban" ${user && user.ban ? 'checked' : ''}>
                  <label class="form-check-label" for="${modalId}-user-ban">Заблокировать доступ</label>
                </div>
              </div>
              <div class="mb-3">
                <input type="text" class="form-control" id="${modalId}-user-ban-msg" value="${user ? (user.ban_msg || '') : ''}" placeholder="сообщение которое видит пользователь">
              </div>
              ` : ''}
            </form>
            ${showDelete ? `<button type="button" class="btn btn-warning" id="${modalId}-params-btn">params</button>` : '<span></span>'}
          </div>
          <div class="modal-footer d-flex justify-content-between">
            ${showDelete ? `<button type="button" class="btn btn-danger" id="${modalId}-delete-btn">Удалить</button>` : '<span></span>'}
            <div>
              <button type="button" class="btn btn-secondary me-2" data-bs-dismiss="modal">Закрыть</button>
              <button type="button" class="btn btn-primary" id="${modalId}-save-btn">Сохранить</button>
            </div>
          </div>
        </div>
      </div>
    </div>
    `;
}

function renderUsersPage(containerId: string) {
    const container = document.getElementById(containerId);
    if (!container) return;

    // Получаем дату +1 месяц от текущей
    function getNextMonthDate(): string {
        const now = new Date();
        const year = now.getFullYear();
        let month = now.getMonth() + 2;
        let nextYear = year;
        if (month > 12) {
            month = 1;
            nextYear++;
        }
        const day = String(now.getDate()).padStart(2, '0');
        return `${nextYear}-${String(month).padStart(2, '0')}-${day}`;
    }

    const nextMonthDate = getNextMonthDate();

    // Модальное окно для добавления пользователя (без полей ban и ban_msg)
    const addModalHtml = getUserModalHtml('addUserModal', 'Добавить пользователя', undefined, nextMonthDate, false, false);

    container.innerHTML = `
        ${addModalHtml}
        <div id="edit-user-modal-container"></div>
        <div class="d-flex justify-content-between align-items-center mb-3">
            <h1 class="mb-0">Пользователи</h1>
            <div>
                <button type="button" class="btn btn-success" id="btn-add-user">Добавить пользователя</button>
                <button type="button" class="btn btn-light ms-2" id="btn-users-settings" title="Настройки" style="vertical-align: middle;">
                    <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" fill="currentColor" class="bi bi-gear" viewBox="0 0 16 16">
                        <path d="M8 4.754a3.246 3.246 0 1 0 0 6.492 3.246 3.246 0 0 0 0-6.492zM5.754 8a2.246 2.246 0 1 1 4.492 0 2.246 2.246 0 0 1-4.492 0z"/>
                        <path d="M9.796 1.343c-.527-1.79-3.065-1.79-3.592 0l-.094.319a.873.873 0 0 1-1.255.52l-.292-.16c-1.64-.892-3.433.902-2.54 2.541l.159.292a.873.873 0 0 1-.52 1.255l-.319.094c-1.79.527-1.79 3.065 0 3.592l.319.094a.873.873 0 0 1 .52 1.255l-.16.292c-.892 1.64.901 3.434 2.541 2.54l.292-.159a.873.873 0 0 1 1.255.52l.094.319c.527 1.79 3.065 1.79 3.592 0l.094-.319a.873.873 0 0 1 1.255-.52l.292.16c1.64.893 3.434-.902 2.54-2.541l-.159-.292a.873.873 0 0 1 .52-1.255l.319-.094c1.79-.527 1.79-3.065 0-3.592l-.319-.094a.873.873 0 0 1-.52-1.255l.16-.292c.893-1.64-.902-3.433-2.541-2.54l-.292.159a.873.873 0 0 1-1.255-.52l-.094-.319zm-2.633.283c.246-.835 1.428-.835 1.674 0l.094.319a1.873 1.873 0 0 0 2.693 1.115l.291-.16c.764-.415 1.6.42 1.184 1.185l-.159.292a1.873 1.873 0 0 0 1.116 2.692l.318.094c.835.246.835 1.428 0 1.674l-.319.094a1.873 1.873 0 0 0-1.115 2.693l.16.291c.415.764-.42 1.6-1.185 1.184l-.291-.159a1.873 1.873 0 0 0-2.693 1.116l-.094.318c-.246.835-1.428.835-1.674 0l-.094-.319a1.873 1.873 0 0 0-2.692-1.115l-.292.16c-.764.415-1.6-.42-1.184-1.185l.159-.291A1.873 1.873 0 0 0 1.945 8.93l-.319-.094c-.835-.246-.835-1.428 0-1.674l.319-.094A1.873 1.873 0 0 0 3.06 4.377l-.16-.292c-.415-.764.42-1.6 1.185-1.184l.292.159a1.873 1.873 0 0 0 2.692-1.115l.094-.319z"/>
                    </svg>
                </button>
                <button type="button" class="btn btn-primary" id="btn-save-users" style="display: none;">Сохранить</button>
            </div>
        </div>
        <div id="users-list" class="row g-3"></div>
    `;
    loadAndRenderUsers('users-list');

    setTimeout(() => {
        // Кнопка "Сохранить"
        const btnSave = document.getElementById('btn-save-users');
        if (btnSave) {
            btnSave.onclick = () => {
                if (rootCustomtItems) {
                    saveCustomtItems();
                } else {
                    alert('Данные не загружены');
                }
            };
        }

        // Кнопка "Добавить"
        const btnAdd = document.getElementById('btn-add-user');
        if (btnAdd) {
            btnAdd.onclick = () => {
                // @ts-ignore
                const modal = new bootstrap.Modal(document.getElementById('addUserModal'));
                modal.show();
            };
        }

        // Кнопка "Сохранить" в модальном окне добавления
        const saveUserBtn = document.getElementById('addUserModal-save-btn');
        if (saveUserBtn) {
            saveUserBtn.onclick = () => {
                const id = (document.getElementById('addUserModal-user-id') as HTMLInputElement).value.trim();
                const idsRaw = (document.getElementById('addUserModal-user-ids') as HTMLInputElement).value.trim();
                const expires = (document.getElementById('addUserModal-user-expires') as HTMLInputElement).value;
                const group = parseInt((document.getElementById('addUserModal-user-group') as HTMLInputElement).value, 10);
                const comment = (document.getElementById('addUserModal-user-comment') as HTMLInputElement).value.trim();

                if (!id || !expires || isNaN(group)) {
                    alert('Пожалуйста, заполните все обязательные поля');
                    return;
                }

                const ids = idsRaw ? idsRaw.split(',').map(s => s.trim()).filter(Boolean) : [];

                const newUser = {
                    id,
                    ids,
                    expires: expires + "T00:00:00",
                    group,
                    comment
                };

                if (window.invc)
                    window.invc.newUser(newUser);

                if (rootCustomtItems && rootCustomtItems["accsdb"] && Array.isArray(rootCustomtItems["accsdb"]["users"])) {
                    rootCustomtItems["accsdb"]["users"].push(newUser);
                    // Перерисовать таблицу
                    const usersList = document.getElementById('users-list');
                    if (usersList) {
                        usersList.innerHTML = renderUsers(rootCustomtItems["accsdb"]["users"]);
                        attachEditHandlers(rootCustomtItems["accsdb"]["users"]);
                    }
                }

                // Закрыть модальное окно
                // @ts-ignore
                const modal = bootstrap.Modal.getInstance(document.getElementById('addUserModal'));
                if (modal) modal.hide();

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
            const modalHtml = `
                <div class="modal fade" id="accsdb-settings-modal" tabindex="-1" aria-labelledby="accsdbSettingsLabel" aria-hidden="true">
                  <div class="modal-dialog modal-lg">
                    <div class="modal-content">
                      <div class="modal-header">
                        <h5 class="modal-title" id="accsdbSettingsLabel">Настройки accsdb</h5>
                        <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Закрыть"></button>
                      </div>
                      <div class="modal-body">
                        <form id="accsdb-settings-form">
                          <div class="row g-2">
                            <div class="col-6 mb-2">
                              <div class="form-check">
                                <input class="form-check-input" type="checkbox" id="accsdb-enable">
                                <label class="form-check-label" for="accsdb-enable">Включить авторизацию</label>
                              </div>
                            </div>
                            <div class="col-6 mb-2">
                              <label class="form-label" for="accsdb-whitepattern">whitepattern</label>
                              <input type="text" class="form-control" id="accsdb-whitepattern">
                            </div>
                            <div class="col-6 mb-2 mb-5">
                              <label class="form-label" for="accsdb-premium_pattern">premium_pattern</label>
                              <input type="text" class="form-control" id="accsdb-premium_pattern">
                            </div>
                            <div class="col-6 mb-2">
                              <label class="form-label" for="accsdb-domainId_pattern">domainId_pattern</label>
                              <input type="text" class="form-control" id="accsdb-domainId_pattern">
                            </div>
                            <div class="col-6 mb-2">
                              <label class="form-label" for="accsdb-maxip_hour">Лимит ip в час</label>
                              <input type="number" class="form-control" id="accsdb-maxip_hour">
                            </div>
                            <div class="col-6 mb-2">
                              <label class="form-label" for="accsdb-maxrequest_hour">Лимит запросов в час</label>
                              <input type="number" class="form-control" id="accsdb-maxrequest_hour">
                            </div>
                            <div class="col-12 mb-2">
                              <label class="form-label" for="accsdb-maxlock_day">Лимит блокировок в сутки (maxlock_day)</label>
                              <input type="number" class="form-control" id="accsdb-maxlock_day">
                            </div>
                            <div class="col-12 mb-2">
                              <label class="form-label" for="accsdb-blocked_hour">На сколько часов блокировать при достижении лимита maxlock_day</label>
                              <input type="number" class="form-control" id="accsdb-blocked_hour">
                            </div>
                            <div class="col-12 mb-2 mt-5">
                              <label class="form-label" for="accsdb-authMesage">Ошибка - нету идентификатора uid, token, email</label>
                              <input type="text" class="form-control" id="accsdb-authMesage">
                            </div>
                            <div class="col-12 mb-2">
                              <label class="form-label" for="accsdb-denyMesage">Ошибка - нету доступа</label>
                              <input type="text" class="form-control" id="accsdb-denyMesage">
                            </div>
                            <div class="col-12 mb-2">
                              <label class="form-label" for="accsdb-denyGroupMesage">Ошибка - группа пользователя ниже group балансера</label>
                              <input type="text" class="form-control" id="accsdb-denyGroupMesage">
                            </div>
                            <div class="col-12 mb-2">
                              <label class="form-label" for="accsdb-expiresMesage">Ошибка - закончился доступ</label>
                              <input type="text" class="form-control" id="accsdb-expiresMesage">
                            </div>
                          </div>
                        </form>
                      </div>
                      <div class="modal-footer">
                        <button type="button" class="btn btn-secondary me-2" data-bs-dismiss="modal">Закрыть</button>
                        <button type="button" class="btn btn-primary" id="accsdb-settings-save-btn">Сохранить</button>
                      </div>
                    </div>
                  </div>
                </div>
                `;
            document.body.insertAdjacentHTML('beforeend', modalHtml);
        }

        // Открытие модального окна и заполнение полей
        const btnSettings = document.getElementById('btn-users-settings');
        if (btnSettings) {
            btnSettings.onclick = function () {
                const accsdb = rootCustomtItems && rootCustomtItems.accsdb ? rootCustomtItems.accsdb : {};

                (document.getElementById('accsdb-enable') as HTMLInputElement).checked = !!accsdb.enable;
                (document.getElementById('accsdb-whitepattern') as HTMLInputElement).value = accsdb.whitepattern ?? '';
                (document.getElementById('accsdb-premium_pattern') as HTMLInputElement).value = accsdb.premium_pattern ?? '';
                (document.getElementById('accsdb-domainId_pattern') as HTMLInputElement).value = accsdb.domainId_pattern ?? '';
                (document.getElementById('accsdb-maxip_hour') as HTMLInputElement).value = accsdb.maxip_hour ?? '';
                (document.getElementById('accsdb-maxrequest_hour') as HTMLInputElement).value = accsdb.maxrequest_hour ?? '';
                (document.getElementById('accsdb-maxlock_day') as HTMLInputElement).value = accsdb.maxlock_day ?? '';
                (document.getElementById('accsdb-blocked_hour') as HTMLInputElement).value = accsdb.blocked_hour ?? '';
                (document.getElementById('accsdb-authMesage') as HTMLInputElement).value = accsdb.authMesage ?? '';
                (document.getElementById('accsdb-denyMesage') as HTMLInputElement).value = accsdb.denyMesage ?? '';
                (document.getElementById('accsdb-denyGroupMesage') as HTMLInputElement).value = accsdb.denyGroupMesage ?? '';
                (document.getElementById('accsdb-expiresMesage') as HTMLInputElement).value = accsdb.expiresMesage ?? '';

                const accsDefault = rootDefaultItems && rootDefaultItems.accsdb ? rootDefaultItems.accsdb : {};
                (document.getElementById('accsdb-whitepattern') as HTMLInputElement).placeholder = accsDefault.whitepattern ?? '';
                (document.getElementById('accsdb-premium_pattern') as HTMLInputElement).placeholder = accsDefault.premium_pattern ?? '';
                (document.getElementById('accsdb-domainId_pattern') as HTMLInputElement).placeholder = accsDefault.domainId_pattern ?? '';
                (document.getElementById('accsdb-maxip_hour') as HTMLInputElement).placeholder = accsDefault.maxip_hour ?? '';
                (document.getElementById('accsdb-maxrequest_hour') as HTMLInputElement).placeholder = accsDefault.maxrequest_hour ?? '';
                (document.getElementById('accsdb-maxlock_day') as HTMLInputElement).placeholder = accsDefault.maxlock_day ?? '';
                (document.getElementById('accsdb-blocked_hour') as HTMLInputElement).placeholder = accsDefault.blocked_hour ?? '';
                (document.getElementById('accsdb-authMesage') as HTMLInputElement).placeholder = accsDefault.authMesage ?? '';
                (document.getElementById('accsdb-denyMesage') as HTMLInputElement).placeholder = accsDefault.denyMesage ?? '';
                (document.getElementById('accsdb-denyGroupMesage') as HTMLInputElement).placeholder = accsDefault.denyGroupMesage ?? '';
                (document.getElementById('accsdb-expiresMesage') as HTMLInputElement).placeholder = accsDefault.expiresMesage ?? '';

                // @ts-ignore
                const modal = new bootstrap.Modal(document.getElementById('accsdb-settings-modal'));
                modal.show();
            };
        }

        // Сохранение изменений
        const accsdbSaveBtn = document.getElementById('accsdb-settings-save-btn');
        if (accsdbSaveBtn) {
            accsdbSaveBtn.onclick = function () {
                if (!rootCustomtItems) return;
                if (!rootCustomtItems.accsdb) rootCustomtItems.accsdb = {};
                const accsdb = rootCustomtItems.accsdb;
                accsdb.enable = (document.getElementById('accsdb-enable') as HTMLInputElement).checked;
                accsdb.whitepattern = (document.getElementById('accsdb-whitepattern') as HTMLInputElement).value;
                accsdb.premium_pattern = (document.getElementById('accsdb-premium_pattern') as HTMLInputElement).value || null;
                accsdb.domainId_pattern = (document.getElementById('accsdb-domainId_pattern') as HTMLInputElement).value || null;
                accsdb.maxip_hour = parseInt((document.getElementById('accsdb-maxip_hour') as HTMLInputElement).value, 10) || 0;
                accsdb.maxrequest_hour = parseInt((document.getElementById('accsdb-maxrequest_hour') as HTMLInputElement).value, 10) || 0;
                accsdb.maxlock_day = parseInt((document.getElementById('accsdb-maxlock_day') as HTMLInputElement).value, 10) || 0;
                accsdb.blocked_hour = parseInt((document.getElementById('accsdb-blocked_hour') as HTMLInputElement).value, 10) || 0;
                accsdb.authMesage = (document.getElementById('accsdb-authMesage') as HTMLInputElement).value;
                accsdb.denyMesage = (document.getElementById('accsdb-denyMesage') as HTMLInputElement).value;
                accsdb.denyGroupMesage = (document.getElementById('accsdb-denyGroupMesage') as HTMLInputElement).value;
                accsdb.expiresMesage = (document.getElementById('accsdb-expiresMesage') as HTMLInputElement).value;

                // Удаление переменных с пустыми или дефолтными значениями
                const defaults = {
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
                Object.keys(defaults).forEach(key => {
                    const value = accsdb[key as keyof typeof defaults];
                    const def = defaults[key as keyof typeof defaults];
                    if (
                        value === undefined ||
                        value === null ||
                        (typeof def === 'string' && (value === '' || value === def)) ||
                        (typeof def === 'number' && value === def) ||
                        (typeof def === 'boolean' && value === def)
                    ) {
                        delete accsdb[key as keyof typeof defaults];
                    }
                });

                // @ts-ignore
                const modal = bootstrap.Modal.getInstance(document.getElementById('accsdb-settings-modal'));
                if (modal) modal.hide();
                saveCustomtItems();
            };
        }


    }, 0);
}

function attachEditHandlers(users: any[]) {
    users.forEach((user: any, idx: number) => {
        const btn = document.getElementById(`edit-user-btn-${idx}`);
        if (btn) {
            btn.onclick = () => {
                const editModalId = `editUserModal-${idx}`;
                const editModalHtml = getUserModalHtml(editModalId, 'Редактировать пользователя', user, undefined, true);
                const editModalContainer = document.getElementById('edit-user-modal-container');
                if (editModalContainer) {
                    editModalContainer.innerHTML = editModalHtml;
                }
                // @ts-ignore
                const modal = new bootstrap.Modal(document.getElementById(editModalId));
                modal.show();

                setTimeout(() => {
                    const editParamsBtn = document.getElementById(`${editModalId}-params-btn`);
                    if (editParamsBtn) {
                        editParamsBtn.onclick = () => {
                            if (window.invc)
                                window.invc.editUser(user);
                        };
                    }

                    const saveBtn = document.getElementById(`${editModalId}-save-btn`);
                    if (saveBtn) {
                        saveBtn.onclick = () => {
                            const id = (document.getElementById(`${editModalId}-user-id`) as HTMLInputElement).value.trim();
                            const idsRaw = (document.getElementById(`${editModalId}-user-ids`) as HTMLInputElement).value.trim();
                            const expires = (document.getElementById(`${editModalId}-user-expires`) as HTMLInputElement).value;
                            const group = parseInt((document.getElementById(`${editModalId}-user-group`) as HTMLInputElement).value, 10);
                            const comment = (document.getElementById(`${editModalId}-user-comment`) as HTMLInputElement).value.trim();
                            const ban = (document.getElementById(`${editModalId}-user-ban`) as HTMLInputElement).checked;
                            const ban_msg = (document.getElementById(`${editModalId}-user-ban-msg`) as HTMLInputElement).value.trim();

                            if (!id || !expires || isNaN(group)) {
                                alert('Пожалуйста, заполните все обязательные поля');
                                return;
                            }

                            const ids = idsRaw ? idsRaw.split(',').map(s => s.trim()).filter(Boolean) : [];

                            user.id = id;
                            user.ids = ids;
                            user.expires = expires + "T00:00:00";
                            user.group = group;
                            user.comment = comment;
                            user.ban = ban;
                            user.ban_msg = ban_msg;

                            const usersList = document.getElementById('users-list');
                            if (usersList) {
                                usersList.innerHTML = renderUsers(rootCustomtItems["accsdb"]["users"]);
                                attachEditHandlers(rootCustomtItems["accsdb"]["users"]);
                            }

                            // @ts-ignore
                            const modal = bootstrap.Modal.getInstance(document.getElementById(editModalId));
                            if (modal) modal.hide();

                            saveCustomtItems();
                        };
                    }

                    // Обработчик для кнопки "Удалить"
                    const deleteBtn = document.getElementById(`${editModalId}-delete-btn`);
                    if (deleteBtn) {
                        deleteBtn.onclick = () => {
                            if (confirm('Удалить пользователя?')) {
                                if (rootCustomtItems && rootCustomtItems["accsdb"] && Array.isArray(rootCustomtItems["accsdb"]["users"])) {
                                    const usersArr = rootCustomtItems["accsdb"]["users"];
                                    const userIdx = usersArr.indexOf(user);
                                    if (userIdx !== -1) {
                                        usersArr.splice(userIdx, 1);
                                        const usersList = document.getElementById('users-list');
                                        if (usersList) {
                                            usersList.innerHTML = renderUsers(usersArr);
                                            attachEditHandlers(usersArr);
                                        }
                                        // @ts-ignore
                                        const modal = bootstrap.Modal.getInstance(document.getElementById(editModalId));
                                        if (modal) modal.hide();
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

function loadAndRenderUsers(containerId: string) {
    const container = document.getElementById(containerId);
    if (!container) return;

    // Сначала загружаем custom, затем current
    fetch('/admin/init/custom')
        .then(res => res.json())
        .then(ob => {
            rootCustomtItems = ob;
            // Теперь загружаем current
            return fetch('/admin/init/current');
        })
        .then(res => res.json())
        .then(ob => {
            // Проверяем наличие accsdb
            if (!rootCustomtItems.accsdb) rootCustomtItems.accsdb = {};
            rootCustomtItems.accsdb.users = ob.accsdb && Array.isArray(ob.accsdb.users) ? ob.accsdb.users : [];

            // Сортировка по user.expires (по возрастанию даты, пустые в конце)
            rootCustomtItems.accsdb.users = [...rootCustomtItems.accsdb.users].sort((a, b) => {
                const aDate = a.expires ? new Date(a.expires).getTime() : Infinity;
                const bDate = b.expires ? new Date(b.expires).getTime() : Infinity;
                return aDate - bDate;
            });

            container.innerHTML = renderUsers(rootCustomtItems.accsdb.users);
            // После рендера таблицы навесить обработчики на кнопки "Редактировать"
            setTimeout(() => {
                if (rootCustomtItems && rootCustomtItems.accsdb && Array.isArray(rootCustomtItems.accsdb.users)) {
                    attachEditHandlers(rootCustomtItems.accsdb.users);
                }
            }, 0);
        })
        .catch(() => {
            container.innerHTML = '<div class="alert alert-danger">Ошибка загрузки пользователей</div>';
        });
}

function renderUsers(users: any[]): string {
    // Получаем текущую дату без времени для сравнения
    const now = new Date();
    now.setHours(0, 0, 0, 0);

    return `
        <table class="table table-bordered table-striped align-middle">
            <thead>
                <tr>
                    <th style="width:48px; text-align:center;"></th>
                    <th>ID</th>
                    <th>IDs</th>
                    <th>Истекает</th>
                    <th>Группа</th>
                    <th>Комментарий</th>
                    <th>Заблокирован</th>
                    <th>ban_msg</th>
                </tr>
            </thead>
            <tbody>
                ${users.map((user, idx) => {
                    // Проверка на просроченность и "оранжевый" статус
                    let isExpired = false;
                    let isWarning = false;
                    if (user.expires) {
                        const expiresDate = new Date(user.expires);
                        expiresDate.setHours(0, 0, 0, 0);
                        isExpired = expiresDate < now;
                        // Проверка на "оранжевый" (меньше 30 дней, но не просрочен)
                        const diffDays = Math.ceil((expiresDate.getTime() - now.getTime()) / (1000 * 60 * 60 * 24));
                        isWarning = !isExpired && diffDays <= 30;
                    }
                    // Проверка на бан
                    const isBanned = !!user.ban;

                    // Классы для выделения
                    let expiresClass = '';
                    if (isExpired) {
                        expiresClass = 'style="background:#ffdddd;"';
                    } else if (isWarning) {
                        expiresClass = 'style="background:#fff3cd;"'; // Bootstrap warning (light orange)
                    }
                    const banClass = isBanned ? 'style="background:#ffdddd;"' : '';

                    return `
                        <tr>
                            <td style="width:48px; text-align:center;">
                                <button type="button" class="btn btn-sm btn-light p-1" id="edit-user-btn-${idx}" title="Редактировать" style="width:32px; height:32px;">
                                    <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" fill="currentColor" class="bi bi-gear" viewBox="0 0 16 16">
                                      <path d="M8 4.754a3.246 3.246 0 1 0 0 6.492 3.246 3.246 0 0 0 0-6.492zM5.754 8a2.246 2.246 0 1 1 4.492 0 2.246 2.246 0 0 1-4.492 0z"/>
                                      <path d="M9.796 1.343c-.527-1.79-3.065-1.79-3.592 0l-.094.319a.873.873 0 0 1-1.255.52l-.292-.16c-1.64-.892-3.433.902-2.54 2.541l.159.292a.873.873 0 0 1-.52 1.255l-.319.094c-1.79.527-1.79 3.065 0 3.592l.319.094a.873.873 0 0 1 .52 1.255l-.16.292c-.892 1.64.901 3.434 2.541 2.54l.292-.159a.873.873 0 0 1 1.255.52l.094.319c.527 1.79 3.065 1.79 3.592 0l.094-.319a.873.873 0 0 1 1.255-.52l.292.16c1.64.893 3.434-.902 2.54-2.541l-.159-.292a.873.873 0 0 1 .52-1.255l.319-.094c1.79-.527 1.79-3.065 0-3.592l-.319-.094a.873.873 0 0 1-.52-1.255l.16-.292c.893-1.64-.902-3.433-2.541-2.54l-.292.159a.873.873 0 0 1-1.255-.52l-.094-.319zm-2.633.283c.246-.835 1.428-.835 1.674 0l.094.319a1.873 1.873 0 0 0 2.693 1.115l.291-.16c.764-.415 1.6.42 1.184 1.185l-.159.292a1.873 1.873 0 0 0 1.116 2.692l.318.094c.835.246.835 1.428 0 1.674l-.319.094a1.873 1.873 0 0 0-1.115 2.693l.16.291c.415.764-.42 1.6-1.185 1.184l-.291-.159a1.873 1.873 0 0 0-2.693 1.116l-.094.318c-.246.835-1.428.835-1.674 0l-.094-.319a1.873 1.873 0 0 0-2.692-1.115l-.292.16c-.764.415-1.6-.42-1.184-1.185l.159-.291A1.873 1.873 0 0 0 1.945 8.93l-.319-.094c-.835-.246-.835-1.428 0-1.674l.319-.094A1.873 1.873 0 0 0 3.06 4.377l-.16-.292c-.415-.764.42-1.6 1.185-1.184l.292.159a1.873 1.873 0 0 0 2.692-1.115l.094-.319z"/>
                                    </svg>
                                </button>
                            </td>
                            <td>${user.id || 'не указан'}</td>
                            <td>${Array.isArray(user.ids) ? user.ids.join('<br>') : ''}</td>
                            <td ${expiresClass}>${user.expires ? formatDate(user.expires) : ''}</td>
                            <td>${user.group || 0}</td>
                            <td>${user.comment || ''}</td>
                            <td ${banClass}>${user.ban ? 'Да' : 'Нет'}</td>
                            <td>${user.ban_msg || ''}</td>
                        </tr>
                    `;
                }).join('')}
            </tbody>
        </table>
    `;
}