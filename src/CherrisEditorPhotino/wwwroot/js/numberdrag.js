'use strict';

// Horizontal drag for number fields, the way ImGui's DragFloat/DragInt behave: press and move sideways to
// scrub the value, click without moving to type instead. It is wired by a class rather than per element, so
// any input marked `drag-number` — including ones Blazor adds later — scrubs without further JavaScript.
//
// Nothing round-trips to .NET while the pointer is down: the field shows the scrubbed value itself and one
// `change` event is dispatched on release, which is the event typing already sends. A whole drag is therefore
// a single edit, and so a single undo step, which is what PropertyDrawer does with IsItemDeactivatedAfterEdit.
window.cherrisNumberDrag = (() => {
    const THRESHOLD = 3;   // pixels of movement before a press counts as a drag rather than a click
    const DECIMALS = 3;    // matches the "0.###" the panels read and write
    const FAST = 10;       // Shift, as in ImGui
    const SLOW = 0.01;     // Alt, as in ImGui

    let drag = null;

    function bound(text) {
        if (text === undefined || text === '') {
            return null;
        }

        const parsed = Number.parseFloat(text);
        return Number.isFinite(parsed) ? parsed : null;
    }

    function setup(input) {
        const speed = Number.parseFloat(input.dataset.speed);

        // What the field shows wins over what was rendered into it: the author may have typed a value and
        // gone straight to dragging, and the drag should continue from the number they can see.
        const typing = Number.parseFloat(input.value);
        const rendered = Number.parseFloat(input.dataset.value);

        return {
            speed: Number.isFinite(speed) && speed > 0 ? speed : 0.01,
            start: Number.isFinite(typing) ? typing : Number.isFinite(rendered) ? rendered : 0,
            min: bound(input.dataset.min),
            max: bound(input.dataset.max),
            integer: input.dataset.integer === 'true'
        };
    }

    function format(value, integer) {
        if (integer) {
            return String(Math.round(value));
        }

        // 0.1 + 0.1 + 0.1 must read as 0.3, not 0.30000000000000004.
        return String(Number(value.toFixed(DECIMALS)));
    }

    function begin(event) {
        const input = event.target;

        if (drag || event.button !== 0 || event.ctrlKey || !input.matches ||
            !input.matches('input.drag-number') || input.readOnly || input.disabled) {
            return;
        }

        // Taking the press keeps the browser from selecting text or placing a caret; a click that never moves
        // puts the caret back in on release, so typing still works.
        event.preventDefault();

        drag = {
            input,
            pointerId: event.pointerId,
            startX: event.clientX,
            text: input.value,
            config: setup(input),
            moving: false
        };

        window.addEventListener('pointermove', move, true);
        window.addEventListener('pointerup', end, true);
        window.addEventListener('pointercancel', cancel, true);
        window.addEventListener('blur', cancel);
    }

    function move(event) {
        if (!drag || event.pointerId !== drag.pointerId) {
            return;
        }

        const dx = event.clientX - drag.startX;

        if (!drag.moving) {
            if (Math.abs(dx) < THRESHOLD) {
                return;
            }

            drag.moving = true;
            drag.input.classList.add('dragging');

            // A focused field scrolls its text under the pointer instead of staying put.
            drag.input.blur();
        }

        event.preventDefault();

        let delta = dx * drag.config.speed;

        if (event.altKey) {
            delta *= SLOW;
        }

        if (event.shiftKey) {
            delta *= FAST;
        }

        let value = drag.config.start + delta;

        if (drag.config.min !== null) {
            value = Math.max(drag.config.min, value);
        }

        if (drag.config.max !== null) {
            value = Math.min(drag.config.max, value);
        }

        drag.input.value = format(value, drag.config.integer);
    }

    function end(event) {
        if (!drag || event.pointerId !== drag.pointerId) {
            return;
        }

        const state = drag;
        stop();
        state.input.classList.remove('dragging');

        if (dragWasDropped(state)) {
            return;
        }

        if (!state.moving) {
            // A click: the press was taken earlier, so the caret is placed here instead.
            state.input.focus();
            const caret = state.input.value.length;
            state.input.setSelectionRange(caret, caret);
            return;
        }

        if (state.input.value !== state.text) {
            // The same event typing sends: the panel reads the value back and queues one edit.
            state.input.dispatchEvent(new Event('change', { bubbles: true }));
        }
    }

    function cancel() {
        if (!drag) {
            return;
        }

        const state = drag;
        stop();
        state.input.classList.remove('dragging');

        if (!dragWasDropped(state)) {
            state.input.value = state.text;
        }
    }

    // The element went away mid-drag — the panel re-rendered it — so there is nothing left to commit.
    function dragWasDropped(state) {
        return !state.input.isConnected;
    }

    function stop() {
        drag = null;
        window.removeEventListener('pointermove', move, true);
        window.removeEventListener('pointerup', end, true);
        window.removeEventListener('pointercancel', cancel, true);
        window.removeEventListener('blur', cancel);
    }

    document.addEventListener('pointerdown', begin, true);

    return { THRESHOLD, DECIMALS };
})();
