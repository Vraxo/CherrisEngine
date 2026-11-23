'use strict';

// Dock layout for the editor page: draggable splitters, persisted like ImGui's ini file.
//
// The layout is three CSS custom properties on #dockspace (--dock-left, --dock-right, --dock-bottom).
// Blazor renders the structure; this module owns the sizes, so dragging never round-trips to .NET.
window.cherrisDock = (() => {
    const STORAGE_KEY = 'cherris.dock.v1';

    const DEFAULTS = { left: 20, right: 24, bottom: 28 };

    // Mirrors the ImGui dock layout's proportions (20% left, 24% right, 72/28 for viewport/bottom) while
    // keeping every pane usable when the window is small.
    const LIMITS = {
        sideMin: 12,
        sideMax: 45,
        sidesMax: 84,
        bottomMin: 14,
        bottomMax: 70
    };

    let current = { ...DEFAULTS };
    let drag = null;

    function clamp(value, min, max) {
        return Math.min(max, Math.max(min, value));
    }

    function apply(root, values) {
        root.style.setProperty('--dock-left', `${values.left}%`);
        root.style.setProperty('--dock-right', `${values.right}%`);
        root.style.setProperty('--dock-bottom', `${values.bottom}%`);
    }

    function load() {
        try {
            const raw = window.localStorage.getItem(STORAGE_KEY);
            const parsed = raw ? JSON.parse(raw) : null;

            if (parsed && typeof parsed.left === 'number' && typeof parsed.right === 'number' &&
                typeof parsed.bottom === 'number') {
                return {
                    left: clamp(parsed.left, LIMITS.sideMin, LIMITS.sideMax),
                    right: clamp(parsed.right, LIMITS.sideMin, LIMITS.sideMax),
                    bottom: clamp(parsed.bottom, LIMITS.bottomMin, LIMITS.bottomMax)
                };
            }
        } catch (error) {
            // A corrupt layout is not worth failing over; fall back to the defaults.
        }

        return { ...DEFAULTS };
    }

    function save() {
        try {
            window.localStorage.setItem(STORAGE_KEY, JSON.stringify(current));
        } catch (error) {
            // Private-mode webviews may refuse storage; the layout then simply resets next launch.
        }
    }

    function onMove(event) {
        if (!drag) {
            return;
        }

        if (drag.axis === 'x') {
            const delta = ((event.clientX - drag.startX) / Math.max(1, drag.rect.width)) * 100;

            if (drag.target === 'left') {
                current.left = clamp(drag.start.left + delta, LIMITS.sideMin,
                    Math.min(LIMITS.sideMax, LIMITS.sidesMax - current.right));
            } else {
                current.right = clamp(drag.start.right - delta, LIMITS.sideMin,
                    Math.min(LIMITS.sideMax, LIMITS.sidesMax - current.left));
            }
        } else {
            const delta = ((event.clientY - drag.startY) / Math.max(1, drag.rect.height)) * 100;
            current.bottom = clamp(drag.start.bottom - delta, LIMITS.bottomMin, LIMITS.bottomMax);
        }

        apply(drag.root, current);
    }

    function onUp() {
        if (!drag) {
            return;
        }

        drag.root.classList.remove('is-dragging');
        document.body.classList.remove('dock-dragging-x', 'dock-dragging-y');
        drag = null;
        save();
    }

    function mount() {
        const root = document.getElementById('dockspace');

        if (!root) {
            return false;
        }

        current = load();
        apply(root, current);

        root.querySelectorAll('[data-dock-splitter]').forEach(splitter => {
            if (splitter.dataset.dockBound === '1') {
                return;
            }

            splitter.dataset.dockBound = '1';

            splitter.addEventListener('mousedown', event => {
                if (event.button !== 0) {
                    return;
                }

                event.preventDefault();

                drag = {
                    root,
                    axis: splitter.dataset.axis,
                    target: splitter.dataset.target,
                    startX: event.clientX,
                    startY: event.clientY,
                    start: { ...current },
                    rect: root.getBoundingClientRect()
                };

                root.classList.add('is-dragging');
                document.body.classList.add(drag.axis === 'x' ? 'dock-dragging-x' : 'dock-dragging-y');
            });
        });

        if (!window.__cherrisDockListening) {
            window.__cherrisDockListening = true;
            window.addEventListener('mousemove', onMove);
            window.addEventListener('mouseup', onUp);
        }

        return true;
    }

    function reset() {
        const root = document.getElementById('dockspace');

        current = { ...DEFAULTS };

        if (root) {
            apply(root, current);
        }

        try {
            window.localStorage.removeItem(STORAGE_KEY);
        } catch (error) {
            // Ignored, as in save().
        }

        return true;
    }

    return {
        mount,
        reset,
        getLayout: () => ({ ...current })
    };
})();

// Small view helpers the panels need: scroll a log to the end without pulling a JS framework in.
window.cherrisUi = {
    scrollToEnd(elementId) {
        const element = document.getElementById(elementId);

        if (element) {
            element.scrollTop = element.scrollHeight;
        }
    },

    focus(element) {
        if (element && element.focus) {
            element.focus();
        }
    }
};
