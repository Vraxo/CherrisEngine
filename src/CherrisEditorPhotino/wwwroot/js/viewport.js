'use strict';

// Viewport bridge for the PhotinoX Blazor host.
//
// Mirrors the wire protocol in src/CherrisEditorWeb/Host/ViewportProtocol.cs:
//   binary frame: [format u8][width i32 LE][height i32 LE][sequence i32 LE][RGBA8 pixels, top-down]
//
// Blazor mounts this against a canvas element and hands it the WebSocket URL of the in-process editor
// host. Pixels travel over the socket; the Blazor components host the rest of the UI, so nothing here
// assumes it owns the document.
window.cherrisViewport = (() => {
    const PROTOCOL_VERSION = 1;
    const FORMAT_RGBA8 = 1;
    const HEADER_SIZE = 13;

    const MAX_RENDER_WIDTH = 1920;
    const MAX_RENDER_HEIGHT = 1200;
    const MIN_RENDER_SIZE = 64;
    const RECONNECT_DELAY_MS = 1000;

    const VERTEX_SHADER = `#version 300 es
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;
out vec2 vTexCoord;

void main() {
    vTexCoord = aTexCoord;
    gl_Position = vec4(aPosition, 0.0, 1.0);
}`;

    const FRAGMENT_SHADER = `#version 300 es
precision mediump float;
in vec2 vTexCoord;
uniform sampler2D uFrame;
out vec4 outColor;

void main() {
    outColor = vec4(texture(uFrame, vTexCoord).rgb, 1.0);
}`;

    const SCROLL_KEYS = new Set([
        'Space', 'Tab', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight',
        'PageUp', 'PageDown', 'Home', 'End', 'Backspace'
    ]);

    let active = null;

    function mount(canvasId, socketUrl) {
        dispose();

        const canvas = document.getElementById(canvasId);

        if (!canvas) {
            return false;
        }

        const overlay = document.getElementById(`${canvasId}-overlay`);
        active = createViewport(canvas, overlay, socketUrl);
        active.start();
        return true;
    }

    function dispose() {
        if (active) {
            active.dispose();
            active = null;
        }
    }

    function getStats() {
        if (!active) {
            return { connected: false, fps: 0, width: 0, height: 0, frames: 0, status: 'idle', message: '' };
        }

        return active.stats();
    }

    function createViewport(canvas, overlay, socketUrl) {
        const listeners = [];
        let socket = null;
        let disposed = false;
        let reconnectTimer = null;
        let gl = null;
        let program = null;
        let texture = null;
        let textureWidth = 0;
        let textureHeight = 0;
        let lastFrameNumber = 0;
        let framesReceived = 0;
        let framesPerSecond = 0;
        let lastStatsAt = performance.now();
        let totalFrames = 0;
        let status = 'connecting';
        let message = '';

        function on(target, type, handler, options) {
            target.addEventListener(type, handler, options);
            listeners.push(() => target.removeEventListener(type, handler, options));
        }

        function setOverlay(text) {
            message = text ?? '';

            if (!overlay) {
                return;
            }

            overlay.textContent = message;
            overlay.hidden = message.length === 0;
        }

        function compileShader(type, source) {
            const shader = gl.createShader(type);
            gl.shaderSource(shader, source);
            gl.compileShader(shader);

            if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
                setOverlay(`Could not compile a frame shader.\n${gl.getShaderInfoLog(shader)}`);
                gl.deleteShader(shader);
                return null;
            }

            return shader;
        }

        function createProgram() {
            const vertexShader = compileShader(gl.VERTEX_SHADER, VERTEX_SHADER);
            const fragmentShader = compileShader(gl.FRAGMENT_SHADER, FRAGMENT_SHADER);

            if (!vertexShader || !fragmentShader) {
                return null;
            }

            const link = gl.createProgram();
            gl.attachShader(link, vertexShader);
            gl.attachShader(link, fragmentShader);
            gl.linkProgram(link);

            if (!gl.getProgramParameter(link, gl.LINK_STATUS)) {
                setOverlay(`Could not link the frame shader.\n${gl.getProgramInfoLog(link)}`);
                return null;
            }

            gl.deleteShader(vertexShader);
            gl.deleteShader(fragmentShader);
            return link;
        }

        function initGraphics() {
            gl = canvas.getContext('webgl2', {
                alpha: false,
                antialias: false,
                depth: false,
                stencil: false,
                preserveDrawingBuffer: false
            });

            if (!gl) {
                status = 'error';
                setOverlay('WebGL2 is not available in this webview, so frames cannot be displayed.');
                return false;
            }

            program = createProgram();

            if (!program) {
                status = 'error';
                return false;
            }

            gl.useProgram(program);

            // Fullscreen triangle strip. v = 0 is the top of the image, matching the host's top-down
            // pixel order, so no flip is needed (UNPACK_FLIP_Y_WEBGL is ignored for typed-array uploads).
            const quad = new Float32Array([
                -1, -1, 0, 1,
                 1, -1, 1, 1,
                -1,  1, 0, 0,
                 1,  1, 1, 0
            ]);

            const buffer = gl.createBuffer();
            gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
            gl.bufferData(gl.ARRAY_BUFFER, quad, gl.STATIC_DRAW);

            const positionLocation = gl.getAttribLocation(program, 'aPosition');
            const texCoordLocation = gl.getAttribLocation(program, 'aTexCoord');

            gl.enableVertexAttribArray(positionLocation);
            gl.vertexAttribPointer(positionLocation, 2, gl.FLOAT, false, 16, 0);
            gl.enableVertexAttribArray(texCoordLocation);
            gl.vertexAttribPointer(texCoordLocation, 2, gl.FLOAT, false, 16, 8);

            texture = gl.createTexture();
            gl.bindTexture(gl.TEXTURE_2D, texture);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
            gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE,
                new Uint8Array([16, 17, 19, 255]));

            gl.clearColor(0.06, 0.07, 0.08, 1.0);
            gl.clear(gl.COLOR_BUFFER_BIT);

            return true;
        }

        function drawFrame(arrayBuffer) {
            if (!gl || !texture || arrayBuffer.byteLength < HEADER_SIZE) {
                return;
            }

            const header = new DataView(arrayBuffer);

            if (header.getUint8(0) !== FORMAT_RGBA8) {
                return;
            }

            const width = header.getInt32(1, true);
            const height = header.getInt32(5, true);
            const sequence = header.getInt32(9, true);
            const expected = HEADER_SIZE + width * height * 4;

            if (width <= 0 || height <= 0 || arrayBuffer.byteLength < expected) {
                return;
            }

            if (sequence <= lastFrameNumber && sequence !== 0) {
                return; // stale frame, something newer was already drawn
            }

            lastFrameNumber = sequence;
            framesReceived++;
            totalFrames++;

            const pixels = new Uint8Array(arrayBuffer, HEADER_SIZE, width * height * 4);

            gl.bindTexture(gl.TEXTURE_2D, texture);

            if (width !== textureWidth || height !== textureHeight) {
                textureWidth = width;
                textureHeight = height;
                gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
            }

            gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
            gl.clear(gl.COLOR_BUFFER_BIT);
            gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
        }

        function send(payload) {
            if (socket && socket.readyState === WebSocket.OPEN) {
                socket.send(JSON.stringify(payload));
            }
        }

        function measureViewport() {
            const container = canvas.parentElement ?? canvas;
            const rect = container.getBoundingClientRect();
            const ratio = Math.min(window.devicePixelRatio || 1, 2);

            let width = Math.max(MIN_RENDER_SIZE, Math.round(rect.width * ratio));
            let height = Math.max(MIN_RENDER_SIZE, Math.round(rect.height * ratio));

            if (width > MAX_RENDER_WIDTH) {
                height = Math.round(height * MAX_RENDER_WIDTH / width);
                width = MAX_RENDER_WIDTH;
            }

            if (height > MAX_RENDER_HEIGHT) {
                width = Math.round(width * MAX_RENDER_HEIGHT / height);
                height = MAX_RENDER_HEIGHT;
            }

            // The canvas is stretched by CSS; only change its backing size when the request changed, so a
            // resize storm cannot allocate a new drawing buffer on every mousemove.
            if (canvas.width !== width || canvas.height !== height) {
                canvas.width = width;
                canvas.height = height;

                if (gl) {
                    gl.viewport(0, 0, width, height);
                }
            }

            send({ type: 'resize', width: width, height: height });
        }

        function installInput() {
            on(window, 'keydown', (event) => {
                if (isTextEntry(event.target)) {
                    return;
                }

                if (SCROLL_KEYS.has(event.code)) {
                    event.preventDefault();
                }

                if (event.repeat) {
                    return;
                }

                send({ type: 'key', code: event.code, down: true });
            });

            on(window, 'keyup', (event) => {
                if (isTextEntry(event.target)) {
                    return;
                }

                send({ type: 'key', code: event.code, down: false });
            });

            on(canvas, 'contextmenu', (event) => event.preventDefault());

            on(canvas, 'mousedown', (event) => {
                canvas.focus();

                if (event.button === 2 && !isPointerLocked() && canvas.requestPointerLock) {
                    canvas.requestPointerLock();
                }

                send({ type: 'mouseButton', button: event.button, down: true });
            });

            on(window, 'mouseup', (event) => send({ type: 'mouseButton', button: event.button, down: false }));

            on(window, 'mousemove', (event) => {
                if (isPointerLocked()) {
                    send({ type: 'mouseMove', dx: event.movementX, dy: event.movementY });
                    return;
                }

                const rect = canvas.getBoundingClientRect();
                send({
                    type: 'mouse',
                    x: (event.clientX - rect.left) * (canvas.width / Math.max(1, rect.width)),
                    y: (event.clientY - rect.top) * (canvas.height / Math.max(1, rect.height))
                });
            });

            on(canvas, 'wheel', (event) => {
                event.preventDefault();
                send({ type: 'wheel', x: -event.deltaX / 100, y: -event.deltaY / 100 });
            }, { passive: false });

            on(document, 'pointerlockchange', () => send({ type: 'lock', locked: isPointerLocked() }));
            on(window, 'blur', () => send({ type: 'blur' }));
            on(document, 'visibilitychange', () => {
                if (document.hidden) {
                    send({ type: 'blur' });
                }
            });

            on(window, 'resize', measureViewport);

            if (window.ResizeObserver) {
                const observer = new ResizeObserver(measureViewport);
                observer.observe(canvas.parentElement ?? canvas);
                listeners.push(() => observer.disconnect());
            }
        }

        function handleText(text) {
            let payload = null;

            try {
                payload = JSON.parse(text);
            } catch (error) {
                return;
            }

            if (!payload || payload.type !== 'hello') {
                return;
            }

            if (payload.protocol !== PROTOCOL_VERSION) {
                status = 'error';
                setOverlay(
                    `Protocol mismatch: the host speaks ${payload.protocol}, this view speaks ${PROTOCOL_VERSION}.\n` +
                    'Rebuild the host and the viewport script together.');
                return;
            }

            status = 'connected';
            setOverlay('');
        }

        function connect() {
            if (disposed) {
                return;
            }

            status = 'connecting';
            socket = new WebSocket(socketUrl);
            socket.binaryType = 'arraybuffer';

            socket.addEventListener('open', () => {
                status = 'connected';
                setOverlay('');
                measureViewport();
            });

            socket.addEventListener('message', (event) => {
                if (typeof event.data === 'string') {
                    handleText(event.data);
                    return;
                }

                drawFrame(event.data);
            });

            socket.addEventListener('close', () => {
                if (disposed) {
                    return;
                }

                status = 'disconnected';
                lastFrameNumber = 0;
                textureWidth = 0;
                textureHeight = 0;
                setOverlay('The editor host closed the connection. Retrying…');
                reconnectTimer = setTimeout(connect, RECONNECT_DELAY_MS);
            });

            socket.addEventListener('error', () => {
                if (socket) {
                    socket.close();
                }
            });
        }

        function isPointerLocked() {
            return document.pointerLockElement === canvas;
        }

        function stats() {
            const now = performance.now();
            const elapsed = now - lastStatsAt;

            // Frames arrive on the socket's schedule, not ours, so sample the rate only once the caller has
            // actually waited long enough to average over.
            if (elapsed >= 400) {
                framesPerSecond = Math.round((framesReceived * 1000) / elapsed);
                framesReceived = 0;
                lastStatsAt = now;
            }

            return {
                connected: socket !== null && socket.readyState === WebSocket.OPEN,
                fps: framesPerSecond,
                width: textureWidth,
                height: textureHeight,
                frames: totalFrames,
                status: status,
                message: message
            };
        }

        return {
            start() {
                if (initGraphics()) {
                    installInput();
                    connect();
                }
            },
            dispose() {
                disposed = true;

                if (reconnectTimer !== null) {
                    clearTimeout(reconnectTimer);
                    reconnectTimer = null;
                }

                for (const remove of listeners) {
                    try {
                        remove();
                    } catch (error) {
                        // The document may already be tearing down; nothing to clean up then.
                    }
                }

                listeners.length = 0;

                if (socket) {
                    try {
                        socket.close();
                    } catch (error) {
                        // Already closed.
                    }

                    socket = null;
                }

                if (gl && texture) {
                    gl.deleteTexture(texture);
                }

                if (gl && program) {
                    gl.deleteProgram(program);
                }
            },
            stats
        };
    }

    function isTextEntry(target) {
        return target instanceof HTMLInputElement ||
            target instanceof HTMLTextAreaElement ||
            target instanceof HTMLSelectElement ||
            (target instanceof HTMLElement && target.isContentEditable);
    }

    return {
        protocol: PROTOCOL_VERSION,
        getProtocol: () => PROTOCOL_VERSION,
        mount,
        dispose,
        getStats
    };
})();
