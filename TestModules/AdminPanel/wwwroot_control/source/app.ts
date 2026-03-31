const pages: { [key: string]: string } = { };

interface Window {
    invc?: {
        newUser: (user: any) => void;
        editUser: (user: any) => void;
    };
}

let rootCustomtItems: any = null;
let rootDefaultItems: any = null;

fetch('/admin/init/default')
    .then(res => res.json())
    .then(ob => {
        rootDefaultItems = ob;
    });

function navigate(page: string) {
    const content = document.getElementById('content');
    if (!content) return;

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
    const sendData = new FormData();
    sendData.append('json', JSON.stringify(rootCustomtItems, null, 2));

    fetch('/admin/init/save', {
        method: 'POST',
        body: sendData
    })
        .then(async response => {
            if (response.ok) {
                const data = await response.json();
                if (data && data.success === true) {
                    showToast('Данные успешно сохранены');
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
}


function escapeHtmlAttr(str: any): string {
    if (str === null || str === undefined) return '';
    return String(str)
        .replace(/&/g, '&amp;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
}

function showToast(message: string) {
    // Создаём контейнер для тостов, если его нет
    let toastContainer = document.getElementById('toast-container');
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
    const bgClass = 'bg-success';
    const textClass = 'text-white';

    const toastId = 'toast-' + Date.now() + Math.floor(Math.random() * 1000);

    toastContainer.insertAdjacentHTML('beforeend', `
        <div id="${toastId}" class="toast align-items-center ${bgClass} ${textClass}" role="alert" aria-live="assertive" aria-atomic="true" data-bs-delay="3000" style="min-width:220px;">
            <div class="d-flex">
                <div class="toast-body">${message}</div>
                <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Закрыть"></button>
            </div>
        </div>
    `);

    // @ts-ignore
    const toastEl = document.getElementById(toastId);
    // @ts-ignore
    const toast = new bootstrap.Toast(toastEl);
    toast.show();

    // Удаляем DOM после скрытия
    if (toastEl) {
        toastEl.addEventListener('hidden.bs.toast', () => {
            toastEl.remove();
        });
    }
}


// Инициализация по умолчанию
window.onload = () => navigate('home');