// Upgrades native <datalist> inputs into a styled combobox.
// Native datalist popups are drawn by the browser and cannot be styled with CSS,
// so the list is rebuilt as regular DOM that follows the app theme.
(function () {
    'use strict';

    const MENU_MARGIN = 6;
    let menu = null;
    let current = null;
    let options = [];
    let active = -1;

    function readOptions(listId) {
        const list = document.getElementById(listId);
        if (!list) {
            return [];
        }

        return Array.from(list.querySelectorAll('option')).map(option => ({
            value: option.value,
            label: option.getAttribute('label') || ''
        }));
    }

    function ensureMenu() {
        if (menu) {
            return menu;
        }

        menu = document.createElement('div');
        menu.className = 'combo-menu';
        menu.setAttribute('role', 'listbox');
        menu.hidden = true;
        document.body.appendChild(menu);

        menu.addEventListener('mousedown', event => {
            const item = event.target.closest('.combo-item');
            if (!item) {
                return;
            }

            event.preventDefault();
            choose(Number(item.dataset.index));
        });

        return menu;
    }

    function escapeHtml(value) {
        return value.replace(/[&<>"]/g, char => ({
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;'
        }[char]));
    }

    function highlight(text, query) {
        if (!query) {
            return escapeHtml(text);
        }

        const index = text.toLowerCase().indexOf(query.toLowerCase());
        if (index < 0) {
            return escapeHtml(text);
        }

        return escapeHtml(text.slice(0, index)) +
            '<mark>' + escapeHtml(text.slice(index, index + query.length)) + '</mark>' +
            escapeHtml(text.slice(index + query.length));
    }

    function render(input) {
        const query = input.value.trim();
        const all = readOptions(input.dataset.comboList);
        const currentValue = query.toLowerCase();

        options = all.filter(option =>
            (option.value + ' ' + option.label).toLowerCase().includes(currentValue));

        active = -1;

        if (!options.length) {
            menu.innerHTML = '<p class="combo-empty">No matching leadframe. Any <code>number,number</code> value is still accepted.</p>';
            return;
        }

        menu.innerHTML = '<p class="combo-heading">Known leadframes</p>' + options.map((option, index) => {
            const selected = option.value.toLowerCase() === currentValue;
            return '<div class="combo-item' + (selected ? ' is-selected' : '') + '"' +
                ' role="option" aria-selected="' + selected + '" data-index="' + index + '">' +
                '<span class="combo-text">' +
                '<span class="combo-value">' + highlight(option.value, query) + '</span>' +
                (option.label ? '<span class="combo-label">' + highlight(option.label, query) + '</span>' : '') +
                '</span><span class="combo-check" aria-hidden="true">&#10003;</span></div>';
        }).join('');
    }

    function position() {
        if (!current || menu.hidden) {
            return;
        }

        const rect = current.getBoundingClientRect();
        const below = window.innerHeight - rect.bottom - MENU_MARGIN;
        const above = rect.top - MENU_MARGIN;
        const dropUp = below < 180 && above > below;

        menu.style.minWidth = rect.width + 'px';
        menu.style.maxHeight = Math.max(140, Math.min(280, dropUp ? above : below)) + 'px';

        const left = Math.min(rect.left, window.innerWidth - menu.offsetWidth - 8);
        menu.style.left = Math.max(8, left) + 'px';
        menu.style.top = dropUp
            ? (rect.top - menu.offsetHeight - MENU_MARGIN) + 'px'
            : (rect.bottom + MENU_MARGIN) + 'px';
    }

    function open(input) {
        ensureMenu();
        current = input;
        render(input);
        menu.hidden = false;
        input.setAttribute('aria-expanded', 'true');
        input.closest('td, .combo-field')?.classList.add('combo-open');
        position();
    }

    function close() {
        if (!menu || menu.hidden) {
            return;
        }

        menu.hidden = true;
        active = -1;

        if (current) {
            current.setAttribute('aria-expanded', 'false');
            current.closest('td, .combo-field')?.classList.remove('combo-open');
        }

        current = null;
    }

    function setActive(index) {
        const items = Array.from(menu.querySelectorAll('.combo-item'));
        if (!items.length) {
            return;
        }

        active = (index + items.length) % items.length;
        items.forEach(item => item.classList.remove('is-active'));
        items[active].classList.add('is-active');
        items[active].scrollIntoView({ block: 'nearest' });
    }

    function choose(index) {
        const option = options[index];
        if (!option || !current) {
            return;
        }

        const input = current;
        input.value = option.value;
        close();
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
        input.focus();
    }

    function upgrade(input) {
        const listId = input.getAttribute('list');
        if (!listId || !document.getElementById(listId)) {
            return;
        }

        input.dataset.comboList = listId;
        input.removeAttribute('list');
        input.setAttribute('autocomplete', 'off');
        input.setAttribute('role', 'combobox');
        input.setAttribute('aria-expanded', 'false');
        input.setAttribute('aria-autocomplete', 'list');
        input.classList.add('combo-input');

        input.addEventListener('focus', () => open(input));
        input.addEventListener('click', () => open(input));
        input.addEventListener('input', () => {
            if (current === input) {
                render(input);
                position();
            } else {
                open(input);
            }
        });

        input.addEventListener('keydown', event => {
            if (event.key === 'ArrowDown') {
                event.preventDefault();
                if (menu?.hidden !== false) {
                    open(input);
                }
                setActive(active + 1);
            } else if (event.key === 'ArrowUp') {
                event.preventDefault();
                if (menu?.hidden !== false) {
                    open(input);
                }
                setActive(active - 1);
            } else if (event.key === 'Enter') {
                if (current === input && !menu.hidden && active > -1) {
                    event.preventDefault();
                    choose(active);
                }
            } else if (event.key === 'Escape') {
                if (current === input && !menu.hidden) {
                    event.stopPropagation();
                    close();
                }
            } else if (event.key === 'Tab') {
                close();
            }
        });

        input.addEventListener('blur', () => {
            window.setTimeout(() => {
                if (current === input && !menu.contains(document.activeElement)) {
                    close();
                }
            }, 0);
        });
    }

    document.addEventListener('DOMContentLoaded', () => {
        document.querySelectorAll('input[list]').forEach(upgrade);
    });

    document.addEventListener('mousedown', event => {
        if (current && event.target !== current && !menu.contains(event.target)) {
            close();
        }
    });

    window.addEventListener('resize', close);
    window.addEventListener('scroll', position, true);
})();
