// Type-the-word confirmation for one-click actions that are hard to take back
// (restore, revert, purge). Buttons opt in with data-confirm-form, and the modal
// submits that form once the word has been typed.
(() => {
    const element = document.getElementById('confirmActionModal');
    if (!element || !window.bootstrap) {
        return;
    }

    if (element.parentElement !== document.body) {
        document.body.appendChild(element);
    }

    const modal = new bootstrap.Modal(element);
    const message = document.getElementById('confirmActionMessage');
    const phraseLabel = document.getElementById('confirmActionPhraseLabel');
    const phrase = document.getElementById('confirmActionPhrase');
    const confirmButton = document.getElementById('confirmActionButton');

    let activeForm = null;
    let requiredPhrase = 'CONFIRM';

    const syncButton = () => {
        confirmButton.disabled = phrase.value.trim().toUpperCase() !== requiredPhrase;
    };

    document.querySelectorAll('[data-confirm-form]').forEach((button) => {
        button.addEventListener('click', () => {
            activeForm = document.getElementById(button.getAttribute('data-confirm-form'));
            requiredPhrase = (button.getAttribute('data-confirm-phrase') || 'CONFIRM').toUpperCase();
            message.textContent = button.getAttribute('data-confirm-label') || 'Are you sure?';
            phraseLabel.textContent = requiredPhrase;
            phrase.value = '';
            syncButton();
            modal.show();
            setTimeout(() => phrase.focus(), 180);
        });
    });

    phrase.addEventListener('input', syncButton);

    confirmButton.addEventListener('click', () => {
        if (phrase.value.trim().toUpperCase() === requiredPhrase) {
            activeForm?.submit();
        }
    });
})();
