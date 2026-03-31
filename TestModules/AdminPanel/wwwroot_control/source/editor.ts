function renderEditorPage(containerId: string) {
    const container = document.getElementById(containerId);
    if (!container) return;

    // Список пунктов меню
    const menuItems = [
        { key: "custom", label: "custom" },
        { key: "current", label: "current" },
        { key: "default", label: "default" }
    ];

    // Кэш для данных редактора
    const editorDataCache: { [type: string]: any } = {};

    // Формируем HTML для бокового меню в стиле online.ts
    const sidebarHtml = `
        <div class="list-group" id="editor-sidebar">
            ${menuItems.map((item, idx) => `
                <a href="#" class="list-group-item list-group-item-action${idx === 0 ? ' active' : ''}" data-type="${item.key}">${item.label}</a>
            `).join('')}
        </div>
    `;

    // Добавляем кастомный стиль для активного пункта (как в online.ts)
    const styleId = 'editor-sidebar-active-style';
    if (!document.getElementById(styleId)) {
        const style = document.createElement('style');
        style.id = styleId;
        style.innerHTML = `
            #editor-sidebar .list-group-item.active {
                background: linear-gradient(90deg, #0d6efd 60%, #3a8bfd 100%);
                color: #fff;
                font-weight: bold;
                border-color: #0d6efd;
                box-shadow: 0 2px 8px rgba(13,110,253,0.10);
            }
            #editor-sidebar .list-group-item.active:focus, 
            #editor-sidebar .list-group-item.active:hover {
                background: linear-gradient(90deg, #0b5ed7 60%, #2563eb 100%);
                color: #fff;
            }
        `;
        document.head.appendChild(style);
    }

    container.innerHTML = `
        <div class="d-flex justify-content-between align-items-center mb-3">
            <h1 class="mb-0">Редактор</h1>
            <button id="editor-save-btn" class="btn btn-primary">Сохранить</button>
        </div>
        <div class="row">
            <div class="col-12 col-md-2 mb-2 mb-md-0">
                ${sidebarHtml}
            </div>
            <div class="col-12 col-md-10" style="min-height:400px;">
                <div id="editor-codemirror" style="
                    border:1px solid #ced4da;
                    border-radius:0.375rem;
                    width:100%;
                    min-height:400px;
                    height:calc(100vh - 160px);
                    box-sizing:border-box;
                "></div>
            </div>
        </div>
    `;

    let editor: any = null;

    function loadEditorData(type: string) {
        // Если данные уже есть в кэше, используем их

        if (type === 'default')
            editorDataCache[type] = rootDefaultItems;

        if (editorDataCache[type]) {
            const jsonText = JSON.stringify(editorDataCache[type], null, 2);
            if (editor) {
                editor.setValue(jsonText);
            } else {
                // @ts-ignore
                editor = CodeMirror(document.getElementById('editor-codemirror'), {
                    value: jsonText,
                    mode: { name: "javascript", json: true },
                    lineNumbers: true,
                    lineWrapping: true,
                    theme: "default",
                    viewportMargin: Infinity,
                });
                const wrapper = editor.getWrapperElement();
                wrapper.style.height = "100%";
                wrapper.style.minHeight = "400px";
            }
            return;
        }

        // Если нет в кэше — делаем fetch
        fetch(`/admin/init/${type}`)
            .then(response => {
                if (!response.ok) throw new Error('Ошибка загрузки данных');
                return response.json();
            })
            .then(data => {
                editorDataCache[type] = data; // сохраняем в кэш
                const jsonText = JSON.stringify(data, null, 2);
                if (editor) {
                    editor.setValue(jsonText);
                } else {
                    // @ts-ignore
                    editor = CodeMirror(document.getElementById('editor-codemirror'), {
                        value: jsonText,
                        mode: { name: "javascript", json: true },
                        lineNumbers: true,
                        lineWrapping: true,
                        theme: "default",
                        viewportMargin: Infinity,
                    });
                    const wrapper = editor.getWrapperElement();
                    wrapper.style.height = "100%";
                    wrapper.style.minHeight = "400px";
                }
            })
            .catch(error => {
                const errorText = `Ошибка: ${error.message}`;
                if (editor) {
                    editor.setValue(errorText);
                } else {
                    // @ts-ignore
                    editor = CodeMirror(document.getElementById('editor-codemirror'), {
                        value: errorText,
                        mode: { name: "javascript", json: true },
                        lineNumbers: true,
                        lineWrapping: true,
                        theme: "default",
                        viewportMargin: Infinity,
                    });
                    const wrapper = editor.getWrapperElement();
                    wrapper.style.height = "100%";
                    wrapper.style.minHeight = "400px";
                }
            });
    }

    // Инициализация редактора с "custom"
    loadEditorData('custom');

    // Обработчик кликов по боковому меню
    setTimeout(() => {
        const sidebar = document.getElementById('editor-sidebar');
        const saveBtn = document.getElementById('editor-save-btn');
        let currentType = "custom";
        if (sidebar) {
            sidebar.querySelectorAll('a[data-type]').forEach(link => {
                link.addEventListener('click', function (e) {
                    e.preventDefault();
                    // Снимаем активный класс со всех
                    sidebar.querySelectorAll('.list-group-item').forEach(item => item.classList.remove('active'));
                    // Добавляем активный класс текущему
                    (e.currentTarget as HTMLElement).classList.add('active');
                    const type = (e.currentTarget as HTMLElement).getAttribute('data-type');
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
            saveBtn.onclick = () => {
                if (!editor) return;
                let json;
                try {
                    json = JSON.stringify(JSON.parse(editor.getValue()), null, 2);
                } catch (e) {
                    alert(e);
                    return;
                }
                const formData = new FormData();
                formData.append('json', json);

                fetch('/admin/init/save', {
                    method: 'POST',
                    body: formData
                })
                    .then(async response => {
                        if (response.ok) {
                            const data = await response.json();
                            if (data && data.success === true) {
                                // Обновляем кэш для custom
                                try {
                                    editorDataCache["custom"] = JSON.parse(json);
                                } catch {}
                                showToast('Данные успешно сохранены');
                            } else if (data && data.ex) {
                                alert(data.ex);
                            } else {
                                alert('Ошибка: сервер не подтвердил сохранение');
                            }
                        } else {
                            alert('Ошибка при сохранении данных');
                        }
                    })
                    .catch(() => {
                        alert('Ошибка при отправке запроса');
                    });
            };
        }
    }, 0);
}