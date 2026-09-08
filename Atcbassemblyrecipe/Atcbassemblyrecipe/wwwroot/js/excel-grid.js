// Shared behaviour for the spreadsheet-style grids (AWACSWSTYPE, Table Sawing,
// Table Marker, AWACSLF). Every one of those pages used to carry its own copy of
// this script; they now call window.excelGrid.init() with the few ids that
// actually differ.
//
// The markup contract:
//   #addRow                      the hidden "new row" <tr>
//   #showAddRow / #cancelAddRow  buttons that reveal and hide it
//   [data-row-id="KEY"]          a display row
//   [data-edit-panel="KEY"]      the edit row that replaces it
//   [data-edit-row="KEY"]        the button that swaps display -> edit
//   [data-cancel-edit="KEY"]     the button that swaps back
//   [data-delete-form="FORM_ID"] a delete button guarded by the confirm modal
//
// Row forms live *outside* the table on purpose. A <form> written between two
// <tr> elements is not kept there by the HTML parser: it is closed immediately
// and its hidden inputs - row id, confirmation flag, antiforgery token - are
// moved out of it, so the POST arrives without them and the save silently fails.
// The inputs inside the row cells reach their form through form="..." instead.
(() => {
    const clamp = (value, min, max) => Math.min(max, Math.max(min, value));

    const initCsvPicker = (csvInputId) => {
        const csvFile = document.getElementById(csvInputId);
        if (!csvFile) {
            return;
        }

        csvFile.addEventListener('change', () => {
            const label = document.querySelector(`label[for="${csvInputId}"]`);
            const file = csvFile.files?.[0];
            if (!file) {
                return;
            }

            const fileError = document.querySelector('[data-csv-file-error]');
            // Matches CsvImportReader.IsSupportedFileName on the server: Excel
            // writes .txt for tab-delimited saves and .tsv on some locales.
            const allowedExtensions = ['.csv', '.txt', '.tsv'];
            const name = file.name.toLowerCase();
            if (!allowedExtensions.some((extension) => name.endsWith(extension))) {
                csvFile.value = '';
                if (label) {
                    label.textContent = 'Import CSV';
                }
                if (fileError) {
                    fileError.textContent = 'Only .csv, .txt or .tsv files can be uploaded.';
                }
                return;
            }

            if (label) {
                label.textContent = file.name;
            }
            if (fileError) {
                fileError.textContent = '';
            }
        });
    };

    const initAddRow = (focusSelector) => {
        const addRow = document.getElementById('addRow');
        const showAddRow = document.getElementById('showAddRow');
        const cancelAddRow = document.getElementById('cancelAddRow');
        const focusFirst = () => addRow?.querySelector(focusSelector)?.focus();

        showAddRow?.addEventListener('click', () => {
            addRow?.classList.remove('d-none');
            focusFirst();
        });

        cancelAddRow?.addEventListener('click', () => {
            addRow?.classList.add('d-none');
        });

        if (addRow && !addRow.classList.contains('d-none')) {
            setTimeout(focusFirst, 220);
        }
    };

    const initInlineEdit = (focusSelector) => {
        document.querySelectorAll('[data-edit-row]').forEach((button) => {
            button.addEventListener('click', () => {
                const key = button.getAttribute('data-edit-row');
                document.querySelector(`[data-row-id="${CSS.escape(key)}"]`)?.classList.add('d-none');
                const panel = document.querySelector(`[data-edit-panel="${CSS.escape(key)}"]`);
                panel?.classList.remove('d-none');
                panel?.querySelector(focusSelector)?.focus();
            });
        });

        document.querySelectorAll('[data-cancel-edit]').forEach((button) => {
            button.addEventListener('click', () => {
                const key = button.getAttribute('data-cancel-edit');
                const panel = document.querySelector(`[data-edit-panel="${CSS.escape(key)}"]`);
                // Throw away whatever was typed, so re-opening the row shows the
                // values that are actually in the database.
                panel?.querySelectorAll('input[data-original-value]').forEach((input) => {
                    input.value = input.getAttribute('data-original-value') ?? '';
                });
                panel?.classList.add('d-none');
                document.querySelector(`[data-row-id="${CSS.escape(key)}"]`)?.classList.remove('d-none');
            });
        });
    };

    const initTableSizing = (tableId, storageKey) => {
        const table = document.getElementById(tableId);
        if (!table) {
            return;
        }

        const tableFontDown = document.getElementById('tableFontDown');
        const tableFontUp = document.getElementById('tableFontUp');
        const tableFontValue = document.getElementById('tableFontValue');
        const resetColumnWidths = document.getElementById('resetColumnWidths');
        const tableFontKey = `assemblyRecipeTableFontScale:${storageKey}`;
        const columnWidthKey = `assemblyRecipeColumnWidths:${storageKey}`;

        const readFontScale = () => {
            const stored = Number.parseFloat(localStorage.getItem(tableFontKey) || '1');
            return Number.isFinite(stored) ? clamp(stored, 0.82, 1.24) : 1;
        };
        const applyTableFont = (value) => {
            const nextScale = clamp(value, 0.82, 1.24);
            table.style.setProperty('--table-font-scale', nextScale.toString());
            localStorage.setItem(tableFontKey, nextScale.toString());
            if (tableFontValue) {
                tableFontValue.value = `${Math.round(nextScale * 100)}%`;
                tableFontValue.textContent = tableFontValue.value;
            }
        };

        applyTableFont(readFontScale());
        tableFontDown?.addEventListener('click', () => applyTableFont(readFontScale() - 0.04));
        tableFontUp?.addEventListener('click', () => applyTableFont(readFontScale() + 0.04));

        const cols = Array.from(table.querySelectorAll('col'));
        const columnKeyAt = (index) => table.tHead?.rows[0]?.cells[index]?.dataset.colKey;

        const loadColumnWidths = () => {
            try {
                const widths = JSON.parse(localStorage.getItem(columnWidthKey) || '{}');
                cols.forEach((col, index) => {
                    const key = columnKeyAt(index);
                    if (key && Number.isFinite(widths[key])) {
                        col.style.width = `${widths[key]}px`;
                    }
                });
            } catch {
                localStorage.removeItem(columnWidthKey);
            }
        };
        const saveColumnWidths = () => {
            const widths = {};
            cols.forEach((col, index) => {
                const key = columnKeyAt(index);
                const width = Number.parseFloat(col.style.width);
                if (key && Number.isFinite(width)) {
                    widths[key] = width;
                }
            });
            localStorage.setItem(columnWidthKey, JSON.stringify(widths));
        };

        loadColumnWidths();

        table.querySelectorAll('.column-resizer').forEach((handle) => {
            handle.addEventListener('mousedown', (event) => {
                event.preventDefault();
                const header = handle.closest('th');
                const index = Array.from(header.parentElement.children).indexOf(header);
                const col = cols[index];
                const startX = event.clientX;
                const startWidth = col.getBoundingClientRect().width || header.getBoundingClientRect().width;
                document.body.classList.add('is-resizing-column');

                const move = (moveEvent) => {
                    col.style.width = `${clamp(startWidth + moveEvent.clientX - startX, 48, 620)}px`;
                };
                const stop = () => {
                    document.removeEventListener('mousemove', move);
                    document.removeEventListener('mouseup', stop);
                    document.body.classList.remove('is-resizing-column');
                    saveColumnWidths();
                };

                document.addEventListener('mousemove', move);
                document.addEventListener('mouseup', stop);
            });

            handle.addEventListener('dblclick', () => {
                const header = handle.closest('th');
                const index = Array.from(header.parentElement.children).indexOf(header);
                cols[index].style.width = '';
                saveColumnWidths();
            });
        });

        resetColumnWidths?.addEventListener('click', () => {
            cols.forEach((col) => (col.style.width = ''));
            localStorage.removeItem(columnWidthKey);
        });
    };

    const initTemplateModal = (templateModalId) => {
        const element = document.getElementById(templateModalId);
        if (!element || !window.bootstrap) {
            return;
        }

        if (element.parentElement !== document.body) {
            document.body.appendChild(element);
        }

        const modal = new bootstrap.Modal(element);
        document.querySelectorAll('[data-template-modal]').forEach((button) => {
            button.addEventListener('click', () => modal.show());
        });
    };

    const initDeleteModal = () => {
        const element = document.getElementById('deleteConfirmModal');
        if (!element || !window.bootstrap) {
            return;
        }

        if (element.parentElement !== document.body) {
            document.body.appendChild(element);
        }

        const modal = new bootstrap.Modal(element);
        const deletePhrase = document.getElementById('deletePhrase');
        const deleteTargetName = document.getElementById('deleteTargetName');
        const confirmDeleteButton = document.getElementById('confirmDeleteButton');
        let activeDeleteForm = null;

        document.querySelectorAll('[data-delete-form]').forEach((button) => {
            button.addEventListener('click', () => {
                activeDeleteForm = document.getElementById(button.getAttribute('data-delete-form'));
                deleteTargetName.textContent = button.getAttribute('data-delete-label') || 'this row';
                deletePhrase.value = '';
                confirmDeleteButton.disabled = true;
                modal.show();
                setTimeout(() => deletePhrase.focus(), 180);
            });
        });

        deletePhrase.addEventListener('input', () => {
            confirmDeleteButton.disabled = deletePhrase.value.trim().toUpperCase() !== 'DELETE';
        });

        confirmDeleteButton.addEventListener('click', () => {
            if (deletePhrase.value.trim().toUpperCase() === 'DELETE') {
                activeDeleteForm?.submit();
            }
        });
    };

    window.excelGrid = {
        init(options) {
            const settings = {
                focusSelector: 'input.cell-input:not([readonly])',
                templateModalId: null,
                csvInputId: null,
                ...options
            };

            initCsvPicker(settings.csvInputId);
            initAddRow(settings.focusSelector);
            initInlineEdit(settings.focusSelector);
            initTableSizing(settings.tableId, settings.storageKey);
            initTemplateModal(settings.templateModalId);
            initDeleteModal();
        }
    };
})();
