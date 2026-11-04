const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { createRequire } = require('node:module');

// Exercise the actual rendered Razor scripts with jQuery and a controllable clock.
const modules = createRequire(path.join(path.resolve(process.argv[3]), '_checks.cjs'));
const { JSDOM } = modules('jsdom');
const jquery = modules('jquery');
const renderDirectory = path.resolve(process.argv[2]);
let checks = 0;
function check(condition, description) {
    assert.ok(condition, description);
    checks++;
    console.log('PASS ' + description);
}
function page(file, marker) {
    const html = fs.readFileSync(path.join(renderDirectory, file), 'utf8');
    const dom = new JSDOM(html, { runScripts: 'outside-only', url: 'http://localhost' });
    const $ = jquery(dom.window);
    dom.window.jQuery = $;
    const requests = [];
    $.getJSON = (url, data) => {
        const deferred = $.Deferred();
        requests.push({ url, data, deferred });
        return deferred.promise();
    };
    let elapsed = 0;
    let timer;
    Object.defineProperty(dom.window.performance, 'now', { value: () => elapsed });
    dom.window.setInterval = callback => { timer = callback; };
    const script = Array.from(html.matchAll(/<script>([\s\S]*?)<\/script>/g)).find(match => match[1].includes(marker))[1];
    const run = () => dom.window.eval(script);
    return { dom, $, requests, run, advance: (milliseconds, fireTimer = true) => { elapsed += milliseconds; if (fireTimer) timer(); } };
}
const midnight = {
    startTime: '00:00:00', endTime: '01:00:00', startDate: '2026-11-05', endDate: '2026-11-05',
    label: '12:00 AM – 1:00 AM', isAvailable: true, hourlyRate: 1000, totalAmount: 1000,
    isPromoRate: false, isMixedRate: false, advanceBookingDeadlineUtc: '2026-11-04T12:00:00Z'
};
const evening = { ...midnight, startTime: '23:00:00', endTime: '00:00:00', startDate: '2026-11-04',
    label: '11:00 PM – 12:00 AM', advanceBookingDeadlineUtc: null };
const reason = 'Requires booking at least 4 hours in advance.';
const friendly = 'Late-night bookings (12 AM–4 AM) must be made at least 4 hours in advance.';
const booking = page('Create.html', 'expireLateNightSlots');
const $ = booking.$;
$('#BookingDate').val('2026-11-04');
$('input[name="PaymentMethod"]').prop('checked', true);
$('#AcceptedTerms').prop('checked', true);
booking.run();
const choose = start => $('.slot-btn').filter((_, b) => $(b).data('start') === start).trigger('click');
booking.requests[0].deferred.resolve({ closed: false, serverNowUtc: '2026-11-04T12:00:00Z', slots: [midnight, evening] });
check($('#StartTime').val() === '00:00:00' && !$('#submitBookingBtn').prop('disabled'),
    'Query preselection is allowed at exactly four hours using server time');
check($('.slot-btn.active').text().includes('Next day'), 'Selected midnight retains its next-day label');
booking.advance(1);
check($('#submitBookingBtn').prop('disabled') && $('#StartTime').val() === '' && $('#lateNightError').text() === friendly,
    'Open page clears expired selection, disables confirmation, and shows friendly message');
check($('.slot-unavailable').prop('disabled') && $('.slot-unavailable').attr('title') === reason
    && $('.availability-period-count').text() === '1 available', 'Expiry disables slot with explanation and updates available count');
choose('23:00:00');
check(!$('#submitBookingBtn').prop('disabled') && $('#lateNightError').text() === '', 'Selecting another allowed slot clears the error');
booking.advance(3600000);
check(!$('#submitBookingBtn').prop('disabled'), 'Late evening slot outside window stays selectable');
$('#DurationHours').val('2').trigger('change');
check($('#submitBookingBtn').prop('disabled') && $('#StartTime').val() === '', 'Duration change clears the old selection and reloads validation');
booking.requests.at(-1).deferred.resolve({ closed: false, serverNowUtc: '2026-11-04T11:59:59Z', slots: [midnight] });
choose('00:00:00');
check(!$('#submitBookingBtn').prop('disabled'), 'New selection before cutoff enables confirmation');
// Advance without the interval to simulate submitting from a throttled/background tab.
booking.advance(2000, false);
const submit = $.Event('submit');
$('#bookingForm').triggerHandler(submit);
check(submit.isDefaultPrevented() && $('#lateNightError').text() === friendly,
    'Submit handler rechecks deadline even when interval has not run');
$('#BookingDate').val('2026-11-05').trigger('change');
booking.requests.at(-1).deferred.resolve({ closed: false, serverNowUtc: '2026-11-04T13:00:00Z',
    slots: [{ ...midnight, isAvailable: false, reason }] });
check($('.slot-btn').prop('disabled') && $('.slot-btn').text().includes(reason), 'Server-restricted slot renders disabled with a visible explanation');
booking.dom.window.close();

const availability = page('Find.html', 'function render(payload)');
availability.run();
availability.requests[0].deferred.resolve({ count: 1, slots: [{ ...evening, courtId: 1, courtName: 'Test Turf',
    date: '2026-11-04', startLabel: '11:00 PM', endLabel: '12:00 AM', totalAmount: 1000 }] });
check(availability.$('.availability-slot-btn').length === 1 && availability.$('#resultsHeader').text() === '1 available slot found',
    'Availability page renders only server-approved slots and the filtered count');
availability.dom.window.close();
console.log(`All ${checks} rendered-page checks passed.`);
