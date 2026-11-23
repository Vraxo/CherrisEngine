'use strict';

// Horizontal drag for number fields, the way ImGui's DragFloat/DragInt behave: press and move sideways to
// scrub the value, click without moving to type instead. It is wired by a class rather than per element, so
// any input marked `drag-number` — including ones Blazor adds later — scrubs without further JavaScript.
//
// The value is *live*: while the pointer moves, one `change` event goes out per animation frame, which is the
// event typing sends, so the scene follows the pointer at frame rate. The panel feeds those into a single
// command, so the whole drag is one undo step — the same compromise PropertyDrawer makes by recording on
// IsItemDeactivatedAfterEdit. The `input` event that opens a drag is what tells the panel a new one has begun,
// so two drags of the same field never merge into one step.
window.cherrisNumberDrag = (() => {
    const THRESHOLD = 3;   // pixels of movement before a press counts as a drag rather than a click
    const DECIMALS = 3;    // matches the "0.###" the panels read and write
    const FAST = 10;       // Shift, as in ImGui
    const SLOW = 0.01;     // Alt, as in ImGui

    let drag = null;
    let frame = 0;

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

    // One `change` per animation frame: the render thread drains its queue once a frame, so publishing faster
    // would only queue work that a later tick overwrites.
    function publish() {
        frame = 0;

        if (!drag || !drag.moving || drag.input.value === drag.published) {
            return;
        }

        drag.published = drag.input.value;
        drag.input.dispatchEvent(new Event('change', { bubbles: true }));
    }

    function schedule() {
        if (frame) {
            return;
        }

        frame = typeof window.requestAnimationFrame === 'function'
            ? window.requestAnimationFrame(publish)
            : 0;

        if (!frame) {
            publish();
        }
    }

    function cancelFrame() {
        if (!frame) {
            return;
        }

        if (typeof window.cancelAnimationFrame === 'function') {
            window.cancelAnimationFrame(frame);
        }

        frame = 0;
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
            published: input.value,
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

            // A field that kept the caret from an earlier click would take the next keystroke, so it is blurred
            // — unless it is the field being dragged, whose element must survive the drag.
            const active = document.activeElement;

            if (active && active !== drag.input && typeof active.blur === 'function') {
                active.blur();
            }

            // Opens a new live session: the panel closes whatever the previous drag recorded before this one
            // writes its first value, so each drag is its own undo step.
            drag.input.dispatchEvent(new Event('input', { bubbles: true }));
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
        schedule();
    }

    function end(event) {
        if (!drag || event.pointerId !== drag.pointerId) {
            return;
        }

        finish();
    }

    // A cancelled pointer or a window that lost focus ends the drag where it is: the scene already has every
    // value published so far, so there is nothing to put back.
    function cancel() {
        if (drag) {
            finish();
        }
    }

    function finish() {
        const state = drag;

        drag = null;
        stop();

        if (!state.input.isConnected) {
            return;
        }

        state.input.classList.remove('dragging');

        if (!state.moving) {
            // A click: the press was taken earlier, so the caret is placed here instead.
            state.input.focus();
            const caret = state.input.value.length;
            state.input.setSelectionRange(caret, caret);
            return;
        }

        // The value the pointer last moved to, which the frame that is now cancelled would have published;
        // nothing goes out when the drag ended on the value it started from.
        if (state.input.value !== state.published) {
            state.published = state.input.value;
            state.input.dispatchEvent(new Event('change', { bubbles: true }));
        }
    }

    function stop() {
        cancelFrame();

        window.removeEventListener('pointermove', move, true);
        window.removeEventListener('pointerup', end, true);
        window.removeEventListener('pointercancel', cancel, true);
        window.removeEventListener('blur', cancel);
    }

    document.addEventListener('pointerdown', begin, true);

    return { THRESHOLD, DECIMALS };
})();
