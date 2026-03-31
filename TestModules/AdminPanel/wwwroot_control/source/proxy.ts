const proxyDefaults = {
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

function getProxyModalHtml(
    modalId: string,
    title: string,
    proxy?: any,
    showDelete = false
): string {
    return `
    <div class="modal fade" id="${modalId}" tabindex="-1" aria-labelledby="${modalId}Label" aria-hidden="true">
      <div class="modal-dialog modal-lg">
        <div class="modal-content">
          <div class="modal-header">
            <h5 class="modal-title" id="${modalId}Label">${title}</h5>
            <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Закрыть"></button>
          </div>
          <div class="modal-body">
            <form id="${modalId}-form">
              <div class="mb-3">
                <label for="${modalId}-proxy-name" class="form-label">Имя для доступа через - globalnameproxy</label>
                <input type="text" class="form-control" id="${modalId}-proxy-name" value="${proxy ? (proxy.name || '') : ''}" placeholder="tor" required>
              </div>
              <div class="mb-3">
                <label for="${modalId}-proxy-pattern" class="form-label">Использовать прокси с подходящим regex (не обязательно)</label>
                <input type="text" class="form-control" id="${modalId}-proxy-pattern" value="${escapeHtmlAttr(proxy ? (proxy.pattern || '') : '')}" placeholder="\\\\.onion">
              </div>
              <div class="mb-3 mt-5 d-flex align-items-center">
                <div class="form-check me-5">
                  <input class="form-check-input" type="checkbox" id="${modalId}-proxy-useAuth" ${proxy && proxy.useAuth ? 'checked' : ''}>
                  <label class="form-check-label" for="${modalId}-proxy-useAuth">Использовать авторизацию</label>
                </div>
                <div class="form-check">
                  <input class="form-check-input" type="checkbox" id="${modalId}-proxy-BypassOnLocal" ${proxy && proxy.BypassOnLocal ? 'checked' : ''}>
                  <label class="form-check-label" for="${modalId}-proxy-BypassOnLocal">Игнорировать localhost</label>
                </div>
              </div>
              <div class="mb-3">
                <label for="${modalId}-proxy-username" class="form-label">username</label>
                <input type="text" class="form-control" id="${modalId}-proxy-username" value="${proxy ? (proxy.username || '') : ''}" placeholder="не обязательно">
              </div>
              <div class="mb-3">
                <label for="${modalId}-proxy-password" class="form-label">password</label>
                <input type="text" class="form-control" id="${modalId}-proxy-password" value="${proxy ? (proxy.password || '') : ''}" placeholder="не обязательно">
              </div>
              <div class="mb-3">
                <label for="${modalId}-proxy-pattern_auth" class="form-label">pattern_auth</label>
                <input type="text" class="form-control" id="${modalId}-proxy-pattern_auth" value="${escapeHtmlAttr(proxy ? (proxy.pattern_auth || '') : '')}" placeholder="^(?<sheme>[^/]+//)?(?<username>[^:/]+):(?<password>[^@]+)@(?<host>.*)">
              </div>
              <div class="mb-3 mt-5">
                <label for="${modalId}-proxy-list" class="form-label">Список (через запятую)</label>
                <input type="text" class="form-control" id="${modalId}-proxy-list" value="${proxy && Array.isArray(proxy.list) ? proxy.list.join(', ') : ''}" placeholder="socks5://127.0.0.1:9050, http://127.0.0.1:5481">
              </div>
              <div class="mb-3">
                <label for="${modalId}-proxy-file" class="form-label">Файл списком</label>
                <input type="text" class="form-control" id="${modalId}-proxy-file" value="${proxy ? (proxy.file || '') : ''}" placeholder="myproxy/pl.txt">
              </div>
              <div class="mb-3">
                <label for="${modalId}-proxy-url" class="form-label">URL на список</label>
                <input type="text" class="form-control" id="${modalId}-proxy-url" value="${proxy ? (proxy.url || '') : ''}" placeholder="https://asocks-list.org/userid.txt?type=res&country=UA">
              </div>
              <div class="mb-3">
                <label for="${modalId}-proxy-refresh_uri" class="form-label">Refresh URI</label>
                <input type="text" class="form-control" id="${modalId}-proxy-refresh_uri" value="${proxy ? (proxy.refresh_uri || '') : ''}" placeholder="http://example.com/refresh">
              </div>
              <div class="mb-3">
                <label for="${modalId}-proxy-maxRequestError" class="form-label">Количество ошибок подряд для смены прокси</label>
                <input type="number" class="form-control" id="${modalId}-proxy-maxRequestError" value="${proxy ? proxy.maxRequestError : 2}">
              </div>
            </form>
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

function renderProxiesPage(containerId: string) {
    const container = document.getElementById(containerId);
    if (!container) return;

    const addModalHtml = getProxyModalHtml('addProxyModal', 'Добавить прокси');

    container.innerHTML = `
        ${addModalHtml}
        <div id="edit-proxy-modal-container"></div>
        <div class="d-flex justify-content-between align-items-center mb-3">
            <h1 class="mb-0">Прокси</h1>
            <div>
                <button type="button" class="btn btn-success" id="btn-add-proxy">Добавить прокси</button>
                <button type="button" class="btn btn-primary" id="btn-save-proxies" style="display: none;">Сохранить</button>
            </div>
        </div>
        <div id="proxies-list" class="row g-3"></div>
    `;
    loadAndRenderProxies('proxies-list');

    setTimeout(() => {
        const btnSave = document.getElementById('btn-save-proxies');
        if (btnSave) {
            btnSave.onclick = () => {
                if (rootCustomtItems) {
                    saveCustomtItems();
                } else {
                    alert('Данные не загружены');
                }
            };
        }

        const btnAdd = document.getElementById('btn-add-proxy');
        if (btnAdd) {
            btnAdd.onclick = () => {
                // @ts-ignore
                const modal = new bootstrap.Modal(document.getElementById('addProxyModal'));
                modal.show();
            };
        }

        const saveProxyBtn = document.getElementById('addProxyModal-save-btn');
        if (saveProxyBtn) {
            saveProxyBtn.onclick = () => {
                const name = (document.getElementById('addProxyModal-proxy-name') as HTMLInputElement).value.trim();
                const pattern = (document.getElementById('addProxyModal-proxy-pattern') as HTMLInputElement).value.trim();
                const useAuth = (document.getElementById('addProxyModal-proxy-useAuth') as HTMLInputElement).checked;
                const BypassOnLocal = (document.getElementById('addProxyModal-proxy-BypassOnLocal') as HTMLInputElement).checked;
                const username = (document.getElementById('addProxyModal-proxy-username') as HTMLInputElement).value.trim();
                const password = (document.getElementById('addProxyModal-proxy-password') as HTMLInputElement).value.trim();
                const pattern_auth = (document.getElementById('addProxyModal-proxy-pattern_auth') as HTMLInputElement).value.trim();
                const maxRequestError = parseInt((document.getElementById('addProxyModal-proxy-maxRequestError') as HTMLInputElement).value, 10) || 0;
                const file = (document.getElementById('addProxyModal-proxy-file') as HTMLInputElement).value.trim();
                const url = (document.getElementById('addProxyModal-proxy-url') as HTMLInputElement).value.trim();
                const listRaw = (document.getElementById('addProxyModal-proxy-list') as HTMLInputElement).value.trim();
                const refresh_uri = (document.getElementById('addProxyModal-proxy-refresh_uri') as HTMLInputElement).value.trim();

                if (!name && !pattern) {
                    alert('Заполните обязательное поле "Имя" или "pattern"');
                    return;
                }

                const list = listRaw ? listRaw.split(',').map(s => s.trim()).filter(Boolean) : [];

                const newProxy = {
                    name,
                    pattern,
                    useAuth,
                    BypassOnLocal,
                    username,
                    password,
                    pattern_auth,
                    maxRequestError,
                    file,
                    url,
                    list,
                    refresh_uri
                };

                // Удаление пустых и дефолтных значений из proxy
                Object.keys(proxyDefaults).forEach(key => {
                    const value = newProxy[key as keyof typeof proxyDefaults];
                    const def = proxyDefaults[key as keyof typeof proxyDefaults];
                    if (
                        value === undefined ||
                        value === null ||
                        (typeof def === 'string' && (value === '' || value === def)) ||
                        (typeof def === 'number' && value === def) ||
                        (typeof def === 'boolean' && value === def) ||
                        (Array.isArray(def) && Array.isArray(value) && value.length === 0)
                    ) {
                        delete newProxy[key as keyof typeof proxyDefaults];
                    }
                });

                if (rootCustomtItems && rootCustomtItems["globalproxy"] && Array.isArray(rootCustomtItems["globalproxy"])) {
                    rootCustomtItems["globalproxy"].push(newProxy);
                    const proxiesList = document.getElementById('proxies-list');
                    if (proxiesList) {
                        proxiesList.innerHTML = renderProxies(rootCustomtItems["globalproxy"]);
                        attachEditProxyHandlers(rootCustomtItems["globalproxy"]);
                    }
                }

                // @ts-ignore
                const modal = bootstrap.Modal.getInstance(document.getElementById('addProxyModal'));
                if (modal) modal.hide();

                const btnSave = document.getElementById('btn-save-proxies');
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

function attachEditProxyHandlers(proxies: any[]) {
    proxies.forEach((proxy: any, idx: number) => {
        const btn = document.getElementById(`edit-proxy-btn-${idx}`);
        if (btn) {
            btn.onclick = () => {
                const editModalId = `editProxyModal-${idx}`;
                const editModalHtml = getProxyModalHtml(editModalId, 'Редактировать прокси', proxy, true);
                const editModalContainer = document.getElementById('edit-proxy-modal-container');
                if (editModalContainer) {
                    editModalContainer.innerHTML = editModalHtml;
                }
                // @ts-ignore
                const modal = new bootstrap.Modal(document.getElementById(editModalId));
                modal.show();

                setTimeout(() => {
                    const saveBtn = document.getElementById(`${editModalId}-save-btn`);
                    if (saveBtn) {
                        saveBtn.onclick = () => {
                            const name = (document.getElementById(`${editModalId}-proxy-name`) as HTMLInputElement).value.trim();
                            const pattern = (document.getElementById(`${editModalId}-proxy-pattern`) as HTMLInputElement).value.trim();
                            const useAuth = (document.getElementById(`${editModalId}-proxy-useAuth`) as HTMLInputElement).checked;
                            const BypassOnLocal = (document.getElementById(`${editModalId}-proxy-BypassOnLocal`) as HTMLInputElement).checked;
                            const username = (document.getElementById(`${editModalId}-proxy-username`) as HTMLInputElement).value.trim();
                            const password = (document.getElementById(`${editModalId}-proxy-password`) as HTMLInputElement).value.trim();
                            const pattern_auth = (document.getElementById(`${editModalId}-proxy-pattern_auth`) as HTMLInputElement).value.trim();
                            const maxRequestError = parseInt((document.getElementById(`${editModalId}-proxy-maxRequestError`) as HTMLInputElement).value, 10) || 0;
                            const file = (document.getElementById(`${editModalId}-proxy-file`) as HTMLInputElement).value.trim();
                            const url = (document.getElementById(`${editModalId}-proxy-url`) as HTMLInputElement).value.trim();
                            const listRaw = (document.getElementById(`${editModalId}-proxy-list`) as HTMLInputElement).value.trim();
                            const refresh_uri = (document.getElementById(`${editModalId}-proxy-refresh_uri`) as HTMLInputElement).value.trim();

                            if (!name && !pattern) {
                                alert('Заполните обязательное поле "Имя" или "pattern"');
                                return;
                            }

                            const list = listRaw ? listRaw.split(',').map(s => s.trim()).filter(Boolean) : [];

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
                            Object.keys(proxyDefaults).forEach(key => {
                                const value = proxy[key as keyof typeof proxyDefaults];
                                const def = proxyDefaults[key as keyof typeof proxyDefaults];
                                if (
                                    value === undefined ||
                                    value === null ||
                                    (typeof def === 'string' && (value === '' || value === def)) ||
                                    (typeof def === 'number' && value === def) ||
                                    (typeof def === 'boolean' && value === def) ||
                                    (Array.isArray(def) && Array.isArray(value) && value.length === 0)
                                ) {
                                    delete proxy[key as keyof typeof proxyDefaults];
                                }
                            });

                            const proxiesList = document.getElementById('proxies-list');
                            if (proxiesList) {
                                proxiesList.innerHTML = renderProxies(rootCustomtItems["globalproxy"]);
                                attachEditProxyHandlers(rootCustomtItems["globalproxy"]);
                            }

                            // @ts-ignore
                            const modal = bootstrap.Modal.getInstance(document.getElementById(editModalId));
                            if (modal) modal.hide();

                            const btnSave = document.getElementById('btn-save-proxies');
                            if (btnSave) {
                                btnSave.click();
                            }
                        };
                    }

                    const deleteBtn = document.getElementById(`${editModalId}-delete-btn`);
                    if (deleteBtn) {
                        deleteBtn.onclick = () => {
                            if (confirm('Удалить прокси?')) {
                                if (rootCustomtItems && rootCustomtItems["globalproxy"] && Array.isArray(rootCustomtItems["globalproxy"])) {
                                    const proxiesArr = rootCustomtItems["globalproxy"];
                                    const proxyIdx = proxiesArr.indexOf(proxy);
                                    if (proxyIdx !== -1) {
                                        proxiesArr.splice(proxyIdx, 1);
                                        const proxiesList = document.getElementById('proxies-list');
                                        if (proxiesList) {
                                            proxiesList.innerHTML = renderProxies(proxiesArr);
                                            attachEditProxyHandlers(proxiesArr);
                                        }
                                        // @ts-ignore
                                        const modal = bootstrap.Modal.getInstance(document.getElementById(editModalId));
                                        if (modal) modal.hide();
                                        const btnSave = document.getElementById('btn-save-proxies');
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

function loadAndRenderProxies(containerId: string) {
    const container = document.getElementById(containerId);
    if (!container) return;

    fetch('/admin/init/custom')
        .then(res => res.json())
        .then(ob => {
            rootCustomtItems = ob;
            return fetch('/admin/init/current');
        })
        .then(res => res.json())
        .then(ob => {
            if (!rootCustomtItems.globalproxy) rootCustomtItems.globalproxy = [];
            rootCustomtItems.globalproxy = Array.isArray(ob.globalproxy) ? ob.globalproxy : [];

            container.innerHTML = renderProxies(rootCustomtItems.globalproxy);
            setTimeout(() => {
                if (rootCustomtItems && rootCustomtItems.globalproxy && Array.isArray(rootCustomtItems.globalproxy)) {
                    attachEditProxyHandlers(rootCustomtItems.globalproxy);
                }
            }, 0);
        })
        .catch(() => {
            container.innerHTML = '<div class="alert alert-danger">Ошибка загрузки прокси</div>';
        });
}

function renderProxies(proxies: any[]): string {
    return `
        <table class="table table-bordered table-striped align-middle">
            <thead>
                <tr>
                    <th style="width:48px; text-align:center;"></th>
                    <th>Имя</th>
                    <th>Pattern</th>
                    <th>auth</th>
                    <th>list</th>
                    <th>url</th>
                    <th>file</th>
                    <th>refresh_uri</th>
                </tr>
            </thead>
            <tbody>
                ${proxies.map((proxy, idx) => {
                    return `
                        <tr>
                            <td style="width:48px; text-align:center;">
                                <button type="button" class="btn btn-sm btn-light p-1" id="edit-proxy-btn-${idx}" title="Редактировать" style="width:32px; height:32px;">
                                    <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" fill="currentColor" class="bi bi-gear" viewBox="0 0 16 16">
                                      <path d="M8 4.754a3.246 3.246 0 1 0 0 6.492 3.246 3.246 0 0 0 0-6.492zM5.754 8a2.246 2.246 0 1 1 4.492 0 2.246 2.246 0 0 1-4.492 0z"/>
                                      <path d="M9.796 1.343c-.527-1.79-3.065-1.79-3.592 0l-.094.319a.873.873 0 0 1-1.255.52l-.292-.16c-1.64-.892-3.433.902-2.54 2.541l.159.292a.873.873 0 0 1-.52 1.255l-.319.094c-1.79.527-1.79 3.065 0 3.592l.319.094a.873.873 0 0 1 .52 1.255l-.16.292c-.892 1.64.901 3.434 2.541 2.54l.292-.159a.873.873 0 0 1 1.255.52l.094.319c.527 1.79 3.065 1.79 3.592 0l.094-.319a.873.873 0 0 1 1.255-.52l.292.16c1.64.893 3.434-.902 2.54-2.541l-.159-.292a.873.873 0 0 1 .52-1.255l.319-.094c1.79-.527 1.79-3.065 0-3.592l-.319-.094a.873.873 0 0 1-.52-1.255l.16-.292c.893-1.64-.902-3.433-2.541-2.54l-.292.159a.873.873 0 0 1-1.255-.52l-.094-.319zm-2.633.283c.246-.835 1.428-.835 1.674 0l.094.319a1.873 1.873 0 0 0 2.693 1.115l.291-.16c.764-.415 1.6.42 1.184 1.185l-.159.292a1.873 1.873 0 0 0 1.116 2.692l.318.094c.835.246.835 1.428 0 1.674l-.319.094a1.873 1.873 0 0 0-1.115 2.693l.16.291c.415.764-.42 1.6-1.185 1.184l-.291-.159a1.873 1.873 0 0 0-2.693 1.116l-.094.318c-.246.835-1.428.835-1.674 0l-.094-.319a1.873 1.873 0 0 0-2.692-1.115l-.292.16c-.764.415-1.6-.42-1.184-1.185l.159-.291A1.873 1.873 0 0 0 1.945 8.93l-.319-.094c-.835-.246-.835-1.428 0-1.674l.319-.094A1.873 1.873 0 0 0 3.06 4.377l-.16-.292c-.415-.764.42-1.6 1.185-1.184l.292.159a1.873 1.873 0 0 0 2.692-1.115l.094-.319z"/>
                                    </svg>
                                </button>
                            </td>
                            <td>${proxy.name || ''}</td>
                            <td>${escapeHtmlAttr(proxy.pattern || '')}</td>
                            <td>${proxy.useAuth ? 'Да' : 'Нет'}</td>
                            <td>${Array.isArray(proxy.list) ? proxy.list.join('<br>') : ''}</td>
                            <td>${proxy.url || ''}</td>
                            <td>${proxy.file || ''}</td>
                            <td>${proxy.refresh_uri || ''}</td>
                        </tr>
                    `;
                }).join('')}
            </tbody>
        </table>
    `;
}