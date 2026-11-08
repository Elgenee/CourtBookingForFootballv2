const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { createRequire } = require('node:module');
const load = createRequire(path.join(process.env.WALKIN_DOM_MODULES, '_notes-checks.cjs'));
const { JSDOM } = load('jsdom');
const dom = new JSDOM(`<!doctype html>
    <div id="bookingNotesDisplay">Original note</div>
    <button id="editBookingNotes" aria-expanded="false">Edit Notes</button>
    <form id="bookingNotesForm" action="/Staff/Bookings/UpdateNotes" hidden>
        <input name="Id" value="7"><input name="__RequestVerificationToken" value="csrf-token">
        <textarea id="bookingNotesInput" name="Notes" maxlength="5000" readonly>Original note</textarea>
        <button type="submit">Save</button><button type="button" id="cancelBookingNotes">Cancel</button>
    </form><div id="bookingNotesMessage" hidden></div>`, { runScripts: 'outside-only', url: 'http://localhost' });
const window = dom.window;
const byId = id => window.document.getElementById(id);
const form = byId('bookingNotesForm');
const input = byId('bookingNotesInput');
const edit = byId('editBookingNotes');
const cancel = byId('cancelBookingNotes');
const save = form.querySelector('[type="submit"]');
let requests = [];
window.fetch = (url, options) => new Promise((resolve, reject) => requests.push({ url, options, resolve, reject }));
window.eval(fs.readFileSync(path.join(__dirname, '../../wwwroot/js/staff-booking-notes.js'), 'utf8'));
let checks = 0;
function check(condition, label) { assert.ok(condition, label); console.log('PASS ' + label); checks++; }
function submit() { const event = new window.Event('submit', { bubbles: true, cancelable: true }); form.dispatchEvent(event); return event; }
const settle = () => new Promise(resolve => setImmediate(resolve));
function respond(index, status, data) {
    requests[index].resolve({ ok: status === 200, status, redirected: false,
        headers: { get: () => 'application/json' }, json: async () => data });
}
(async () => {
    check(input.readOnly && form.hidden, 'Notes start read-only with editing controls hidden');
    edit.click();
    check(!input.readOnly && !form.hidden && edit.hidden && edit.getAttribute('aria-expanded') === 'true'
        && window.document.activeElement === input, 'Edit opens and focuses the notes editor');
    input.value = 'Unsaved'; cancel.click();
    check(input.value === 'Original note' && input.readOnly && form.hidden && requests.length === 0,
        'Cancel discards the draft without sending a request');

    edit.click(); input.value = '<img src=x onerror=alert(1)>\nSaved';
    check(submit().defaultPrevented, 'Save prevents a full page submission');
    check(requests.length === 1 && save.disabled && cancel.disabled && input.readOnly, 'Saving disables editing and repeated clicks');
    submit(); cancel.click();
    check(requests.length === 1 && !form.hidden, 'Duplicate submit and Cancel cannot interrupt an active save');
    check(requests[0].url.endsWith('/Staff/Bookings/UpdateNotes') && requests[0].options.method === 'POST'
        && requests[0].options.body.get('Id') === '7' && requests[0].options.body.get('__RequestVerificationToken') === 'csrf-token',
        'Save includes the booking ID and antiforgery token');
    respond(0, 200, { notes: input.value, message: 'Booking notes updated successfully.' }); await settle();
    check(form.hidden && !edit.hidden && input.readOnly && byId('bookingNotesDisplay').textContent === input.value
        && !byId('bookingNotesDisplay').querySelector('img'), 'Successful save updates visible notes as safe text');
    check(!byId('bookingNotesMessage').hidden && byId('bookingNotesMessage').classList.contains('alert-success')
        && window.document.activeElement === edit, 'Success message is shown and focus returns to Edit Notes');

    edit.click(); input.value = 'Retry this draft'; submit();
    respond(1, 400, { message: 'Notes must be 5,000 characters or fewer.' }); await settle();
    check(!form.hidden && !input.readOnly && input.value === 'Retry this draft' && !save.disabled
        && byId('bookingNotesMessage').textContent.includes('5,000'), 'Validation failure keeps the draft editable and shows the error');
    submit(); requests[2].reject(new Error('Network unavailable')); await settle();
    check(!form.hidden && !input.readOnly && input.value === 'Retry this draft' && !save.disabled,
        'Network failure preserves the draft and allows retry');
    cancel.click(); edit.click();
    check(input.value.includes('<img'), 'Cancel after failure restores the last successfully saved notes');

    input.value = ''; submit(); respond(3, 200, { notes: null, message: 'Booking notes updated successfully.' }); await settle();
    check(byId('bookingNotesDisplay').textContent === 'No notes.' && !edit.hidden, 'Cleared notes show an empty-state message and remain editable');
    edit.click(); input.value = 'Session expired'; submit();
    requests[4].resolve({ ok: true, redirected: true, headers: { get: () => 'text/html' } }); await settle();
    check(!form.hidden && input.value === 'Session expired' && byId('bookingNotesMessage').classList.contains('alert-danger'),
        'An authentication redirect keeps the draft and reports failure');
    console.log(`All ${checks} notes UI checks passed.`);
    window.close();
})().catch(error => { console.error(error); process.exitCode = 1; window.close(); });
