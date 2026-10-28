const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const Selection = require('../../wwwroot/js/staff-walkin.js');
let checks = 0;
function check(condition, description) {
    assert.ok(condition, description);
    console.log('PASS ' + description);
    checks++;
}
function slot(overrides = {}) {
    return Object.assign({ courtId: 1, groupId: 1, isFullCourt: false, operatingDate: '2026-11-10',
        startDate: '2026-11-10', endDate: '2026-11-10', startTime: '10:00:00', endTime: '11:00:00',
        durationHours: 1, totalAmount: 800, label: '10:00 AM – 11:00 AM', courtName: 'Split A', sport: 'Football', rateType: 'Promo' }, overrides);
}
const selection = new Selection();
check(!selection.add(slot()), 'First selection is accepted');
check(selection.add(slot()).includes('already'), 'Duplicate selection is rejected');
check(selection.add(slot({ startTime: '09:00:00', endTime: '11:00:00', durationHours: 2 })).includes('overlaps'), 'Different-duration overlap is rejected');
check(!selection.add(slot({ startTime: '11:00:00', endTime: '12:00:00' })), 'Touching endpoints are allowed');
check(!selection.add(slot({ courtId: 3 })), 'Split siblings are independent');
check(selection.add(slot({ courtId: 2, isFullCourt: true })).includes('shared'), 'Full field conflicts with split selections');
check(!selection.add(slot({ courtId: 4, groupId: null })), 'Independent field is unaffected');
check(!selection.add(slot({ operatingDate: '2026-11-12', startDate: '2026-11-12', endDate: '2026-11-12', durationHours: 2, totalAmount: 1600 })), 'Selections persist across different dates');
assert.deepEqual(selection.totals(), { count: 5, hours: 6, amount: 4800 });
check(true, 'Totals use every selected slot and duration');
selection.remove(0);
check(selection.totals().count === 4 && selection.totals().amount === 4000, 'Removal recalculates totals');
const overnight = new Selection([slot({ startTime: '23:00:00', endDate: '2026-11-11', endTime: '01:00:00', durationHours: 2 })]);
check(overnight.add(slot({ startDate: '2026-11-11', endDate: '2026-11-11', startTime: '00:00:00', endTime: '01:00:00' })).includes('overlaps'), 'Cross-midnight overlap is detected using actual dates');
check(!overnight.add(slot({ startDate: '2026-11-11', endDate: '2026-11-11', startTime: '01:00:00', endTime: '02:00:00' })), 'Adjacent after-midnight slot is allowed');
const fullFirst = new Selection([slot({ courtId: 2, isFullCourt: true })]);
check(fullFirst.add(slot()).includes('shared'), 'Full/split conflict works in both selection orders');
const capped = new Selection(Array.from({ length: 100 }, () => slot()));
check(capped.add(slot()).includes('100'), 'Batch limit is enforced');
check(new Selection([slot({ totalAmount: 0.10 }), slot({ totalAmount: 0.20 })]).totals().amount === 0.30, 'Currency totals avoid floating-point artifacts');

if (process.env.WALKIN_DOM_MODULES) {
    const { createRequire } = require('node:module');
    const tempRequire = createRequire(path.join(process.env.WALKIN_DOM_MODULES, '_checks.cjs'));
    const { JSDOM } = tempRequire('jsdom');
    const jquery = tempRequire('jquery');
    const dom = new JSDOM(`<!doctype html><form id="walkInForm" data-slots-url="/Bookings/GetSlots">
        <select id="CourtId"><option value="1" data-name="Split A" data-sport="Football" data-group="1" data-full="false">Split A</option>
        <option value="2" data-name="Full" data-sport="Football" data-group="1" data-full="true">Full</option></select>
        <input id="BookingDate" value="2026-11-10"><select id="DurationHours"><option value="1">1</option><option value="2">2</option></select>
        <input id="StartTime" name="StartTime"><div id="slotsContainer"></div><button type="button" id="addToBookingBtn" disabled>Add</button>
        <div id="selectionMessage"></div><div id="selectedSlotInputs"></div><div id="selectedBookings"></div>
        <span id="summaryCount"></span><span id="summaryDuration"></span><span id="summaryTotal"></span>
        <button type="submit" id="submitWalkInBtn" disabled>Create</button></form>
        <script type="application/json" id="walkInSelectionData">[]</script>`, { runScripts: 'outside-only', url: 'http://localhost' });
    const recoveryMarkup = dom.serialize();
    const $ = jquery(dom.window);
    dom.window.jQuery = $;
    let valid = true;
    $.fn.valid = () => valid;
    const requests = [];
    $.getJSON = function (url, data) {
        const deferred = $.Deferred();
        const request = deferred.promise();
        request.abort = () => deferred.reject(null, 'abort');
        requests.push({ url, data, deferred });
        return request;
    };
    const available = [
        ['08:00:00', '09:00:00', '8:00 AM – 9:00 AM'], ['10:00:00', '11:00:00', '10:00 AM – 11:00 AM'],
        ['14:00:00', '15:00:00', '2:00 PM – 3:00 PM'], ['18:00:00', '19:00:00', '6:00 PM – 7:00 PM'],
        ['23:00:00', '00:00:00', '11:00 PM – 12:00 AM']
    ].map(([startTime, endTime, label]) => ({ startTime, endTime, label, startDate: '2026-11-10',
        endDate: endTime === '00:00:00' ? '2026-11-11' : '2026-11-10', hourlyRate: 800, totalAmount: 800,
        isAvailable: true, isPromoRate: true, isMixedRate: false }));
    dom.window.eval(fs.readFileSync(path.resolve(__dirname, '../../wwwroot/js/staff-walkin.js'), 'utf8'));
    check($('#submitWalkInBtn').prop('disabled') && requests.length === 1, 'Empty UI disables submit and requests existing slot endpoint');
    requests[0].deferred.resolve({ closed: false, slots: available });
    check($('.availability-period').length === 4, 'Morning, Afternoon, Evening, and Late Night sections remain');
    function choose(start) { $('.slot-btn').filter((_, button) => $(button).data('slot').startTime === start).trigger('click'); }
    choose('10:00:00');
    check(!$('#addToBookingBtn').prop('disabled') && $('#summaryCount').text() === '0', 'Slot click enables Add without silently adding a booking');
    $('#addToBookingBtn').trigger('click');
    check($('#summaryCount').text() === '1' && $('#selectedBookings').text().includes('November 10, 2026'), 'Add updates grouped summary immediately');
    check($('[name="Slots[0].StartTime"]').val() === '10:00:00' && $('#StartTime').val() === '00:00:00', 'Posted collection and legacy TimeSpan bind correctly');
    check($('.slot-added').length === 1 && $('.slot-added').prop('disabled'), 'Added slot stays highlighted and cannot be selected twice');
    $('#addToBookingBtn').trigger('click');
    check($('#summaryCount').text() === '1', 'Repeated Add click does not duplicate the selection');
    $('#BookingDate').val('2026-11-12').trigger('change');
    check($('#summaryCount').text() === '1', 'Changing date preserves selected bookings');
    const nextDay = available.map(s => Object.assign({}, s, { startDate: '2026-11-12', endDate: s.endTime === '00:00:00' ? '2026-11-13' : '2026-11-12' }));
    requests[1].deferred.resolve({ closed: false, slots: nextDay });
    choose('14:00:00'); $('#addToBookingBtn').trigger('click');
    check($('#summaryCount').text() === '2' && $('#selectedBookings h3').length === 2 && $('#summaryTotal').text().includes('1,600.00'), 'Second date gets a separate group and updates total');
    $('[data-remove-index="0"]').trigger('click');
    check($('#summaryCount').text() === '1' && $('[name="Slots[0].BookingDate"]').val() === '2026-11-12' && !$('[name="Slots[1].BookingDate"]').length,
        'Remove renumbers collection inputs for MVC binding');
    choose('18:00:00'); $('#addToBookingBtn').trigger('click');
    let confirmations = 0;
    dom.window.confirm = () => { confirmations++; return false; };
    let submit = $.Event('submit'); $('#walkInForm').triggerHandler(submit);
    check(submit.isDefaultPrevented() && confirmations === 1 && !$('#submitWalkInBtn').prop('disabled'), 'Cancelling batch confirmation leaves the form editable');
    valid = false;
    submit = $.Event('submit'); $('#walkInForm').triggerHandler(submit);
    check(submit.isDefaultPrevented() && confirmations === 1, 'Invalid customer form does not confirm or submit');
    valid = true;
    dom.window.confirm = () => { confirmations++; return true; };
    submit = $.Event('submit'); $('#walkInForm').triggerHandler(submit);
    check(!submit.isDefaultPrevented() && $('#submitWalkInBtn').prop('disabled'), 'Confirmed batch disables submit while saving');
    submit = $.Event('submit'); $('#walkInForm').triggerHandler(submit);
    check(submit.isDefaultPrevented() && confirmations === 2, 'Repeated submit event is blocked');
    $(dom.window).trigger('pageshow');
    check(!$('#submitWalkInBtn').prop('disabled') && $('[data-remove-index]').filter(':disabled').length === 0, 'Back navigation restores editable controls');
    $('#BookingDate').val('2026-11-13').trigger('change');
    const stale = requests[2];
    $('#BookingDate').val('2026-11-14').trigger('change');
    requests[3].deferred.resolve({ closed: true, slots: [] });
    stale.deferred.resolve({ closed: false, slots: available });
    check($('#slotsContainer').text().includes('closed') && !$('.slot-btn').length, 'Stale availability response cannot replace the current date');
    dom.window.close();
    const recoveryDom = new JSDOM(recoveryMarkup, { runScripts: 'outside-only', url: 'http://localhost' });
    const recovery = jquery(recoveryDom.window);
    recoveryDom.window.jQuery = recovery;
    recovery.fn.valid = () => true;
    recovery.getJSON = () => Object.assign(recovery.Deferred().promise(), { abort() {} });
    recovery('#walkInSelectionData').text(JSON.stringify([
        slot({ startTime: null, startDate: null, endDate: null, endTime: null, error: 'Start time is required.' }),
        slot({ startTime: null, startDate: null, endDate: null, endTime: null, error: 'Start time is required.' })
    ]));
    recoveryDom.window.eval(fs.readFileSync(path.resolve(__dirname, '../../wwwroot/js/staff-walkin.js'), 'utf8'));
    check(recovery('[data-remove-index]').length === 2 && recovery('#selectedBookings').text().includes('Start time is required'),
        'Invalid restored slots remain visible and removable after server validation');
    recoveryDom.window.close();
}
console.log(`All ${checks} JavaScript checks passed.`);
