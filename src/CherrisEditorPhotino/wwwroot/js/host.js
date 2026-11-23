'use strict';

// Small host-side helpers for the Blazor UI. Kept separate from viewport.js so the frame bridge stays
// focused on the wire protocol.
window.cherrisHost = {
    // Copies diagnostics to the clipboard, returning false when the webview refuses (no permission, no
    // clipboard API, or an older WebKitGTK). The caller shows the text anyway, so a failure is cosmetic.
    async copy(text) {
        try {
            if (navigator.clipboard && navigator.clipboard.writeText) {
                await navigator.clipboard.writeText(text);
                return true;
            }
        } catch (error) {
            // Fall through to the legacy path.
        }

        try {
            const area = document.createElement('textarea');
            area.value = text;
            area.setAttribute('readonly', '');
            area.style.position = 'fixed';
            area.style.top = '-1000px';
            area.style.opacity = '0';
            document.body.appendChild(area);
            area.select();
            const copied = document.execCommand('copy');
            document.body.removeChild(area);
            return copied;
        } catch (error) {
            return false;
        }
    }
};
