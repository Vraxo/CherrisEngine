'use strict';

// Wire protocol mirror of src/OvercoreEditorSession/Host/ViewportProtocol.cs.
// Binary frame: [format u8][width i32 LE][height i32 LE][sequence i32 LE][RGBA8 pixels, top-down]
const PROTOCOL_VERSION = 1;
const FORMAT_RGBA8 = 1;
const HEADER_SIZE = 13;

const MAX_RENDER_WIDTH = 1920;
const MAX_RENDER_HEIGHT = 1200;
const MIN_RENDER_SIZE = 64;

const canvas = document.getElementById('viewport');
const statusEl = document.getElementById('status');
const overlayEl = document.getElementById('overlay');

let socket = null;
let gl = null;
let program = null;
let texture = null;
let textureWidth = 0;
let textureHeight = 0;

let framesReceived = 0;
let framesPerSecond = 0;
let lastFrameNumber = 0;
let lastStatsAt = performance.now();
let resendResize = null;

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

function showOverlay(message) {
    overlayEl.textContent = message;
    overlayEl.hidden = false;
}

function hideOverlay() {
    overlayEl.hidden = true;
}

function setStatus(text, state) {
    statusEl.textContent = text;
    statusEl.className = state || '';
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
        showOverlay('WebGL2 is not available here.\nUse a browser or webview with WebGL2 support.');
        return false;
    }

    program = createProgram(VERTEX_SHADER, FRAGMENT_SHADER);

    if (!program) {
        return false;
    }

    gl.useProgram(program);

    // Fullscreen triangle strip. v = 0 is the top of the image, matching the host's top-down pixel order,
    // so no flip is needed (UNPACK_FLIP_Y_WEBGL is ignored for typed-array uploads anyway).
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

function createProgram(vertexSource, fragmentSource) {
    const vertexShader = compileShader(gl.VERTEX_SHADER, vertexSource);
    const fragmentShader = compileShader(gl.FRAGMENT_SHADER, fragmentSource);

    if (!vertexShader || !fragmentShader) {
        return null;
    }

    const link = gl.createProgram();
    gl.attachShader(link, vertexShader);
    gl.attachShader(link, fragmentShader);
    gl.linkProgram(link);

    if (!gl.getProgramParameter(link, gl.LINK_STATUS)) {
        showOverlay(`Could not link the frame shader.\n${gl.getProgramInfoLog(link)}`);
        return null;
    }

    gl.deleteShader(vertexShader);
    gl.deleteShader(fragmentShader);
    return link;
}

function compileShader(type, source) {
    const shader = gl.createShader(type);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);

    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
        showOverlay(`Could not compile a frame shader.\n${gl.getShaderInfoLog(shader)}`);
        gl.deleteShader(shader);
        return null;
    }

    return shader;
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
        return; // stale frame, already drew something newer
    }

    lastFrameNumber = sequence;
    framesReceived++;

    const pixels = new Uint8Array(arrayBuffer, HEADER_SIZE, width * height * 4);

    gl.bindTexture(gl.TEXTURE_2D, texture);

    if (width !== textureWidth || height !== textureHeight) {
        textureWidth = width;
        textureHeight = height;
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
        hideOverlay();
    }

    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
    gl.clear(gl.COLOR_BUFFER_BIT);
    gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
}

function send(message) {
    if (socket && socket.readyState === WebSocket.OPEN) {
        socket.send(JSON.stringify(message));
    }
}

function measureViewport() {
    const rect = canvas.parentElement.getBoundingClientRect();
    const ratio = Math.min(window.devicePixelRatio || 1, 2);

    let width = Math.round(rect.width * ratio);
    let height = Math.round(rect.height * ratio);

    width = Math.max(MIN_RENDER_SIZE, width);
    height = Math.max(MIN_RENDER_SIZE, height);

    if (width > MAX_RENDER_WIDTH) {
        height = Math.round(height * MAX_RENDER_WIDTH / width);
        width = MAX_RENDER_WIDTH;
    }

    if (height > MAX_RENDER_HEIGHT) {
        width = Math.round(width * MAX_RENDER_HEIGHT / height);
        height = MAX_RENDER_HEIGHT;
    }

    if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;

        if (gl) {
            gl.viewport(0, 0, width, height);
        }
    }

    send({ type: 'resize', width: width, height: height });
    return { width: width, height: height };
}

function scheduleResize() {
    if (resendResize !== null) {
        clearTimeout(resendResize);
    }

    resendResize = setTimeout(() => {
        resendResize = null;
        measureViewport();
    }, 120);
}

function toViewportPixels(event) {
    const rect = canvas.getBoundingClientRect();
    const scaleX = canvas.width / Math.max(1, rect.width);
    const scaleY = canvas.height / Math.max(1, rect.height);

    return {
        x: (event.clientX - rect.left) * scaleX,
        y: (event.clientY - rect.top) * scaleY
    };
}

function isTextEntry(target) {
    return target instanceof HTMLInputElement ||
        target instanceof HTMLTextAreaElement ||
        target instanceof HTMLSelectElement ||
        (target instanceof HTMLElement && target.isContentEditable);
}

function isPointerLocked() {
    return document.pointerLockElement === canvas;
}

const SCROLL_KEYS = new Set([
    'Space', 'Tab', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight',
    'PageUp', 'PageDown', 'Home', 'End', 'Backspace'
]);

function installInputHandlers() {
    window.addEventListener('keydown', (event) => {
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

    window.addEventListener('keyup', (event) => {
        if (isTextEntry(event.target)) {
            return;
        }

        send({ type: 'key', code: event.code, down: false });
    });

    canvas.addEventListener('contextmenu', (event) => event.preventDefault());

    canvas.addEventListener('mousedown', (event) => {
        canvas.focus();

        if (event.button === 2 && !isPointerLocked() && canvas.requestPointerLock) {
            canvas.requestPointerLock();
        }

        send({ type: 'mouseButton', button: event.button, down: true });
    });

    window.addEventListener('mouseup', (event) => {
        send({ type: 'mouseButton', button: event.button, down: false });
    });

    window.addEventListener('mousemove', (event) => {
        if (isPointerLocked()) {
            send({ type: 'mouseMove', dx: event.movementX, dy: event.movementY });
            return;
        }

        const point = toViewportPixels(event);
        send({ type: 'mouse', x: point.x, y: point.y });
    });

    canvas.addEventListener('wheel', (event) => {
        event.preventDefault();
        send({ type: 'wheel', x: -event.deltaX / 100, y: -event.deltaY / 100 });
    }, { passive: false });

    document.addEventListener('pointerlockchange', () => {
        send({ type: 'lock', locked: isPointerLocked() });
    });

    window.addEventListener('blur', () => send({ type: 'blur' }));
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) {
            send({ type: 'blur' });
        }
    });

    window.addEventListener('resize', scheduleResize);

    if (window.ResizeObserver) {
        new ResizeObserver(scheduleResize).observe(canvas.parentElement);
    }
}

function updateStats() {
    const now = performance.now();
    const elapsed = now - lastStatsAt;

    if (elapsed >= 500) {
        framesPerSecond = Math.round((framesReceived * 1000) / elapsed);
        framesReceived = 0;
        lastStatsAt = now;

        if (socket && socket.readyState === WebSocket.OPEN) {
            setStatus(`${framesPerSecond} fps · ${textureWidth || canvas.width}×${textureHeight || canvas.height} · protocol ${PROTOCOL_VERSION}`, 'connected');
        }
    }

    requestAnimationFrame(updateStats);
}

function connect() {
    const scheme = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    socket = new WebSocket(`${scheme}//${window.location.host}/ws`);
    socket.binaryType = 'arraybuffer';

    socket.addEventListener('open', () => {
        setStatus('connected', 'connected');
        measureViewport();
    });

    socket.addEventListener('message', (event) => {
        if (typeof event.data === 'string') {
            handleTextMessage(event.data);
            return;
        }

        drawFrame(event.data);
    });

    socket.addEventListener('close', () => {
        setStatus('disconnected — retrying…', 'disconnected');
        lastFrameNumber = 0;
        textureWidth = 0;
        textureHeight = 0;
        showOverlay('The editor host closed the connection.\nRetrying…');
        setTimeout(connect, 1000);
    });

    socket.addEventListener('error', () => {
        if (socket) {
            socket.close();
        }
    });
}

function handleTextMessage(text) {
    let message = null;

    try {
        message = JSON.parse(text);
    } catch (error) {
        return;
    }

    if (message && message.type === 'hello') {
        if (message.protocol !== PROTOCOL_VERSION) {
            showOverlay(
                `Protocol mismatch: the host speaks ${message.protocol}, this page speaks ${PROTOCOL_VERSION}.\n` +
                'Rebuild the host or hard-reload the page.');
            return;
        }

        hideOverlay();
        setStatus(`connected · ${message.width}×${message.height} target`, 'connected');
    }
}

if (initGraphics()) {
    installInputHandlers();
    connect();
    requestAnimationFrame(updateStats);
} else {
    showOverlay('The renderer could not start in this browser or webview.');
}
