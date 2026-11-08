(function () {
    const form = document.getElementById('bookingNotesForm');
    if (!form) return;

    const input = document.getElementById('bookingNotesInput');
    const display = document.getElementById('bookingNotesDisplay');
    const edit = document.getElementById('editBookingNotes');
    const cancel = document.getElementById('cancelBookingNotes');
    const save = form.querySelector('button[type="submit"]');
    const message = document.getElementById('bookingNotesMessage');
    let savedNotes = input.value;
    let saving = false;

    function closeEditor() {
        input.readOnly = true;
        form.hidden = true;
        edit.hidden = false;
        edit.setAttribute('aria-expanded', 'false');
        edit.focus();
    }

    edit.addEventListener('click', function () {
        input.value = savedNotes;
        input.readOnly = false;
        form.hidden = false;
        edit.hidden = true;
        edit.setAttribute('aria-expanded', 'true');
        message.hidden = true;
        input.focus();
    });

    cancel.addEventListener('click', function () {
        if (saving) return;
        input.value = savedNotes;
        message.hidden = true;
        closeEditor();
    });

    form.addEventListener('submit', async function (event) {
        event.preventDefault();
        if (saving || !form.reportValidity()) return;
        saving = true;
        save.disabled = cancel.disabled = true;
        input.readOnly = true;
        message.hidden = true;

        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: { 'Accept': 'application/json' }
            });
            const data = response.headers.get('content-type')?.includes('application/json')
                ? await response.json() : null;
            if (!response.ok || response.redirected || !data || !Object.hasOwn(data, 'notes')) {
                throw new Error(data?.message || 'Unable to save notes. Please try again or reload the page.');
            }

            savedNotes = data.notes || '';
            input.value = savedNotes;
            display.textContent = savedNotes || 'No notes.';
            closeEditor();
            message.className = 'alert alert-success mt-2 mb-0';
            message.textContent = data.message;
        } catch (error) {
            input.readOnly = false;
            message.className = 'alert alert-danger mt-2 mb-0';
            message.textContent = error.message;
        } finally {
            saving = false;
            save.disabled = cancel.disabled = false;
            message.hidden = false;
        }
    });
})();
