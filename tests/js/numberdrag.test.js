'use strict';

// Checks for wwwroot/js/numberdrag.js, the horizontal drag on number fields. It runs without a browser: the
// script only touches `window`, `document` and the input it is given, so a plain object stands in for each.
//
// Run with: node tests/js/numberdrag.test.js   (CI does, after `dotnet test`.)

const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');

const source = fs.readFileSync(
    path.join(__dirname, '..', '..', 'src', 'OvercoreEditorPhotino', 'wwwroot', 'js', 'numberdrag.js'),
    'utf8');

let documentHandlers = {};
let windowHandlers = {};
let frames = new Map();
let nextFrameId = 0;

function load() {
    documentHandlers = {};
    windowHandlers = {};
    frames = new Map();
    nextFrameId = 0;

    global.window = {
        addEventListener: (type, handler) => { windowHandlers[type] = handler; },
        removeEventListener: (type) => { delete windowHandlers[type]; },
        // A frame is only run when a test asks for one, so a per-frame publish can be counted exactly.
        requestAnimationFrame: (callback) => { frames.set(++nextFrameId, callback); return nextFrameId; },
        cancelAnimationFrame: (id) => { frames.delete(id); }
    };
    global.document = {
        addEventListener: (type, handler) => { documentHandlers[type] = handler; }
    };

    // eslint-disable-next-line no-eval -- the script is an IIFE that installs one global, as in the browser.
    eval(source);

    return window.overcoreNumberDrag;
}

/** An `<input class="drag-number">` with the data attributes the panel renders. */
function makeInput(data = {}) {
    return {
        value: '1',
        dataset: Object.assign({ speed: '0.1', value: '1' }, data),
        isConnected: true,
        readOnly: false,
        disabled: false,
        focused: false,
        events: [],
        matches: (selector) => selector === 'input.drag-number',
        classList: {
            classes: new Set(),
            add(name) { this.classes.add(name); },
            remove(name) { this.classes.delete(name); }
        },
        // One element has the focus at a time, so focusing this one takes it off whoever had it, as a browser
        // would. `document.activeElement` is what the script reads and what a test can inspect.
        focus() {
            const previous = document.activeElement;

            this.focused = true;
            document.activeElement = this;

            if (previous && previous !== this && typeof previous.blur === 'function') {
                previous.blur();
            }
        },
        blur() {
            this.focused = false;

            if (document.activeElement === this) {
                document.activeElement = undefined;
            }
        },
        setSelectionRange() {},
        dispatchEvent(event) { this.events.push(event.type); return true; }
    };
}

function press(input, pointerId, clientX = 0, modifiers = {}) {
    documentHandlers.pointerdown(Object.assign({
        target: input, button: 0, pointerId, clientX, ctrlKey: false, preventDefault() {}
    }, modifiers));
}

function move(pointerId, clientX, modifiers = {}) {
    windowHandlers.pointermove(Object.assign({ pointerId, clientX, preventDefault() {} }, modifiers));
}

function release(pointerId) {
    windowHandlers.pointerup({ pointerId });
}

/** Runs the animation frames that are due, as the browser would once the frame is drawn. */
function tick() {
    const due = [...frames.values()];

    frames.clear();
    due.forEach((callback) => callback());
}

const results = [];

function check(name, run) {
    try {
        run();
        results.push('PASS ' + name);
    } catch (error) {
        results.push('FAIL ' + name + '\n     ' + error.message);
    }
}

load();

check('a drag publishes one change per frame, so the scene follows the pointer', () => {
    const input = makeInput();
    press(input, 1, 100);
    move(1, 110);
    assert.strictEqual(input.value, '2');
    assert.deepStrictEqual(input.events, ['input'], 'the movement opens the drag before any value is sent');

    // Nothing is sent until the frame is drawn, however many moves land in it.
    move(1, 115);
    assert.deepStrictEqual(input.events, ['input']);
    tick();
    assert.deepStrictEqual(input.events, ['input', 'change']);

    move(1, 130);
    tick();
    assert.strictEqual(input.value, '4');
    assert.deepStrictEqual(input.events, ['input', 'change', 'change']);

    release(1);
    assert.deepStrictEqual(input.events, ['input', 'change', 'change'], 'the last value was already published');
    assert.deepStrictEqual(Object.keys(windowHandlers), [], 'listeners are removed on release');
});

check('a release before the next frame still sends the value the pointer is on', () => {
    const input = makeInput();
    press(input, 2, 0);
    move(2, 30);
    release(2);

    assert.strictEqual(input.value, '4');
    assert.deepStrictEqual(input.events, ['input', 'change']);
});

check('a second drag of the same field opens a step of its own', () => {
    const input = makeInput();

    press(input, 3, 0);
    move(3, 10);
    tick();
    release(3);

    press(input, 3, 0);
    move(3, 10);
    tick();
    release(3);

    assert.deepStrictEqual(input.events, ['input', 'change', 'input', 'change']);
});

check('a drag holds the focus while it runs, and lets go when it ends', () => {
    const left = makeInput();
    const dragged = makeInput();

    left.focus();
    press(dragged, 17, 0);
    move(17, 40);

    assert.strictEqual(dragged.focused, true, "the dragged field takes the caret, so the next keystroke lands in it");
    assert.strictEqual(left.focused, false, 'the field the drag moved away from gives the caret up');
    assert.strictEqual(dragged.value, '5');

    release(17);

    assert.strictEqual(dragged.focused, false, 'and the end of the drag is what the panel rebuilds the field on');

    delete document.activeElement;
});

check('a press that does not move places the caret instead of editing', () => {
    const input = makeInput();
    press(input, 4, 100);
    move(4, 102);
    release(4);

    assert.strictEqual(input.value, '1');
    assert.strictEqual(input.focused, true);
    assert.deepStrictEqual(input.events, []);
});

check('Shift is ten times faster and Alt a hundredth', () => {
    const fast = makeInput();
    press(fast, 3, 0);
    move(3, 10, { shiftKey: true });
    assert.strictEqual(fast.value, '11');
    release(3);

    const slow = makeInput();
    press(slow, 4, 0);
    move(4, 10, { altKey: true });
    assert.strictEqual(slow.value, '1.01');
    release(4);
});

check('the declared bounds clamp the drag', () => {
    const input = makeInput({ min: '0', max: '1' });
    press(input, 5, 0);
    move(5, 500);
    assert.strictEqual(input.value, '1');
    move(5, -500);
    assert.strictEqual(input.value, '0');
    release(5);
});

check('integers round to whole numbers', () => {
    const input = makeInput({ integer: 'true', speed: '1' });
    press(input, 6, 0);
    move(6, 3);
    assert.strictEqual(input.value, '4');
    release(6);
});

check('no floating point noise in the field', () => {
    const input = makeInput({ value: '0.1', speed: '0.1' });
    input.value = '0.1';
    press(input, 7, 0);
    move(7, 2);
    move(7, 3);
    assert.strictEqual(input.value, '0.4');
    release(7);
});

check('a cancel keeps the value the pointer reached, which the scene already has', () => {
    const input = makeInput();
    press(input, 8, 0);
    move(8, 40);
    windowHandlers.pointercancel({ pointerId: 8 });

    assert.strictEqual(input.value, '5');
    assert.deepStrictEqual(input.events, ['input', 'change'], 'one last value, and no frame left pending');
    assert.deepStrictEqual(Object.keys(windowHandlers), []);
});

check('an element re-rendered mid-drag is dropped rather than written', () => {
    const input = makeInput();
    press(input, 9, 0);
    move(9, 40);
    input.isConnected = false;
    release(9);

    assert.deepStrictEqual(input.events, ['input'], 'a replaced input cannot be committed through');
});

check('a drag continues from the value the field shows', () => {
    const input = makeInput();
    input.value = '5';
    press(input, 16, 0);
    move(16, 10);

    assert.strictEqual(input.value, '6', 'the typed 5 is the starting point, not the rendered 1');
    release(16);
});

check('a drag that ends on its starting value sends no value at all', () => {
    const input = makeInput();
    press(input, 10, 0);
    move(10, 40);
    move(10, 0);
    release(10);

    assert.strictEqual(input.value, '1');
    assert.deepStrictEqual(input.events, ['input'], 'the drag opened, but the value never left where it was');
});

check('other inputs are left alone', () => {
    const input = makeInput();
    input.matches = () => false;
    press(input, 11, 0);

    assert.deepStrictEqual(Object.keys(windowHandlers), []);
    assert.strictEqual(input.value, '1');
});

check('a right click or a ctrl click does not start a drag', () => {
    const right = makeInput();
    documentHandlers.pointerdown({ target: right, button: 2, pointerId: 12, clientX: 0, preventDefault() {} });
    assert.deepStrictEqual(Object.keys(windowHandlers), []);

    const ctrl = makeInput();
    press(ctrl, 13, 0, { ctrlKey: true });
    assert.deepStrictEqual(Object.keys(windowHandlers), []);
});

check('a disabled or read-only field does not start a drag', () => {
    const disabled = makeInput();
    disabled.disabled = true;
    press(disabled, 14, 0);
    assert.deepStrictEqual(Object.keys(windowHandlers), []);

    const readOnly = makeInput();
    readOnly.readOnly = true;
    press(readOnly, 15, 0);
    assert.deepStrictEqual(Object.keys(windowHandlers), []);
});

console.log(results.join('\n'));

if (results.some((line) => line.startsWith('FAIL'))) {
    process.exitCode = 1;
}
