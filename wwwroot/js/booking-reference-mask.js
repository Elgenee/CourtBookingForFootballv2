(() => {
    "use strict";
    const input = document.querySelector("[data-booking-reference-mask]");
    if (!input) return;
    const prefix = "GFC-";

    function payload(value) {
        const characters = value.toUpperCase().replace(/^GFC[\s-]*/, "").replace(/[^A-Z0-9]/g, "");
        let result = "";
        for (const character of characters) {
            if (result.length === 14) break;
            if (result.length >= 8 || /[0-9]/.test(character)) result += character;
        }
        return result;
    }

    function format(value, cursor, showPrefix = true) {
        const data = payload(value);
        const beforeCursor = payload(value.slice(0, cursor)).length;
        input.value = !data && !showPrefix ? "" : prefix + data.slice(0, 8)
            + (data.length >= 8 ? "-" + data.slice(8) : "");
        if (showPrefix) {
            const position = Math.min(input.value.length,
                prefix.length + beforeCursor + (beforeCursor >= 8 ? 1 : 0));
            input.setSelectionRange(position, position);
        }
    }

    input.addEventListener("focus", () => {
        if (!input.value) {
            input.value = prefix;
            input.setSelectionRange(prefix.length, prefix.length);
        }
    });
    input.addEventListener("input", event => {
        if (!event.isComposing) format(input.value, input.selectionStart);
    });
    input.addEventListener("compositionend", () => format(input.value, input.selectionStart));
    input.addEventListener("blur", () => {
        if (!payload(input.value)) input.value = "";
    });
    input.addEventListener("keydown", event => {
        if (input.selectionStart !== input.selectionEnd) return;
        const cursor = input.selectionStart;
        if (event.key === "Backspace") {
            if (cursor <= prefix.length) event.preventDefault();
            // Backspace over the date separator should delete the preceding digit.
            else if (cursor === 13) input.setSelectionRange(12, 12);
        } else if (event.key === "Delete") {
            // Skip fixed characters so Delete operates on the next editable character.
            if (cursor < prefix.length) input.setSelectionRange(prefix.length, prefix.length);
            else if (cursor === 12) input.setSelectionRange(13, 13);
        }
    });
    input.addEventListener("paste", event => {
        const text = event.clipboardData?.getData("text");
        if (!text) return;
        event.preventDefault();
        // Replace the whole field when pasting a complete reference, even when
        // the fixed prefix is already present. This avoids maxlength truncation.
        if (/^\s*GFC[\s-]*/i.test(text)) {
            format(text.trim(), text.trim().length);
        } else {
            const start = Math.max(prefix.length, input.selectionStart);
            const end = Math.max(start, input.selectionEnd);
            format(input.value.slice(0, start) + text + input.value.slice(end), start + text.length);
        }
    });

    if (input.value) format(input.value, input.value.length, false);
})();
