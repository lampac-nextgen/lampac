function loadCustomAndDefault(container?: HTMLElement, onLoaded?: () => void) {
    let loaded = 0;
    let hasError = false;

    function checkLoaded() {
        loaded++;
        if (loaded === 2 && !hasError && onLoaded) {
            onLoaded();
        }
    }

    fetch('/admin/init/custom')
        .then(res => res.json())
        .then(data => {
            rootCustomtItems = data;
            checkLoaded();
        })
        .catch(() => {
            hasError = true;
            if (container) {
                container.innerHTML = `<div class="alert alert-danger">Ошибка загрузки customItems</div>`;
            }
        });

    checkLoaded();
}


function renderBaseForm() {

    const BASE_FIXED_VALUES = {
        "multiaccess": rootCustomtItems?.multiaccess ?? rootDefaultItems?.multiaccess ?? false,
        "mikrotik": rootCustomtItems?.mikrotik ?? rootDefaultItems?.mikrotik ?? false,
        "typecache": rootCustomtItems?.typecache ??  "",
        "imagelibrary": rootCustomtItems?.imagelibrary ??  "",
        "pirate_store": rootCustomtItems?.pirate_store ?? rootDefaultItems?.pirate_store ?? true,
        "apikey": rootCustomtItems?.apikey ??  "",
        "litejac": rootCustomtItems?.litejac ?? rootDefaultItems?.litejac ?? true,
        "filelog": rootCustomtItems?.filelog ?? rootDefaultItems?.filelog ?? false,
        "disableEng": rootCustomtItems?.disableEng ?? rootDefaultItems?.disableEng ?? false,
        "anticaptchakey": rootCustomtItems?.anticaptchakey ??  "",
        "omdbapi_key": rootCustomtItems?.omdbapi_key ??  "",
        "playerInner": rootCustomtItems?.playerInner ??"",
        "defaultOn": rootCustomtItems?.defaultOn ??"enable",
        "real_ip_cf": rootCustomtItems?.real_ip_cf ?? rootDefaultItems?.real_ip_cf ?? false,
        "corsehost": rootCustomtItems?.corsehost ?? ""
    };

    const BASE_DEFAULT_VALUES: { [key: string]: any } = {
        "multiaccess": rootDefaultItems?.multiaccess ?? false,
        "mikrotik": rootDefaultItems?.mikrotik ?? false,
        "typecache": rootDefaultItems?.typecache ?? "",
        "imagelibrary": rootDefaultItems?.imagelibrary ?? "",
        "pirate_store": rootDefaultItems?.pirate_store ?? true,
        "apikey": rootDefaultItems?.apikey ?? "",
        "litejac": rootDefaultItems?.litejac ?? true,
        "filelog": rootDefaultItems?.filelog ?? false,
        "disableEng": rootDefaultItems?.disableEng ?? false,
        "anticaptchakey": rootDefaultItems?.anticaptchakey ?? "",
        "omdbapi_key": rootDefaultItems?.omdbapi_key ?? "",
        "playerInner": rootDefaultItems?.playerInner ?? "",
        "defaultOn": rootDefaultItems?.defaultOn ?? "enable",
        "real_ip_cf": rootDefaultItems?.real_ip_cf ?? false,
        "corsehost": rootDefaultItems?.corsehost ?? ""
    };

    let html = `<form id="base-form">`;
    Object.entries(BASE_FIXED_VALUES).forEach(([field, defValue]) => {
        const defType = typeof defValue;
        let value = defValue;
        let placeholder = BASE_DEFAULT_VALUES[field] ?? '';

        if (defType === 'boolean') {
            html += `
                <div class="form-check mb-3">
                    <input class="form-check-input" type="checkbox" id="field-base-${field}" name="base.${field}" ${value ? 'checked' : ''}>
                    <label class="form-check-label" for="field-base-${field}">${field}</label>
                </div>
            `;
        } else if (defType === 'number') {
            html += `
                <div class="mb-3">
                    <label class="form-label" for="field-base-${field}">${field}</label>
                    <input type="number" class="form-control" id="field-base-${field}" name="base.${field}" value="${value}" placeholder="${placeholder}">
                </div>
            `;
        } else {
            html += `
                <div class="mb-3">
                    <label class="form-label" for="field-base-${field}">${field}</label>
                    <input type="text" class="form-control" id="field-base-${field}" name="base.${field}" value="${value}" placeholder="${placeholder}">
                </div>
            `;
        }
    });
    html += `<button type="button" class="btn btn-primary" id="base-save-btn">Сохранить</button></form>`;

    const mainContent = document.getElementById('other-main-content');
    if (mainContent) mainContent.innerHTML = html;

    const saveBtn = document.getElementById('base-save-btn');
    if (saveBtn) {
        saveBtn.onclick = function () {
            const form = document.getElementById('base-form') as HTMLFormElement;
            if (!form) return;
            const formData = new FormData(form);

            const updated: any = {};
            Object.entries(BASE_FIXED_VALUES).forEach(([field, defValue]) => {
                const defType = typeof defValue;
                let val: any = formData.get(`base.${field}`);

                if (defType === 'boolean') {
                    const el = form.querySelector(`[name="base.${field}"]`) as HTMLInputElement | null;
                    val = el ? el.checked : false;
                } else if (defType === 'number') {
                    val = val !== null && val !== undefined && val !== '' ? parseInt(val as string, 10) : defValue;
                } else {
                    val = val ?? '';
                }

                // Удаляем пустые значения и значения, совпадающие с rootDefaultItems
                const defaultVal = rootDefaultItems?.[field];
                const isEmpty = (defType === 'string' && val === '') ||
                    (defType === 'number' && (val === '' || isNaN(val)));
                const isDefault = defaultVal !== undefined && val === defaultVal;

                if (!isEmpty && !isDefault) {
                    updated[field] = val;
                }
            });

            // Обновляем rootCustomtItems без вложенности "base"
            // Удаляем старые base-поля
            Object.keys(BASE_FIXED_VALUES).forEach(field => {
                if (field in rootCustomtItems) {
                    delete rootCustomtItems[field];
                }
            });
            // Добавляем новые значения
            rootCustomtItems = { ...rootCustomtItems, ...updated };

            saveCustomtItems();
        };
    }
}

function renderOnterModalContext(html: string, custom: any, def: any) {
    html += `
        <button type="button" class="btn btn-secondary mb-2 mt-3" id="context-modal-btn">context</button>
        <div class="modal" tabindex="-1" id="context-modal" style="display:none; background:rgba(0,0,0,0.3); position:fixed; top:0; left:0; width:100vw; height:100vh; z-index:1050;">
            <div class="modal-dialog" style="margin:10vh auto; max-width:400px;">
                <div class="modal-content">
                    <div class="modal-header">
                        <h5 class="modal-title">Редактировать context</h5>
                        <button type="button" class="btn-close" id="context-modal-close" aria-label="Закрыть"></button>
                    </div>
                    <div class="modal-body">
                        <form id="context-form">
                            <div class="form-check mb-3">
                                <input class="form-check-input" type="checkbox" id="context-keepopen" name="keepopen" ${custom.keepopen ?? def.keepopen ? 'checked' : ''}>
                                <label class="form-check-label" for="context-keepopen">keepopen</label>
                            </div>
                            <div class="mb-3">
                                <label class="form-label" for="context-keepalive">keepalive</label>
                                <input type="number" class="form-control" id="context-keepalive" name="keepalive" value="${custom.keepalive ?? ''}" placeholder="${def.keepalive ?? ''}">
                            </div>
                            <div class="mb-3">
                                <label class="form-label" for="context-min">min</label>
                                <input type="number" class="form-control" id="context-min" name="min" value="${custom.min ?? ''}" placeholder="${def.min ?? ''}">
                            </div>
                            <div class="mb-3">
                                <label class="form-label" for="context-max">max</label>
                                <input type="number" class="form-control" id="context-max" name="max" value="${custom.max ?? ''}" placeholder="${def.max ?? ''}">
                            </div>
                        </form>
                    </div>
                    <div class="modal-footer">
                        <button type="button" class="btn btn-secondary" id="context-modal-cancel">Отмена</button>
                        <button type="button" class="btn btn-primary" id="context-modal-save">Сохранить</button>
                    </div>
                </div>
            </div>
        </div>
    `;
    return html;
}

function initOnterModalContext(key: string, def: any) {
    var modal = document.getElementById('context-modal' ) as HTMLElement;
    var openBtn = document.getElementById('context-modal-btn');
    var closeBtn = document.getElementById('context-modal-close');
    var cancelBtn = document.getElementById('context-modal-cancel');
    var saveBtn = document.getElementById('context-modal-save');

    var closeModal = function () {
        if (modal) modal.style.display = 'none';
    };
    var openModal = function () {
        if (modal) modal.style.display = 'block';
    };

    if (openBtn) openBtn.onclick = openModal;
    if (closeBtn) closeBtn.onclick = closeModal;
    if (cancelBtn) cancelBtn.onclick = closeModal;

    if (saveBtn) {
        saveBtn.onclick = function () {

            var form = document.getElementById('context-form') as HTMLFormElement;
            if (!form) return;
            var formData = new FormData(form);

            var updatedContext: any = {
                keepopen: (formData.get('keepopen') === 'on'),
                keepalive: parseInt(formData.get('keepalive') as string, 10) || 0,
                min: parseInt(formData.get('min') as string, 10) || 0,
                max: parseInt(formData.get('max') as string, 10) || 0
            };

            // Удаляем переменные с 0 или совпадающие с дефолтными
            Object.keys(updatedContext).forEach(function (key) {
                if (
                    updatedContext[key] === 0 ||
                    (def && def[key] !== undefined && updatedContext[key] === def[key])
                ) {
                    delete updatedContext[key];
                }
            });

            if (key === 'chromium') {
                rootCustomtItems = {
                    ...rootCustomtItems,
                    chromium: {
                        ...(rootCustomtItems.chromium || {}),
                        context: updatedContext
                    }
                };
            }
            else {
                rootCustomtItems = {
                    ...rootCustomtItems,
                    firefox: {
                        ...(rootCustomtItems.firefox || {}),
                        context: updatedContext
                    }
                };
            }

            closeModal();
            saveCustomtItems();
        };
    }
}


function renderOnterModalImage(html: string, custom: any, def: any) {
    html += `
        <button type="button" class="btn btn-secondary mb-2 mt-3" id="image-modal-btn">image</button>
        <div class="modal" tabindex="-1" id="image-modal" style="display:none; background:rgba(0,0,0,0.3); position:fixed; top:0; left:0; width:100vw; height:100vh; z-index:1050;">
            <div class="modal-dialog" style="margin:10vh auto; max-width:400px;">
                <div class="modal-content">
                    <div class="modal-header">
                        <h5 class="modal-title">Редактировать image</h5>
                        <button type="button" class="btn-close" id="image-modal-close" aria-label="Закрыть"></button>
                    </div>
                    <div class="modal-body">
                        <form id="image-form">
                            <div class="form-check mb-3">
                                <input class="form-check-input" type="checkbox" id="image-cache" name="cache" ${custom.cache ?? def.cache ? 'checked' : ''}>
                                <label class="form-check-label" for="image-cache">cache</label>
                            </div>
                            <div class="form-check mb-3">
                                <input class="form-check-input" type="checkbox" id="image-cache_rsize" name="cache_rsize" ${custom.cache_rsize ?? def.cache_rsize ? 'checked' : ''}>
                                <label class="form-check-label" for="image-cache_rsize">cache_rsize</label>
                            </div>
                            <div class="form-check mb-3">
                                <input class="form-check-input" type="checkbox" id="image-useproxy" name="useproxy" ${custom.useproxy ?? def.useproxy ? 'checked' : ''}>
                                <label class="form-check-label" for="image-useproxy">useproxy</label>
                            </div>
                            <div class="form-check mb-3">
                                <input class="form-check-input" type="checkbox" id="image-useproxystream" name="useproxystream" ${custom.useproxystream ?? def.useproxystream ? 'checked' : ''}>
                                <label class="form-check-label" for="image-useproxystream">useproxystream</label>
                            </div>
                            <div class="mb-3">
                                <label class="form-label" for="image-globalnameproxy">globalnameproxy</label>
                                <input type="text" class="form-control" id="image-globalnameproxy" name="globalnameproxy" value="${custom.globalnameproxy ?? ''}" placeholder="${def.globalnameproxy ?? ''}">
                            </div>
                        </form>
                    </div>
                    <div class="modal-footer">
                        <button type="button" class="btn btn-secondary" id="image-modal-cancel">Отмена</button>
                        <button type="button" class="btn btn-primary" id="image-modal-save">Сохранить</button>
                    </div>
                </div>
            </div>
        </div>
    `;
    return html;
}

function initOnterModalImage(key: string, def: any) {
    var modal = document.getElementById('image-modal') as HTMLElement;
    var openBtn = document.getElementById('image-modal-btn');
    var closeBtn = document.getElementById('image-modal-close');
    var cancelBtn = document.getElementById('image-modal-cancel');
    var saveBtn = document.getElementById('image-modal-save');

    var closeModal = function () {
        if (modal) modal.style.display = 'none';
    };
    var openModal = function () {
        if (modal) modal.style.display = 'block';
    };

    if (openBtn) openBtn.onclick = openModal;
    if (closeBtn) closeBtn.onclick = closeModal;
    if (cancelBtn) cancelBtn.onclick = closeModal;

    if (saveBtn) {
        saveBtn.onclick = function () {
            var form = document.getElementById('image-form') as HTMLFormElement;
            if (!form) return;
            var formData = new FormData(form);

            var updatedImage: any = {
                cache: (formData.get('cache') === 'on'),
                cache_rsize: (formData.get('cache_rsize') === 'on'),
                useproxy: (formData.get('useproxy') === 'on'),
                useproxystream: (formData.get('useproxystream') === 'on'),
                globalnameproxy: formData.get('globalnameproxy') ?? ''
            };

            // Удаляем переменные, совпадающие с дефолтными
            Object.keys(updatedImage).forEach(function (field) {
                if (
                    (def && def[field] !== undefined && updatedImage[field] === def[field]) ||
                    (typeof updatedImage[field] === 'string' && updatedImage[field] === '')
                ) {
                    delete updatedImage[field];
                }
            });

            rootCustomtItems = {
                ...rootCustomtItems,
                [key]: {
                    ...(rootCustomtItems[key] || {}),
                    image: updatedImage
                }
            };

            closeModal();
            saveCustomtItems();
        };
    }
}


function renderOnterModalBuffering(html: string, custom: any, def: any) {
    html += `
        <button type="button" class="btn btn-secondary mb-2 mt-3" id="buffering-modal-btn">buffering</button>
        <div class="modal" tabindex="-1" id="buffering-modal" style="display:none; background:rgba(0,0,0,0.3); position:fixed; top:0; left:0; width:100vw; height:100vh; z-index:1050;">
            <div class="modal-dialog" style="margin:10vh auto; max-width:400px;">
                <div class="modal-content">
                    <div class="modal-header">
                        <h5 class="modal-title">Редактировать buffering</h5>
                        <button type="button" class="btn-close" id="buffering-modal-close" aria-label="Закрыть"></button>
                    </div>
                    <div class="modal-body">
                        <form id="buffering-form">
                            <div class="form-check mb-3">
                                <input class="form-check-input" type="checkbox" id="buffering-enable" name="enable" ${custom.enable ?? def.enable ? 'checked' : ''}>
                                <label class="form-check-label" for="buffering-enable">enable</label>
                            </div>
                            <div class="mb-3">
                                <label class="form-label" for="buffering-rent">rent</label>
                                <input type="number" class="form-control" id="buffering-rent" name="rent" value="${custom.rent ?? ''}" placeholder="${def.rent ?? ''}">
                            </div>
                            <div class="mb-3">
                                <label class="form-label" for="buffering-length">length</label>
                                <input type="number" class="form-control" id="buffering-length" name="length" value="${custom.length ?? ''}" placeholder="${def.length ?? ''}">
                            </div>
                            <div class="mb-3">
                                <label class="form-label" for="buffering-millisecondsTimeout">millisecondsTimeout</label>
                                <input type="number" class="form-control" id="buffering-millisecondsTimeout" name="millisecondsTimeout" value="${custom.millisecondsTimeout ?? ''}" placeholder="${def.millisecondsTimeout ?? ''}">
                            </div>
                        </form>
                    </div>
                    <div class="modal-footer">
                        <button type="button" class="btn btn-secondary" id="buffering-modal-cancel">Отмена</button>
                        <button type="button" class="btn btn-primary" id="buffering-modal-save">Сохранить</button>
                    </div>
                </div>
            </div>
        </div>
    `;
    return html;
}

function initOnterModalBuffering(key: string, def: any) {
    var modal = document.getElementById('buffering-modal') as HTMLElement;
    var openBtn = document.getElementById('buffering-modal-btn');
    var closeBtn = document.getElementById('buffering-modal-close');
    var cancelBtn = document.getElementById('buffering-modal-cancel');
    var saveBtn = document.getElementById('buffering-modal-save');

    var closeModal = function () {
        if (modal) modal.style.display = 'none';
    };
    var openModal = function () {
        if (modal) modal.style.display = 'block';
    };

    if (openBtn) openBtn.onclick = openModal;
    if (closeBtn) closeBtn.onclick = closeModal;
    if (cancelBtn) cancelBtn.onclick = closeModal;

    if (saveBtn) {
        saveBtn.onclick = function () {
            var form = document.getElementById('buffering-form') as HTMLFormElement;
            if (!form) return;
            var formData = new FormData(form);

            var updatedBuffering: any = {
                enable: (formData.get('enable') === 'on'),
                rent: parseInt(formData.get('rent') as string, 10) || 0,
                length: parseInt(formData.get('length') as string, 10) || 0,
                millisecondsTimeout: parseInt(formData.get('millisecondsTimeout') as string, 10) || 0
            };

            // Удаляем переменные с 0 или совпадающие с дефолтными
            Object.keys(updatedBuffering).forEach(function (field) {
                if (
                    updatedBuffering[field] === 0 ||
                    (def && def[field] !== undefined && updatedBuffering[field] === def[field])
                ) {
                    delete updatedBuffering[field];
                }
            });

            rootCustomtItems = {
                ...rootCustomtItems,
                [key]: {
                    ...(rootCustomtItems[key] || {}),
                    buffering: updatedBuffering
                }
            };

            closeModal();
            saveCustomtItems();
        };
    }
}


function renderOnterModalDlna(html: string, custom: any, def: any) {
    html += `
        <button type="button" class="btn btn-secondary mb-2 mt-3" id="dlna-modal-btn">cover</button>
        <div class="modal" tabindex="-1" id="dlna-modal" style="display:none; background:rgba(0,0,0,0.3); position:fixed; top:0; left:0; width:100vw; height:100vh; z-index:1050;">
            <div class="modal-dialog" style="margin:10vh auto; max-width:400px;">
                <div class="modal-content">
                    <div class="modal-header">
                        <h5 class="modal-title">Редактировать cover</h5>
                        <button type="button" class="btn-close" id="dlna-modal-close" aria-label="Закрыть"></button>
                    </div>
                    <div class="modal-body">
                        <form id="dlna-form">
                            <div class="form-check mb-3">
                                <input class="form-check-input" type="checkbox" id="dlna-enable" name="enable" ${custom.enable ?? def.enable ? 'checked' : ''}>
                                <label class="form-check-label" for="dlna-enable">enable</label>
                            </div>
                            <div class="form-check mb-3">
                                <input class="form-check-input" type="checkbox" id="dlna-consoleLog" name="consoleLog" ${custom.consoleLog ?? def.consoleLog ? 'checked' : ''}>
                                <label class="form-check-label" for="dlna-consoleLog">consoleLog</label>
                            </div>
                            <div class="form-check mb-3">
                                <input class="form-check-input" type="checkbox" id="dlna-preview" name="preview" ${custom.preview ?? def.preview ? 'checked' : ''}>
                                <label class="form-check-label" for="dlna-preview">preview</label>
                            </div>
                            <div class="mb-3">
                                <label class="form-label" for="dlna-timeout">timeout</label>
                                <input type="number" class="form-control" id="dlna-timeout" name="timeout" value="${custom.timeout ?? ''}" placeholder="${def.timeout ?? ''}">
                            </div>
                            <div class="mb-3">
                                <label class="form-label" for="dlna-skipModificationTime">skipModificationTime</label>
                                <input type="number" class="form-control" id="dlna-skipModificationTime" name="skipModificationTime" value="${custom.skipModificationTime ?? ''}" placeholder="${def.skipModificationTime ?? ''}">
                            </div>
                            <div class="mb-3">
                                <label class="form-label" for="dlna-extension">extension</label>
                                <input type="text" class="form-control" id="dlna-extension" name="extension" value="${escapeHtmlAttr(custom.extension ?? '')}" placeholder="${escapeHtmlAttr(def.extension ?? '')}">
                            </div>
                            <div class="mb-3">
                                <label class="form-label" for="dlna-coverComand">coverComand</label>
                                <input type="text" class="form-control" id="dlna-coverComand" name="coverComand" value="${escapeHtmlAttr(custom.coverComand ?? '')}" placeholder="${escapeHtmlAttr(def.coverComand ?? '')}">
                            </div>
                            <div class="mb-3">
                                <label class="form-label" for="dlna-previewComand">previewComand</label>
                                <input type="text" class="form-control" id="dlna-previewComand" name="previewComand" value="${escapeHtmlAttr(custom.previewComand ?? '')}" placeholder="${escapeHtmlAttr(def.previewComand ?? '')}">
                            </div>
                            <div class="mb-3">
                                <label class="form-label" for="dlna-priorityClass">priorityClass</label>
                                <input type="number" class="form-control" id="dlna-priorityClass" name="priorityClass" value="${escapeHtmlAttr(custom.priorityClass ?? '')}" placeholder="${escapeHtmlAttr(def.priorityClass ?? '')}">
                            </div>
                        </form>
                    </div>
                    <div class="modal-footer">
                        <button type="button" class="btn btn-secondary" id="dlna-modal-cancel">Отмена</button>
                        <button type="button" class="btn btn-primary" id="dlna-modal-save">Сохранить</button>
                    </div>
                </div>
            </div>
        </div>
    `;
    return html;
}

function initOnterModalDlna(key: string, def: any) {
    var modal = document.getElementById('dlna-modal') as HTMLElement;
    var openBtn = document.getElementById('dlna-modal-btn');
    var closeBtn = document.getElementById('dlna-modal-close');
    var cancelBtn = document.getElementById('dlna-modal-cancel');
    var saveBtn = document.getElementById('dlna-modal-save');

    var closeModal = function () {
        if (modal) modal.style.display = 'none';
    };
    var openModal = function () {
        if (modal) modal.style.display = 'block';
    };

    if (openBtn) openBtn.onclick = openModal;
    if (closeBtn) closeBtn.onclick = closeModal;
    if (cancelBtn) cancelBtn.onclick = closeModal;

    if (saveBtn) {
        saveBtn.onclick = function () {
            var form = document.getElementById('dlna-form') as HTMLFormElement;
            if (!form) return;
            var formData = new FormData(form);

            var updatedDlna: any = {
                enable: (formData.get('enable') === 'on'),
                consoleLog: (formData.get('consoleLog') === 'on'),
                preview: (formData.get('preview') === 'on'),
                timeout: parseInt(formData.get('timeout') as string, 10) || 0,
                skipModificationTime: parseInt(formData.get('skipModificationTime') as string, 10) || 0,
                extension: formData.get('extension') ?? '',
                coverComand: formData.get('coverComand') ?? '',
                previewComand: formData.get('previewComand') ?? '',
                priorityClass: parseInt(formData.get('priorityClass') as string, 10) || 0
            };

            // Удаляем переменные с 0 или пустые строки, либо совпадающие с дефолтными
            Object.keys(updatedDlna).forEach(function (field) {
                if (
                    (typeof updatedDlna[field] === 'boolean' && def && def[field] !== undefined && updatedDlna[field] === def[field]) ||
                    (typeof updatedDlna[field] === 'number' && (updatedDlna[field] === 0 || (def && def[field] !== undefined && updatedDlna[field] === def[field]))) ||
                    (typeof updatedDlna[field] === 'string' && (updatedDlna[field] === '' || (def && def[field] !== undefined && updatedDlna[field] === def[field])))
                ) {
                    delete updatedDlna[field];
                }
            });

            rootCustomtItems = {
                ...rootCustomtItems,
                [key]: {
                    ...(rootCustomtItems[key] || {}),
                    cover: updatedDlna
                }
            };

            closeModal();
            saveCustomtItems();
        };
    }
}


function renderOnterModalInitPlugins(html: string, custom: any, def: any) {
    html += `
        <button type="button" class="btn btn-secondary mb-2 mt-3" id="initplugins-modal-btn">initPlugins</button>
        <div class="modal" tabindex="-1" id="initplugins-modal" style="display:none; background:rgba(0,0,0,0.3); position:fixed; top:0; left:0; width:100vw; height:100vh; z-index:1050;">
            <div class="modal-dialog" style="margin:10vh auto; max-width:400px;">
                <div class="modal-content">
                    <div class="modal-header">
                        <h5 class="modal-title">Редактировать initPlugins</h5>
                        <button type="button" class="btn-close" id="initplugins-modal-close" aria-label="Закрыть"></button>
                    </div>
                    <div class="modal-body">
                        <form id="initplugins-form">
                            ${['dlna', 'tracks', 'tmdbProxy', 'online', 'sisi', 'timecode', 'torrserver', 'backup', 'sync'].map(field => `
                                <div class="form-check mb-2">
                                    <input class="form-check-input" type="checkbox" id="initplugins-${field}" name="${field}" ${(custom[field] ?? def[field]) ? 'checked' : ''}>
                                    <label class="form-check-label" for="initplugins-${field}">${field}</label>
                                </div>
                            `).join('')}
                        </form>
                    </div>
                    <div class="modal-footer">
                        <button type="button" class="btn btn-secondary" id="initplugins-modal-cancel">Отмена</button>
                        <button type="button" class="btn btn-primary" id="initplugins-modal-save">Сохранить</button>
                    </div>
                </div>
            </div>
        </div>
    `;
    return html;
}

function initOnterModalInitPlugins(key: string, def: any) {
    var modal = document.getElementById('initplugins-modal') as HTMLElement;
    var openBtn = document.getElementById('initplugins-modal-btn');
    var closeBtn = document.getElementById('initplugins-modal-close');
    var cancelBtn = document.getElementById('initplugins-modal-cancel');
    var saveBtn = document.getElementById('initplugins-modal-save');

    var closeModal = function () {
        if (modal) modal.style.display = 'none';
    };
    var openModal = function () {
        if (modal) modal.style.display = 'block';
    };

    if (openBtn) openBtn.onclick = openModal;
    if (closeBtn) closeBtn.onclick = closeModal;
    if (cancelBtn) cancelBtn.onclick = closeModal;

    if (saveBtn) {
        saveBtn.onclick = function () {
            var form = document.getElementById('initplugins-form') as HTMLFormElement;
            if (!form) return;
            var formData = new FormData(form);

            var fields = ['dlna', 'tracks', 'tmdbProxy', 'online', 'sisi', 'timecode', 'torrserver', 'backup', 'sync'];
            var updatedInitPlugins: any = {};
            fields.forEach(field => {
                const el = form.querySelector(`[name="${field}"]`) as HTMLInputElement | null;
                const checked = el ? el.checked : false;
                if (def && def[field] !== undefined && checked === def[field]) {
                    // Совпадает с дефолтным, не сохраняем
                    return;
                }
                updatedInitPlugins[field] = checked;
            });

            rootCustomtItems = {
                ...rootCustomtItems,
                [key]: {
                    ...(rootCustomtItems[key] || {}),
                    initPlugins: updatedInitPlugins
                }
            };

            closeModal();
            saveCustomtItems();
        };
    }
}


function renderOnterModalBookmarks(html: string, custom: any, def: any) {
    html += `
        <button type="button" class="btn btn-secondary mb-2 mt-3" id="bookmarks-modal-btn">bookmarks</button>
        <div class="modal" tabindex="-1" id="bookmarks-modal" style="display:none; background:rgba(0,0,0,0.3); position:fixed; top:0; left:0; width:100vw; height:100vh; z-index:1050;">
            <div class="modal-dialog" style="margin:10vh auto; max-width:400px;">
                <div class="modal-content">
                    <div class="modal-header">
                        <h5 class="modal-title">Редактировать bookmarks</h5>
                        <button type="button" class="btn-close" id="bookmarks-modal-close" aria-label="Закрыть"></button>
                    </div>
                    <div class="modal-body">
                        <form id="bookmarks-form">
                            <div class="form-check mb-3">
                                <input class="form-check-input" type="checkbox" id="bookmarks-saveimage" name="saveimage" ${custom.saveimage ?? def.saveimage ? 'checked' : ''}>
                                <label class="form-check-label" for="bookmarks-saveimage">saveimage</label>
                            </div>
                            <div class="form-check mb-3">
                                <input class="form-check-input" type="checkbox" id="bookmarks-savepreview" name="savepreview" ${custom.savepreview ?? def.savepreview ? 'checked' : ''}>
                                <label class="form-check-label" for="bookmarks-savepreview">savepreview</label>
                            </div>
                        </form>
                    </div>
                    <div class="modal-footer">
                        <button type="button" class="btn btn-secondary" id="bookmarks-modal-cancel">Отмена</button>
                        <button type="button" class="btn btn-primary" id="bookmarks-modal-save">Сохранить</button>
                    </div>
                </div>
            </div>
        </div>
    `;
    return html;
}

function initOnterModalBookmarks(key: string, def: any) {
    var modal = document.getElementById('bookmarks-modal') as HTMLElement;
    var openBtn = document.getElementById('bookmarks-modal-btn');
    var closeBtn = document.getElementById('bookmarks-modal-close');
    var cancelBtn = document.getElementById('bookmarks-modal-cancel');
    var saveBtn = document.getElementById('bookmarks-modal-save');

    var closeModal = function () {
        if (modal) modal.style.display = 'none';
    };
    var openModal = function () {
        if (modal) modal.style.display = 'block';
    };

    if (openBtn) openBtn.onclick = openModal;
    if (closeBtn) closeBtn.onclick = closeModal;
    if (cancelBtn) cancelBtn.onclick = closeModal;

    if (saveBtn) {
        saveBtn.onclick = function () {
            var form = document.getElementById('bookmarks-form') as HTMLFormElement;
            if (!form) return;
            var formData = new FormData(form);

            var updatedBookmarks: any = {
                saveimage: (formData.get('saveimage') === 'on'),
                savepreview: (formData.get('savepreview') === 'on')
            };

            // Удаляем переменные, совпадающие с дефолтными
            Object.keys(updatedBookmarks).forEach(function (field) {
                if (
                    (def && def[field] !== undefined && updatedBookmarks[field] === def[field])
                ) {
                    delete updatedBookmarks[field];
                }
            });

            rootCustomtItems = {
                ...rootCustomtItems,
                [key]: {
                    ...(rootCustomtItems[key] || {}),
                    bookmarks: updatedBookmarks
                }
            };

            closeModal();
            saveCustomtItems();
        };
    }
}


function renderOtherPage(containerId: string) {
    const container = document.getElementById(containerId);
    if (!container) return;

    const keys = ['base', 'listen', 'WAF', 'tmdb', 'cub', 'LampaWeb', 'dlna', 'online', 'sisi', 'chromium', 'firefox', 'serverproxy', 'weblog', 'openstat', 'posterApi', 'rch', 'storage', 'ffprobe', 'fileCacheInactive', 'vast', 'apn', 'kit', 'sync'];

    loadCustomAndDefault(container, () => {
        // Боковое меню
        const sidebarHtml = `
            <div class="list-group" id="other-sidebar">
                ${keys.map((key, idx) => `
                    <a href="#" class="list-group-item list-group-item-action${idx === 0 ? ' active' : ''}" data-key="${key}">${key}</a>
                `).join('')}
            </div>
        `;

        container.innerHTML = `
            <div class="row">
                <div class="col-12 col-md-2 mb-3 mb-md-0">
                    ${sidebarHtml}
                </div>
                <div class="col-12 col-md-10" id="other-main-content" style="margin-bottom: 2em; background-color: #fdfdfd; padding: 10px; border-radius: 8px; box-shadow: 0 18px 24px rgb(175 175 175 / 30%);"></div>
            </div>
        `;

        function renderKeyForm(key: string) {
            if (key === 'base') {
                renderBaseForm();
                return;
            }
            const def = rootDefaultItems && rootDefaultItems[key] ? rootDefaultItems[key] : {};
            const custom = rootCustomtItems && rootCustomtItems[key] ? rootCustomtItems[key] : {};

            const excludeFields = ['proxy', 'override_conf', 'appReplace', 'cache_hls', 'headersDeny', // в пизду
                'context', 'image', 'buffering', 'cover', 'initPlugins', 'bookmarks'];  // адаптировано
            const arrayEmptyFields = ['Args', 'geo', 'with_search', 'rsize_disable', 'proxyimg_disable', 'ipsDeny', 'ipsAllow', 'countryDeny', 'countryAllow'];

            let html = `<form id="other-form">`;
            Object.keys(def)
                .filter(field => !excludeFields.includes(field))
                .forEach(field => {
                    const placeholder = def[field] ?? '';
                    let value = custom[field] !== undefined ? custom[field] : '';
                    if (value === null || value === undefined) value = '';
                    const defType = typeof def[field];

                    if (arrayEmptyFields.includes(field)) {
                        // Массив, который должен быть [""] если пусто
                        let arrValue: string[] = Array.isArray(value) ? value : [];
                        if (arrValue.length === 0) arrValue = [''];
                        html += `
                            <div class="mb-3">
                                <label class="form-label" for="field-${key}-${field}">${field} (через запятую)</label>
                                <input type="text" class="form-control" id="field-${key}-${field}" name="${key}.${field}" value="${arrValue.join(', ')}" placeholder="${Array.isArray(placeholder) ? placeholder.join(', ') : ''}">
                            </div>
                        `;
                    } else if (defType === 'boolean') {
                        html += `
                            <div class="form-check mb-3">
                                <input class="form-check-input" type="checkbox" id="field-${key}-${field}" name="${key}.${field}" ${(value !== '' ? value : def[field]) ? 'checked' : ''}>
                                <label class="form-check-label" for="field-${key}-${field}">${field}</label>
                            </div>
                        `;
                    } else if (defType === 'number' && Number.isInteger(def[field])) {
                        html += `
                            <div class="mb-3">
                                <label class="form-label" for="field-${key}-${field}">${field}</label>
                                <input type="number" class="form-control" id="field-${key}-${field}" name="${key}.${field}" value="${value}" placeholder="${placeholder}">
                            </div>
                        `;
                    } else {
                        html += `
                            <div class="mb-3">
                                <label class="form-label" for="field-${key}-${field}">${field}</label>
                                <input type="text" class="form-control" id="field-${key}-${field}" name="${key}.${field}" value="${value}" placeholder="${placeholder}">
                            </div>
                        `;
                    }
                });

            html += `<button type="button" class="btn btn-primary" id="other-save-btn">Сохранить</button></form>`;

            if (def)
            {
                if (def['context']) {
                    html = renderOnterModalContext(
                        html,
                        (custom && custom['context']) ? custom['context'] : {},
                        def['context']
                    );
                }

                if (def['image']) {
                    html = renderOnterModalImage(
                        html,
                        (custom && custom['image']) ? custom['image'] : {},
                        def['image']
                    );
                }

                if (def['buffering']) {
                    html = renderOnterModalBuffering(
                        html,
                        (custom && custom['buffering']) ? custom['buffering'] : {},
                        def['buffering']
                    );
                }

                if (def['cover']) {
                    html = renderOnterModalDlna(
                        html,
                        (custom && custom['cover']) ? custom['cover'] : {},
                        def['cover']
                    );
                }

                if (def['initPlugins']) {
                    html = renderOnterModalInitPlugins(
                        html,
                        (custom && custom['initPlugins']) ? custom['initPlugins'] : {},
                        def['initPlugins']
                    );
                }

                if (def['bookmarks']) {
                    html = renderOnterModalBookmarks(
                        html,
                        (custom && custom['bookmarks']) ? custom['bookmarks'] : {},
                        def['bookmarks']
                    );
                }
            }

            const mainContent = document.getElementById('other-main-content');
            if (mainContent) mainContent.innerHTML = html;

            if (def)
            {
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

            const saveBtn = document.getElementById('other-save-btn');
            if (saveBtn) {
                saveBtn.onclick = function () {
                    const form = document.getElementById('other-form') as HTMLFormElement;
                    if (!form) return;
                    const formData = new FormData(form);

                    const updated: any = {};
                    updated[key] = {};
                    Object.keys(def)
                        .filter(field => !excludeFields.includes(field))
                        .forEach(field => {
                            const defType = typeof def[field];
                            let val: any = formData.get(`${key}.${field}`);

                            if (arrayEmptyFields.includes(field)) {
                                const arr = (val as string)
                                    .split(',')
                                    .map(s => s.trim())
                                    .filter(s => s !== '');
                                if (arr.length > 0) {
                                    updated[key][field] = arr;
                                } else {
                                    delete updated[key][field];
                                }
                            } else if (defType === 'boolean') {
                                const el = form.querySelector(`[name="${key}.${field}"]`) as HTMLInputElement | null;
                                val = el ? el.checked : false;
                                if (val !== def[field]) updated[key][field] = val;
                            } else if (defType === 'number' && Number.isInteger(def[field])) {
                                val = val !== null && val !== undefined && val !== '' ? parseInt(val as string, 10) : 0;
                                if (val !== 0 && val !== def[field]) updated[key][field] = val;
                            } else if (val !== null && val !== undefined && val !== '') {
                                updated[key][field] = val;
                            }
                        });

                    // Обновляем rootCustomtItems
                    rootCustomtItems = { ...rootCustomtItems, ...updated };

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
        setTimeout(() => {
            const sidebar = document.getElementById('other-sidebar');
            if (sidebar) {
                sidebar.querySelectorAll('a[data-key]').forEach(link => {
                    link.addEventListener('click', function (e) {
                        e.preventDefault();
                        // Снимаем активный класс со всех
                        sidebar.querySelectorAll('.list-group-item').forEach(item => item.classList.remove('active'));
                        // Добавляем активный класс текущему
                        (e.currentTarget as HTMLElement).classList.add('active');
                        const key = (e.currentTarget as HTMLElement).getAttribute('data-key');
                        if (!key) return;
                        renderKeyForm(key);
                    });
                });
            }
        }, 0);
    });
}