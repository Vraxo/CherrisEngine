// Theme application for the Blazor editor.
//
// A theme is data: Overcore.UI.EditorTheme.ToCssVariables() produces the custom properties below, and this
// file does nothing but put them on the document root. The stylesheet's :root block holds the default theme
// as a fallback, so the UI is styled before Blazor starts; this then overrides it with whichever theme the
// editor has selected.
//
// The last applied set is mirrored into localStorage and replayed by the inline script in index.html, so a
// window that opens on a non-default theme does not flash the default one first.

(function () {
    "use strict";

    const storageKey = "overcore.theme";

    function applyTo(tokens) {
        const root = document.documentElement;

        for (const name of Object.keys(tokens || {})) {
            if (!name.startsWith("--theme-")) {
                continue;
            }

            root.style.setProperty(name, tokens[name]);
        }
    }

    window.overcoreTheme = {
        // Applies a theme's variables and remembers them for the next window.
        apply: function (tokens) {
            applyTo(tokens);

            try {
                localStorage.setItem(storageKey, JSON.stringify(tokens));
            } catch {
                // A locked-down profile only costs the flash-free start, not the theme.
            }
        },

        // Drops the overrides so the stylesheet's own defaults show again.
        reset: function () {
            const root = document.documentElement;
            const names = [];

            for (let i = 0; i < root.style.length; i++) {
                const name = root.style.item(i);

                if (name.startsWith("--theme-")) {
                    names.push(name);
                }
            }

            for (const name of names) {
                root.style.removeProperty(name);
            }

            try {
                localStorage.removeItem(storageKey);
            } catch {
                // Ignored: nothing to clear.
            }
        }
    };
})();
