const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

// Test request coordination without a browser or additional packages.
const handlers = {};
const retryHandlers = {};
const attributes = new Map();
const pending = [];
const redirects = [];
let focused = 0;
const feedback = { textContent: "", classList: { add() {}, remove() {} } };
const retry = { hidden: true, addEventListener: (name, handler) => retryHandlers[name] = handler };
const body = { innerHTML: "original", querySelector: () => ({ focus() { focused++; } }) };
const calendar = {
    querySelector: selector => ({ ".daily-calendar-body": body, ".daily-calendar-feedback": feedback, "[data-calendar-retry]": retry })[selector],
    setAttribute: (key, value) => attributes.set(key, value),
    removeAttribute: key => attributes.delete(key),
    addEventListener: (name, handler) => handlers[name] = handler
};
const window = {
    location: { href: "http://localhost/Admin/Bookings/DailyCalendar", assign: url => redirects.push(url) },
    history: { replaceState: (_, __, url) => window.location.href = String(url) }
};
vm.runInNewContext(fs.readFileSync(path.join(__dirname, "../../wwwroot/js/daily-booking-calendar.js"), "utf8"), {
    document: { querySelector: () => calendar }, window,
    AbortController, URL, URLSearchParams,
    FormData: class { constructor(form) { this.form = form; } *[Symbol.iterator]() { yield* this.form.fields; } },
    fetch: (url, options) => new Promise((resolve, reject) => pending.push({ url, options, resolve, reject }))
});
function click(url, modifiers = {}) {
    const event = { target: { closest: () => ({ href: url }) }, button: 0, preventDefault() { this.prevented = true; }, ...modifiers };
    handlers.click(event);
    return event;
}
const flush = () => new Promise(resolve => setImmediate(resolve));
const ok = content => ({ ok: true, redirected: false, text: async () => content });

(async () => {
    click("http://localhost/Admin/Bookings/DailyCalendar?date=2030-10-16&courtId=1");
    assert.equal(attributes.get("aria-busy"), "true");
    assert.match(feedback.textContent, /Loading/);
    assert.equal(pending[0].url.searchParams.get("partial"), "true");
    assert.equal(pending[0].options.method ?? "GET", "GET");
    pending[0].resolve(ok("first day")); await flush();
    assert.equal(body.innerHTML, "first day");
    assert.equal(attributes.has("aria-busy"), false);
    assert.equal(new URL(window.location.href).searchParams.has("partial"), false);
    assert.equal(focused, 1);
    console.log("PASS GET partial updates content, loading state, focus, and bookmark URL");

    click("http://localhost/Admin/Bookings/DailyCalendar?date=2030-10-17");
    click("http://localhost/Admin/Bookings/DailyCalendar?date=2030-10-18");
    assert.equal(pending[1].options.signal.aborted, true);
    pending[2].resolve(ok("latest day")); await flush();
    pending[1].resolve(ok("stale day")); await flush();
    assert.equal(body.innerHTML, "latest day");
    console.log("PASS Fast date changes abort prior requests and ignore stale responses");

    click("http://localhost/Admin/Bookings/DailyCalendar?date=2030-10-19");
    pending[3].reject(new Error("offline")); await flush();
    assert.equal(body.innerHTML, "latest day");
    assert.equal(retry.hidden, false);
    assert.match(feedback.textContent, /previous view/);
    retryHandlers.click();
    assert.equal(pending[4].url.searchParams.get("date"), "2030-10-19");
    pending[4].resolve(ok("retried day")); await flush();
    assert.equal(body.innerHTML, "retried day");
    assert.equal(retry.hidden, true);
    console.log("PASS Failed loads preserve the current view and support retry");

    const count = pending.length;
    assert.equal(click("http://localhost/example", { ctrlKey: true }).prevented, undefined);
    assert.equal(pending.length, count);
    console.log("PASS Modified link clicks retain native browser behavior");

    const form = { action: "http://localhost/Staff/Bookings/DailyCalendar", fields: [["date", "2030-10-20"], ["courtId", "3"]] };
    handlers.change({ target: { matches: () => true, form } });
    assert.equal(pending[5].url.searchParams.get("courtId"), "3");
    assert.equal(pending[5].url.searchParams.get("date"), "2030-10-20");
    pending[5].resolve(ok("new field")); await flush();
    assert.equal(body.innerHTML, "new field");
    console.log("PASS Field changes preserve the selected date");

    click("http://localhost/Staff/Bookings/DailyCalendar?date=2030-10-21");
    pending[6].resolve({ redirected: true, url: "http://localhost/Account/Login" }); await flush();
    assert.equal(redirects[0], "http://localhost/Account/Login");
    assert.equal(body.innerHTML, "new field");
    console.log("PASS Expired authentication navigates to sign-in without embedding the login page");
})().catch(error => { console.error(error); process.exitCode = 1; });
