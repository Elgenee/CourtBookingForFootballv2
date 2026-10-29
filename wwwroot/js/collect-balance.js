(function () {
    const form = document.getElementById('collectBalanceForm');
    if (!form) return;
    form.addEventListener('submit', function (event) {
        if (form.dataset.submitting === 'true') {
            event.preventDefault();
            return;
        }
        if (!form.checkValidity()) return;
        form.dataset.submitting = 'true';
        const button = form.querySelector('button[type="submit"]');
        button.disabled = true;
        button.textContent = 'Recording payment…';
    });
    window.addEventListener('pageshow', function () {
        delete form.dataset.submitting;
        const button = form.querySelector('button[type="submit"]');
        button.disabled = false;
        button.textContent = 'Confirm Payment';
    });
})();
