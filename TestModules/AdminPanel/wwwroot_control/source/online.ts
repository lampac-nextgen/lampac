// Массив балансеров будет формироваться динамически
let balancers: string[] = [];

function loadCustomAndCurrent(container?: HTMLElement, onLoaded?: () => void) {
    function checkLoaded() {
        if (onLoaded) {
            // Формируем balancers только после загрузки rootDefaultItems
            if (rootDefaultItems) {
                balancers = Object.keys(rootDefaultItems).filter(key => rootDefaultItems[key] && typeof rootDefaultItems[key].kit !== 'undefined' && rootDefaultItems[key].rip !== true);
            } else {
                balancers = [];
            }
            onLoaded();
        }
    }

    fetch('/admin/init/custom')
        .then(res => res.json())
        .then(ob => {
            rootCustomtItems = ob;
            checkLoaded();
        })
        .catch(() => {
            if (container) {
                container.innerHTML = `<div class="alert alert-danger">Ошибка загрузки rootCustomtItems</div>`;
            }
        });
}

function renderOnlinePage(containerId: string) {
    const container = document.getElementById(containerId);
    if (!container) return;

    // Базовые поля по умолчанию
    const defaultBalancer: any = {
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
    const ignoreKeys = ['rip', 'proxy', 'plugin', 'apn'];
    const ignoreSaveKeys = ['headers', 'headers_stream'];

    // Функция для рендера формы по balancer
    function renderBalancerForm(balancer: string) {

        let balancerDefaults = { ...defaultBalancer };
        if (balancer === "FilmixTV") {
            balancerDefaults = {
                user_apitv: "",
                passwd_apitv: "",
                tokens: [""],
                ...balancerDefaults
            };
        }
        else if (balancer === "FilmixPartner") {
            balancerDefaults = {
                APIKEY: "",
                APISECRET: "",
                user_name: "",
                user_passw: "",
                lowlevel_api_passw: "",
                ...balancerDefaults,
                tokens: [""]
            };
        }
        else if (balancer === "Rezka" || balancer === "RezkaPrem") {
            balancerDefaults = {
                login: "",
                passwd: "",
                ...balancerDefaults
            };
        }
        else if (balancer === "VideoCDN") {
            balancerDefaults = {
                clientId: "",
                iframehost: "",
                username: "",
                password: "",
                domain: "",
                ...balancerDefaults
            };
        }
        else if (balancer === "KinoPub") {
            balancerDefaults = {
                filetype: "",
                tokens: [""],
                ...balancerDefaults
            };
        }
        else if (balancer === "Alloha" || balancer === "Mirage" || balancer === "Kodik") {
            balancerDefaults = {
                secret_token: "",
                linkhost: "",
                ...balancerDefaults
            };
        }

        const current = (rootDefaultItems && rootDefaultItems[balancer]) ? rootDefaultItems[balancer] : {};
        const custom = (rootCustomtItems && rootCustomtItems[balancer]) ? rootCustomtItems[balancer] : {};
        const allKeys = Array.from(new Set([
            ...Object.keys(balancerDefaults),
            ...Object.keys(current),
            ...Object.keys(custom)
        ])).filter(key => !ignoreKeys.includes(key));

        const data = { ...balancerDefaults, ...current };
        const mainContent = document.getElementById('online-main-content');
        if (!mainContent) return;

        function renderObjectAsText(obj: any): string {
            if (!obj) return '';
            return Object.entries(obj)
                .map(([k, v]) => `<b>${k}</b>: ${JSON.stringify(v)}`)
                .join(',<br>');
        }

        // Ключи, для которых нужно добавить "- через запятую"
        const arrayCommaKeys = ['rhub_geo_disable', 'geo_hide', 'overridehosts', 'geostreamproxy', 'tokens'];

        mainContent.innerHTML = `
            <div class="d-flex justify-content-between align-items-center mb-3">
                <h1 class="mb-0">${balancer}</h1>
                <button type="button" class="btn btn-primary" id="balancer-save-btn">Сохранить</button>
            </div>
            <form id="balancer-form">
                ${allKeys
                .sort((a, b) => {
                    const aIsBool = typeof data[a] === 'boolean';
                    const bIsBool = typeof data[b] === 'boolean';
                    if (aIsBool === bIsBool) return 0;
                    return aIsBool ? 1 : -1;
                })
                .map((key) => {
                    const defValue = balancerDefaults[key];
                    const value = current[key];
                    const customValue = custom[key];

                    if (key === 'headers' || key === 'headers_stream') {
                        // Не выводить, если пустой объект или undefined/null
                        const obj = { ...(customValue || value || defValue) };
                        const balancerKey = balancer; // для замыкания
                        const modalId = 'online-modal';

                        // Генерация строк для редактирования
                        const rows = Object.entries(obj)
                            .map(([k, v], idx) => `
                                <div class="row mb-2 align-items-center" data-row>
                                    <div class="col-5">
                                        <input type="text" class="form-control form-control-sm" name="header-key" value="${k}" data-idx="${idx}">
                                    </div>
                                    <div class="col-5">
                                        <input type="text" class="form-control form-control-sm" name="header-value" value="${escapeHtmlAttr(typeof v === 'object' ? JSON.stringify(v) : v)}" data-idx="${idx}">
                                    </div>
                                    <div class="col-2 text-end">
                                        <button type="button" class="btn btn-danger btn-sm" data-remove-row>&times;</button>
                                    </div>
                                </div>
                            `).join('');

                        // Добавляем модальное окно (один раз)
                        if (!document.getElementById(modalId)) {
                            const modalHtml = `
                                <div class="modal fade" id="${modalId}" tabindex="-1" aria-labelledby="online-modal-label" aria-hidden="true">
                                  <div class="modal-dialog modal-xl">
                                    <div class="modal-content">
                                      <div class="modal-header">
                                        <h5 class="modal-title" id="online-modal-label">Редактировать ${key}</h5>
                                        <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Закрыть"></button>
                                      </div>
                                      <div class="modal-body" id="online-modal-body"></div>
                                      <div class="modal-footer">
                                        <button type="button" class="btn btn-secondary me-2" data-bs-dismiss="modal">Закрыть</button>
                                        <button type="button" class="btn btn-primary" id="modal-save-btn">Сохранить</button>
                                      </div>
                                    </div>
                                  </div>
                                </div>
                            `;
                            document.body.insertAdjacentHTML('beforeend', modalHtml);
                        }

                        // Кнопка для показа модального окна
                        setTimeout(() => {
                            const btn = document.getElementById(`show-modal-${key}`);
                            if (btn) {
                                btn.onclick = function () {
                                    const modalBody = document.getElementById('online-modal-body');
                                    if (modalBody) {
                                        modalBody.innerHTML = `
                                                <form id="modal-headers-form">
                                                    <div id="modal-headers-rows">
                                                        ${rows}
                                                    </div>
                                                    <button type="button" class="btn btn-success btn-sm mt-2" id="add-header-row">Добавить</button>
                                                </form>
                                            `;

                                        // Добавить новую строку
                                        const addBtn = document.getElementById('add-header-row');
                                        if (addBtn) {
                                            addBtn.onclick = function () {
                                                const rowsDiv = document.getElementById('modal-headers-rows');
                                                if (rowsDiv) {
                                                    rowsDiv.insertAdjacentHTML('beforeend', `
                                                        <div class="row mb-2 align-items-center" data-row>
                                                            <div class="col-5">
                                                                <input type="text" class="form-control form-control-sm" name="header-key" value="">
                                                            </div>
                                                            <div class="col-5">
                                                                <input type="text" class="form-control form-control-sm" name="header-value" value="">
                                                            </div>
                                                            <div class="col-2 text-end">
                                                                <button type="button" class="btn btn-danger btn-sm" data-remove-row>&times;</button>
                                                            </div>
                                                        </div>
                                                    `);
                                                }
                                            };
                                        }

                                        // Удаление строки
                                        modalBody.addEventListener('click', function (e) {
                                            const target = e.target as HTMLElement;
                                            if (target && target.hasAttribute('data-remove-row')) {
                                                const row = target.closest('[data-row]');
                                                if (row) row.remove();
                                            }
                                        });

                                        // Сохранение
                                        const saveBtn = document.getElementById('modal-save-btn');
                                        if (saveBtn) {
                                            saveBtn.onclick = function () {
                                                const form = document.getElementById('modal-headers-form') as HTMLFormElement;
                                                if (!form) return;
                                                const keys = Array.from(form.querySelectorAll('input[name="header-key"]')) as HTMLInputElement[];
                                                const values = Array.from(form.querySelectorAll('input[name="header-value"]')) as HTMLInputElement[];
                                                const newObj: any = {};
                                                for (let i = 0; i < keys.length; i++) {
                                                    const k = keys[i].value.trim();
                                                    let v = values[i].value;
                                                    if (!k) continue;
                                                    // Попытка распарсить JSON, иначе строка
                                                    try {
                                                        v = JSON.parse(v);
                                                    } catch { /* оставить строкой */ }
                                                    newObj[k] = v;
                                                }
                                                // Обновляем rootCustomtItems
                                                if (!rootCustomtItems) rootCustomtItems = {};
                                                if (!rootCustomtItems[balancerKey]) rootCustomtItems[balancerKey] = {};
                                                rootCustomtItems[balancerKey][key] = newObj;

                                                // Закрыть модалку
                                                // @ts-ignore
                                                const modal = bootstrap.Modal.getInstance(document.getElementById(modalId));
                                                if (modal) modal.hide();

                                                // Перерисовать форму
                                                renderBalancerForm(balancerKey);

                                                // Автоматически нажимаем на кнопку "Сохранить" основной формы
                                                const balancerSaveBtn = document.getElementById('balancer-save-btn');
                                                if (balancerSaveBtn) {
                                                    balancerSaveBtn.click();
                                                }
                                            };
                                        }
                                    }
                                    // Открываем модальное окно через Bootstrap API
                                    // @ts-ignore
                                    const modal = new bootstrap.Modal(document.getElementById(modalId));
                                    modal.show();
                                };
                            }
                        }, 0);

                        return `
                            <div class="mb-2">
                                <b id="show-modal-${key}" style="cursor:pointer; color:#0d6efd;">${key}</b>
                            </div>
                            <div class="mb-3" style="font-family:monospace; background:#f8f9fa; border-radius:4px; padding:8px;">
                                ${renderObjectAsText(obj)}
                            </div>
                        `;
                    }
                    else if (key === 'vast') {
                        return `
                                <div class="mb-2"><b>vast</b></div>
                                <div class="mb-3 ms-3">
                                    <label class="form-label">url</label>
                                    <input type="text" class="form-control" name="vast.url" value="${(customValue && customValue.url) ? customValue.url : ''}" placeholder="${(value && value.url) ?? ''}">
                                </div>
                                <div class="mb-3 ms-3">
                                    <label class="form-label">msg</label>
                                    <input type="text" class="form-control" name="vast.msg" value="${(customValue && customValue.msg) ? customValue.msg : ''}" placeholder="${(value && value.msg) ?? ''}">
                                </div>
                            `;
                    }
                    else if (typeof (customValue ?? value ?? defValue) === 'object' && !Array.isArray(customValue ?? value ?? defValue)) return '';
                    else if (Array.isArray(customValue ?? value ?? defValue)) {
                        const commaLabel = arrayCommaKeys.includes(key) ? ' <span class="text-muted">- через запятую</span>' : '';
                        return `
                                <div class="mb-3">
                                    <label class="form-label">${key}${commaLabel}</label>
                                    <input type="text" class="form-control" name="${key}" value="${Array.isArray(customValue) ? customValue.join(', ') : ''}" placeholder="${Array.isArray(value) ? value.join(', ') : ''}">
                                </div>
                            `;
                    }
                    else if (typeof value === 'boolean') {
                        // Для boolean: если customValue определён, используем его, иначе value
                        const checked = (typeof customValue === 'boolean' ? customValue : value) ? 'checked' : '';
                        const notDefault = (typeof customValue === 'boolean') ? ' <span class="text-warning">(not default)</span>' : '';
                        return `
                            <div class="form-check mb-3">
                                <input class="form-check-input" type="checkbox" name="${key}" id="balancer-${key}" ${checked}>
                                <label class="form-check-label" for="balancer-${key}">${key}${notDefault}</label>
                            </div>
                        `;
                    }
                    return `
                            <div class="mb-3">
                                <label class="form-label">${key}</label>
                                <input type="text" class="form-control" name="${key}" value="${customValue !== undefined && customValue !== null ? customValue : ''}" placeholder="${value !== undefined && value !== null ? value : ''}">
                            </div>
                        `;
                }).join('')}
            <button type="button" class="btn btn-primary mt-4" id="balancer-save-btn2">Сохранить</button></form>
        `;

        const saveBtn2 = document.getElementById('balancer-save-btn2');
        if (saveBtn2) {
            saveBtn2.onclick = function () {
                const saveBtn = document.getElementById('balancer-save-btn');
                if (saveBtn) {
                    saveBtn.click();
                }
            };
        }

        // Обработчик кнопки "Сохранить"
        const saveBtn = document.getElementById('balancer-save-btn');
        if (saveBtn) {
            saveBtn.onclick = function () {
                const form = document.getElementById('balancer-form') as HTMLFormElement;
                if (!form) return;

                // Собираем значения из формы
                const formData = new FormData(form);
                const updated: any = {};

                allKeys.forEach(key => {
                    if (ignoreSaveKeys.includes(key)) return;

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
                    if (
                        typeof current[key] === 'number' &&
                        Number.isInteger(current[key])
                    ) {
                        const val = formData.get(key);
                        updated[key] = val !== null && val !== undefined && val !== ""
                            ? parseInt(val as string, 10)
                            : 0;
                        return;
                    }

                    // массивы
                    if (Array.isArray(defaultBalancer[key]) || Array.isArray(current[key])) {
                        const val = (formData.get(key) as string) || "";
                        updated[key] = val.split(',').map(s => s.trim()).filter(Boolean);
                        return;
                    }

                    // обычные поля
                    updated[key] = formData.get(key) ?? "";
                });

                // Обновляем rootCustomtItems
                if (!rootCustomtItems) rootCustomtItems = {};
                rootCustomtItems[balancer] = { ...rootCustomtItems[balancer], ...updated };

                // Удаляем из rootCustomtItems[balancer] пустые поля и поля, совпадающие с rootDefaultItems
                if (rootCustomtItems[balancer]) {
                    const curr = rootDefaultItems && rootDefaultItems[balancer] ? rootDefaultItems[balancer] : {};
                    Object.keys(rootCustomtItems[balancer]).forEach(key => {
                        const val = rootCustomtItems[balancer][key];

                        // Специальная проверка для vast: если vast.url пустой, удалить весь vast
                        if (
                            key === "vast" &&
                            val &&
                            typeof val === "object" &&
                            ("url" in val) &&
                            (val.url === undefined || val.url === null || val.url === "")
                        ) {
                            delete rootCustomtItems[balancer][key];
                            return;
                        }

                        // Удаляем пустые строки, пустые массивы, пустые объекты
                        if (
                            val === "" ||
                            (Array.isArray(val) && (val.length === 0 || (val.length === 1 && val[0] === ""))) ||
                            (typeof val === "object" && val !== null && !Array.isArray(val) && Object.keys(val).length === 0)
                        ) {
                            delete rootCustomtItems[balancer][key];
                            return;
                        }

                        // Удаляем если совпадает с rootDefaultItems
                        if (
                            typeof val === "object" && val !== null && !Array.isArray(val) && curr[key] &&
                            JSON.stringify(val) === JSON.stringify(curr[key])
                        ) {
                            delete rootCustomtItems[balancer][key];
                            return;
                        }
                        if (
                            Array.isArray(val) && Array.isArray(curr[key]) &&
                            JSON.stringify(val) === JSON.stringify(curr[key])
                        ) {
                            delete rootCustomtItems[balancer][key];
                            return;
                        }
                        if (
                            (typeof val === "string" || typeof val === "number" || typeof val === "boolean") &&
                            val === curr[key]
                        ) {
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
    loadCustomAndCurrent(container, () => {
        // Формируем HTML для бокового меню динамически
        const sidebarHtml = `
            <div class="list-group" id="balancer-sidebar">
                ${balancers.map((balancer, idx) => `
                    <a href="#" class="list-group-item list-group-item-action${idx === 0 ? ' active' : ''}" data-balancer="${balancer}">${balancer}</a>
                `).join('')}
            </div>
        `;

        // Добавляем кастомный стиль для активного пункта
        const styleId = 'online-balancer-active-style';
        if (!document.getElementById(styleId)) {
            const style = document.createElement('style');
            style.id = styleId;
            style.innerHTML = `
                #balancer-sidebar .list-group-item.active {
                    background: linear-gradient(90deg, #0d6efd 60%, #3a8bfd 100%);
                    color: #fff;
                    font-weight: bold;
                    border-color: #0d6efd;
                    box-shadow: 0 2px 8px rgba(13,110,253,0.10);
                }
                #balancer-sidebar .list-group-item.active:focus, 
                #balancer-sidebar .list-group-item.active:hover {
                    background: linear-gradient(90deg, #0b5ed7 60%, #2563eb 100%);
                    color: #fff;
                }
            `;
            document.head.appendChild(style);
        }

        container.innerHTML = `
            <div class="row">
                <div class="col-12 col-md-2 mb-3 mb-md-0">
                    ${sidebarHtml}
                </div>
                <div class="col-12 col-md-10" id="online-main-content" style="margin-bottom: 1.2em; background-color: #fdfdfd; padding: 10px; border-radius: 8px; box-shadow: 0 18px 24px rgb(175 175 175 / 30%);"></div>
            </div>
        `;

        // По умолчанию показываем первый balancer, если есть
        if (balancers.length > 0) {
            renderBalancerForm(balancers[0]);
        }

        // Навешиваем обработчик на меню после рендера
        setTimeout(() => {
            const sidebar = document.getElementById('balancer-sidebar');
            if (sidebar) {
                sidebar.querySelectorAll('a[data-balancer]').forEach(link => {
                    link.addEventListener('click', function (e) {
                        e.preventDefault();
                        // Снимаем активный класс со всех
                        sidebar.querySelectorAll('.list-group-item').forEach(item => item.classList.remove('active'));
                        // Добавляем активный класс текущему
                        (e.currentTarget as HTMLElement).classList.add('active');
                        const balancer = (e.currentTarget as HTMLElement).getAttribute('data-balancer');
                        if (!balancer) return;
                        renderBalancerForm(balancer);
                    });
                });
            }
        }, 0);
    });
}