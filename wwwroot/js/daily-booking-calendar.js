(() => {
    "use strict";
    const calendar = document.querySelector("[data-daily-calendar]");
    if (!calendar) return;
    const body = calendar.querySelector(".daily-calendar-body");
    const feedback = calendar.querySelector(".daily-calendar-feedback");
    const retry = calendar.querySelector("[data-calendar-retry]");
    let request;
    let lastUrl;

    async function load(url) {
        if (request) request.abort();
        const current = new AbortController();
        request = current;
        lastUrl = new URL(url, window.location.href);
        lastUrl.searchParams.delete("partial");
        const pageUrl = new URL(lastUrl);
        const partialUrl = new URL(pageUrl);
        partialUrl.searchParams.set("partial", "true");
        calendar.setAttribute("aria-busy", "true");
        feedback.classList.remove("is-error");
        feedback.textContent = "Loading daily slots…";
        retry.hidden = true;
        try {
            const response = await fetch(partialUrl, {
                signal: current.signal,
                credentials: "same-origin",
                cache: "no-store"
            });
            if (response.redirected) {
                window.location.assign(response.url);
                return;
            }
            if (!response.ok) throw new Error("Calendar request failed");
            const html = await response.text();
            if (current !== request) return;
            body.innerHTML = html;
            window.history.replaceState(null, "", pageUrl);
            feedback.textContent = "Daily slots updated.";
            // Keep keyboard focus useful when the selected date is replaced.
            const selected = body.querySelector('[aria-current="date"]');
            if (selected) selected.focus({ preventScroll: true });
        } catch (error) {
            if (error.name === "AbortError" || current !== request) return;
            feedback.classList.add("is-error");
            feedback.textContent = "Could not load the selected date. The previous view is still shown. Please retry.";
            retry.hidden = false;
        } finally {
            if (current === request) calendar.removeAttribute("aria-busy");
        }
    }

    calendar.addEventListener("click", event => {
        const link = event.target.closest("a[data-calendar-link]");
        if (!link || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        load(link.href);
    });
    function loadForm(form) {
        const url = new URL(form.action, window.location.href);
        url.search = new URLSearchParams(new FormData(form)).toString();
        load(url);
    }
    calendar.addEventListener("submit", event => {
        if (!event.target.matches(".daily-calendar-field-form")) return;
        event.preventDefault();
        loadForm(event.target);
    });
    calendar.addEventListener("change", event => {
        if (event.target.matches('select[name="courtId"]')) loadForm(event.target.form);
    });
    retry.addEventListener("click", () => load(lastUrl));
})();
