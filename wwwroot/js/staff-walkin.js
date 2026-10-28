(function ($) {
    "use strict";

    // Also exported for dependency-free checks of the selection rules.
    class WalkInSelection {
        constructor(slots) { this.slots = slots || []; }
        conflict(candidate) {
            const start = candidate.startDate + 'T' + candidate.startTime;
            const end = candidate.endDate + 'T' + candidate.endTime;
            for (const slot of this.slots) {
                const sameCourt = slot.courtId === candidate.courtId;
                const shared = slot.groupId != null && slot.groupId === candidate.groupId
                    && (slot.isFullCourt || candidate.isFullCourt);
                if (!(sameCourt || shared) || !slot.startDate || !slot.endDate) continue;
                if (start < slot.endDate + 'T' + slot.endTime && end > slot.startDate + 'T' + slot.startTime)
                    return sameCourt && start === slot.startDate + 'T' + slot.startTime && slot.durationHours === candidate.durationHours
                        ? 'This slot has already been added.'
                        : 'This slot overlaps a selected booking for the same field or shared field.';
            }
            return null;
        }
        add(slot) {
            const error = this.slots.length >= 100 ? 'Choose at most 100 bookings at a time.' : this.conflict(slot);
            if (!error) this.slots.push(slot);
            return error;
        }
        remove(index) { this.slots.splice(index, 1); }
        totals() {
            return {
                count: this.slots.length,
                hours: this.slots.reduce((total, slot) => total + slot.durationHours, 0),
                amount: this.slots.reduce((total, slot) => total + Math.round(slot.totalAmount * 100), 0) / 100
            };
        }
    }
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = WalkInSelection;
        return;
    }

    const $form = $('#walkInForm');
    const $court = $('#CourtId');
    const $date = $('#BookingDate');
    const $duration = $('#DurationHours');
    const $slots = $('#slotsContainer');
    const $add = $('#addToBookingBtn');
    const $submit = $('#submitWalkInBtn');
    const selection = new WalkInSelection(JSON.parse($('#walkInSelectionData').text()));
    let selectedSlot = null;
    let lastPayload = null;
    let request = null;
    let requestVersion = 0;
    let submitting = false;
    const periods = [
        { key: 'morning', label: 'Morning', icon: 'fa-sun' },
        { key: 'afternoon', label: 'Afternoon', icon: 'fa-cloud-sun' },
        { key: 'evening', label: 'Evening', icon: 'fa-cloud-moon' },
        { key: 'late-night', label: 'Late Night', icon: 'fa-moon' }
    ];
    const money = value => '₱' + Number(value).toLocaleString('en-PH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const formatDate = value => value ? new Date(value + 'T00:00:00')
        .toLocaleDateString('en-PH', { month: 'long', day: 'numeric', year: 'numeric' }) : 'Invalid date';

    function periodKey(slot) {
        const hour = parseInt(slot.startTime.split(':')[0], 10);
        if (hour >= 6 && hour < 12) return 'morning';
        if (hour >= 12 && hour < 18) return 'afternoon';
        if (hour >= 18 && hour < 22) return 'evening';
        return 'late-night';
    }
    function dayNote(slot) {
        if (slot.startDate > slot.operatingDate) return 'Next day · ' + formatDate(slot.startDate);
        if (slot.endDate > slot.operatingDate) return 'Ends next day · ' + formatDate(slot.endDate);
        return '';
    }
    function candidate(slot) {
        const $option = $court.find('option:selected');
        return Object.assign({}, slot, {
            courtId: Number($court.val()), operatingDate: $date.val(),
            durationHours: Number($duration.val()), courtName: $option.attr('data-name'),
            sport: $option.attr('data-sport'), groupId: $option.attr('data-group') ? Number($option.attr('data-group')) : null,
            isFullCourt: $option.attr('data-full') === 'true',
            rateType: slot.isMixedRate ? 'Mixed rates' : slot.isPromoRate ? 'Promo' : 'Regular'
        });
    }
    function message(text, error) {
        $('#selectionMessage').toggleClass('text-danger', !!error).toggleClass('text-success', !error).text(text);
    }
    function clearCandidate() {
        selectedSlot = null;
        // The legacy scalar TimeSpan still binds on POST; the batch uses Slots.
        $('#StartTime').val('00:00:00');
        $add.prop('disabled', true);
    }
    function updateSummary() {
        const $list = $('#selectedBookings').empty();
        const $inputs = $('#selectedSlotInputs').empty();
        const groups = new Map();
        selection.slots.forEach(function (slot, index) {
            if (!groups.has(slot.operatingDate)) groups.set(slot.operatingDate, []);
            groups.get(slot.operatingDate).push({ slot, index });
            const fields = { CourtId: slot.courtId, BookingDate: slot.operatingDate, StartTime: slot.startTime, DurationHours: slot.durationHours };
            Object.keys(fields).forEach(key => $('<input>', {
                type: 'hidden', name: 'Slots[' + index + '].' + key, value: fields[key]
            }).appendTo($inputs));
        });
        Array.from(groups.keys()).sort().forEach(function (date) {
            $('<h3>', { class: 'h6 mt-3', text: formatDate(date) }).appendTo($list);
            groups.get(date).sort((a, b) => ((a.slot.startDate || '') + (a.slot.startTime || ''))
                .localeCompare((b.slot.startDate || '') + (b.slot.startTime || '')))
                .forEach(function (row) {
                    const slot = row.slot;
                    const $row = $('<div>', { class: 'border rounded p-2 mb-2' });
                    const $heading = $('<div>', { class: 'd-flex justify-content-between align-items-start' });
                    $('<strong>', { class: 'small mr-2', text: slot.label }).appendTo($heading);
                    $('<button>', { type: 'button', class: 'btn btn-outline-danger btn-sm', text: 'Remove',
                        'aria-label': 'Remove ' + slot.courtName + ', ' + formatDate(date) + ', ' + slot.label,
                        'data-remove-index': row.index, 'data-testid': 'walkin-remove-slot' }).appendTo($heading);
                    $row.append($heading);
                    $('<div>', { class: 'small', text: slot.courtName + ' · ' + slot.sport }).appendTo($row);
                    $('<div>', { class: 'small', text: slot.durationHours + ' hour(s) · ' + money(slot.totalAmount) + ' · ' + slot.rateType }).appendTo($row);
                    const note = dayNote(slot);
                    if (note) $('<div>', { class: 'small text-muted', text: note }).appendTo($row);
                    if (slot.error) $('<div>', { class: 'small text-danger', text: slot.error }).appendTo($row);
                    $row.appendTo($list);
                });
        });
        if (!selection.slots.length) $list.append($('<p>', { class: 'text-muted small', text: 'Select a slot and click Add to Booking. You can add more dates and times before submitting.' }));
        const totals = selection.totals();
        $('#summaryCount').text(totals.count);
        $('#summaryDuration').text(totals.hours + (totals.hours === 1 ? ' hour' : ' hours'));
        $('#summaryTotal').text(money(totals.amount));
        $submit.prop('disabled', submitting || !totals.count);
    }
    function renderSlots(payload) {
        $slots.empty();
        if (payload.closed) {
            $slots.append('<div class="alert alert-warning mb-0 py-2">We are closed on this day.</div>');
            return;
        }
        if (!payload.slots || !payload.slots.length) {
            $slots.append('<div class="text-muted small">No slots in this window.</div>');
            return;
        }
        periods.forEach(function (period) {
            const slots = payload.slots.filter(slot => periodKey(slot) === period.key);
            if (!slots.length) return;
            const $section = $('<section>', { class: 'availability-period availability-period-' + period.key, 'aria-label': period.label });
            const $heading = $('<div>', { class: 'availability-period-heading' });
            $('<h3>', { class: 'availability-period-title' }).append($('<i>', { class: 'fas ' + period.icon, 'aria-hidden': 'true' }))
                .append(document.createTextNode(period.label)).appendTo($heading);
            $('<span>', { class: 'availability-period-count', text: slots.filter(slot => slot.isAvailable && !selection.conflict(candidate(slot))).length + ' available' }).appendTo($heading);
            const $grid = $('<div>', { class: 'slot-grid' });
            slots.forEach(function (slot) {
                const item = candidate(slot);
                const conflict = selection.conflict(item);
                const added = selection.slots.some(s => s.courtId === item.courtId && s.operatingDate === item.operatingDate
                    && s.startTime === item.startTime && s.durationHours === item.durationHours);
                const disabled = !slot.isAvailable || !!conflict;
                const $button = $('<button>', { type: 'button', disabled,
                    class: 'slot-btn' + (added ? ' active slot-added' : disabled ? ' slot-unavailable' : '')
                        + (slot.isMixedRate ? ' slot-mixed' : slot.isPromoRate ? ' slot-promo' : ''),
                    title: conflict || slot.reason || '', 'aria-pressed': added ? 'true' : 'false'
                }).data('slot', item);
                $('<span>', { class: 'slot-time', text: slot.label }).appendTo($button);
                if (added) $('<span>', { class: 'slot-rate-badge', text: 'Added' }).appendTo($button);
                else if (slot.isMixedRate || slot.isPromoRate) $('<span>', { class: 'slot-rate-badge', text: item.rateType }).appendTo($button);
                const note = dayNote(item);
                if (note) $('<span>', { class: 'availability-day-note', text: note }).appendTo($button);
                $button.appendTo($grid);
            });
            $section.append($heading, $grid).appendTo($slots);
        });
    }
    function loadSlots() {
        const version = ++requestVersion;
        if (request) request.abort();
        clearCandidate();
        lastPayload = null;
        message('', false);
        if (!$court.val() || !$date.val() || !$duration.val()) {
            $slots.text('Pick a field, date, and duration to see available slots.');
            return;
        }
        $slots.html('<div class="text-muted small"><i class="fas fa-spinner fa-spin mr-1"></i>Loading slots…</div>');
        request = $.getJSON($form.attr('data-slots-url'), { courtId: $court.val(), date: $date.val(), durationHours: $duration.val() })
            .done(function (payload) {
                if (version !== requestVersion) return;
                lastPayload = payload;
                renderSlots(payload);
            }).fail(function (_, status) {
                if (version === requestVersion && status !== 'abort')
                    $slots.html('<div class="alert alert-danger mb-0 py-2">Failed to load slots. Change the date or field to try again.</div>');
            });
    }
    $slots.on('click', '.slot-btn:not(:disabled)', function () {
        $slots.find('.slot-btn:not(.slot-added)').removeClass('active').attr('aria-pressed', 'false');
        $(this).addClass('active').attr('aria-pressed', 'true');
        selectedSlot = $(this).data('slot');
        $('#StartTime').val(selectedSlot.startTime);
        $add.prop('disabled', false);
        message(selectedSlot.label + ' · ' + money(selectedSlot.totalAmount) + ' — click Add to Booking.', false);
    });
    $add.on('click', function () {
        if (!selectedSlot || submitting) return;
        const error = selection.add(selectedSlot);
        if (error) { message(error, true); return; }
        clearCandidate();
        updateSummary();
        if (lastPayload) renderSlots(lastPayload);
        message('Slot added. Choose another time or date to add more bookings.', false);
    });
    $('#selectedBookings').on('click', '[data-remove-index]', function () {
        if (submitting) return;
        selection.remove(Number($(this).attr('data-remove-index')));
        clearCandidate();
        updateSummary();
        if (lastPayload) renderSlots(lastPayload);
        message('Slot removed.', false);
    });
    $court.add($date).add($duration).on('change', loadSlots);
    $form.on('submit', function (event) {
        if (submitting || !selection.slots.length || !$form.valid()) { event.preventDefault(); return; }
        if (selectedSlot) {
            message('Click Add to Booking to include the selected slot before submitting.', true);
            event.preventDefault();
            return;
        }
        const totals = selection.totals();
        if (totals.count > 1 && !window.confirm('Create ' + totals.count + ' walk-in bookings (' + totals.hours + ' hours) for ' + money(totals.amount) + '?')) {
            event.preventDefault();
            return;
        }
        submitting = true;
        $submit.prop('disabled', true).text('Creating bookings…');
        $add.prop('disabled', true);
        $form.find('button[type="button"]').prop('disabled', true);
    });
    // Browsers may restore the disabled button from the back/forward cache.
    $(window).on('pageshow', function () {
        submitting = false;
        $submit.html('<i class="fas fa-check mr-2"></i>Create Walk-In Bookings');
        updateSummary();
        if (lastPayload) renderSlots(lastPayload);
    });
    updateSummary();
    loadSlots();
})(typeof jQuery === 'undefined' ? null : jQuery);
