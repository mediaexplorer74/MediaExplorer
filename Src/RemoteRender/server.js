#!/usr/bin/env node
/**
 * MediaExplorer Remote Renderer — Playwright-based headless browser server
 * 
 * Protocol (WebSocket JSON):
 *   Client → Server:
 *     { type: "render", url: "https://...", width: 412, height: 915, waitMs: 3000 }
 *     { type: "click", x: 200, y: 450 }
 *     { type: "type", selector: "input[name=q]", text: "hello" }
 *     { type: "press", key: "Enter" }
 *     { type: "scroll", deltaY: 300 }
 *     { type: "screenshot" }
 *     { type: "getText" }
 *     { type: "getTitle" }
 *     { type: "close" }
 * 
 *   Server → Client:
 *     { type: "screenshot", data: "<base64 JPEG>", width: 412, height: 915 }
 *     { type: "title", text: "Page Title" }
 *     { type: "text", content: "Extracted text..." }
 *     { type: "error", message: "Error message" }
 *     { type: "ready" }
 * 
 * Usage:
 *   npm install
 *   npm start
 *   # or with ngrok: ngrok http 8081
 */

const { chromium } = require('playwright');
const WebSocket = require('ws');
const http = require('http');

const PORT = parseInt(process.env.PORT || '8081', 10);
const VERBOSE = process.argv.includes('--verbose');
const REMOTE_PIN = (process.env.REMOTE_PIN || '').trim();

let browser = null;
let page = null;
let viewportWidth = 412;
let viewportHeight = 915;

function log(msg) {
    if (VERBOSE) console.log(`[${new Date().toISOString()}] ${msg}`);
}

async function ensureBrowser() {
    if (!browser || !browser.isConnected()) {
        log('Launching browser...');
        browser = await chromium.launch({
            headless: true,
            args: [
                '--no-sandbox',
                '--disable-setuid-sandbox',
                '--disable-dev-shm-usage',
                '--disable-gpu'
            ]
        });
        const context = await browser.newContext({
            viewport: { width: viewportWidth, height: viewportHeight },
            userAgent: 'Mozilla/5.0 (Windows Phone 10.0; Android 6.0.1; Microsoft; Lumia 950) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36'
        });
        page = await context.newPage();
        log('Browser launched');
    }
    return page;
}

async function handleRender(params) {
    const { url, width, height, waitMs = 3000 } = params;
    const p = await ensureBrowser();

    if (width && height) {
        viewportWidth = width;
        viewportHeight = height;
        await p.setViewportSize({ width, height });
    }

    log(`Navigating to ${url}`);
    await p.goto(url, { waitUntil: 'domcontentloaded', timeout: 30000 });

    if (waitMs > 0) {
        await p.waitForTimeout(Math.min(waitMs, 10000));
    }

    const title = await p.title();
    log(`Page loaded: ${title}`);
    return { title };
}

async function handleScreenshot() {
    const p = await ensureBrowser();
    const buffer = await p.screenshot({
        type: 'jpeg',
        quality: 70
    });
    return buffer.toString('base64');
}

async function handleGetText() {
    const p = await ensureBrowser();
    return await p.evaluate(() => document.body?.innerText || '');
}

async function handleGetTitle() {
    const p = await ensureBrowser();
    return await p.title();
}

async function handleClick(params) {
    const p = await ensureBrowser();
    const { x, y } = params;
    await p.mouse.click(x, y);
    await p.waitForTimeout(500);
}

async function handleType(params) {
    const p = await ensureBrowser();
    const { selector, text } = params;
    await p.fill(selector, text);
}

async function handlePress(params) {
    const p = await ensureBrowser();
    const { key } = params;
    await p.keyboard.press(key);
    await p.waitForTimeout(500);
}

async function handleScroll(params) {
    const p = await ensureBrowser();
    const { deltaY } = params;
    await p.mouse.wheel(0, deltaY || 300);
    await p.waitForTimeout(300);
}

async function handleClose() {
    if (page) {
        await page.close().catch(() => {});
        page = null;
    }
    if (browser) {
        await browser.close().catch(() => {});
        browser = null;
    }
    log('Browser closed');
}

function handleMessage(ws, raw) {
    let msg;
    try {
        msg = JSON.parse(raw);
    } catch (e) {
        ws.send(JSON.stringify({ type: 'error', message: 'Invalid JSON' }));
        return;
    }

    const { type, ...params } = msg;
    log(`Received: ${type}`);

    if (REMOTE_PIN && !ws.isAuthed && type !== 'auth') {
        ws.send(JSON.stringify({ type: 'error', message: 'Authentication required' }));
        return;
    }

    switch (type) {
        case 'auth': {
            const pin = (params.pin || '').toString();
            if (!REMOTE_PIN) {
                ws.isAuthed = true;
                ws.send(JSON.stringify({ type: 'auth_ok', message: 'No PIN configured' }));
            } else if (pin === REMOTE_PIN) {
                ws.isAuthed = true;
                ws.send(JSON.stringify({ type: 'auth_ok', message: 'Authenticated' }));
            } else {
                ws.send(JSON.stringify({ type: 'error', message: 'Invalid PIN' }));
            }
            break;
        }
        case 'render':
            handleRender(params)
                .then(r => {
                    ws.send(JSON.stringify({ type: 'ready', title: r.title }));
                    // Auto-screenshot after render
                    return handleScreenshot();
                })
                .then(b64 => {
                    ws.send(JSON.stringify({ type: 'screenshot', data: b64, width: viewportWidth, height: viewportHeight }));
                })
                .catch(e => ws.send(JSON.stringify({ type: 'error', message: e.message })));
            break;

        case 'screenshot':
            handleScreenshot()
                .then(b64 => {
                    ws.send(JSON.stringify({ type: 'screenshot', data: b64, width: viewportWidth, height: viewportHeight }));
                })
                .catch(e => ws.send(JSON.stringify({ type: 'error', message: e.message })));
            break;

        case 'click':
            handleClick(params)
                .then(() => handleScreenshot())
                .then(b64 => {
                    ws.send(JSON.stringify({ type: 'screenshot', data: b64, width: viewportWidth, height: viewportHeight }));
                })
                .catch(e => ws.send(JSON.stringify({ type: 'error', message: e.message })));
            break;

        case 'type':
            handleType(params)
                .catch(e => ws.send(JSON.stringify({ type: 'error', message: e.message })));
            break;

        case 'press':
            handlePress(params)
                .then(() => handleScreenshot())
                .then(b64 => {
                    ws.send(JSON.stringify({ type: 'screenshot', data: b64, width: viewportWidth, height: viewportHeight }));
                })
                .catch(e => ws.send(JSON.stringify({ type: 'error', message: e.message })));
            break;

        case 'scroll':
            handleScroll(params)
                .then(() => handleScreenshot())
                .then(b64 => {
                    ws.send(JSON.stringify({ type: 'screenshot', data: b64, width: viewportWidth, height: viewportHeight }));
                })
                .catch(e => ws.send(JSON.stringify({ type: 'error', message: e.message })));
            break;

        case 'getText':
            handleGetText()
                .then(text => ws.send(JSON.stringify({ type: 'text', content: text })))
                .catch(e => ws.send(JSON.stringify({ type: 'error', message: e.message })));
            break;

        case 'getTitle':
            handleGetTitle()
                .then(title => ws.send(JSON.stringify({ type: 'title', text: title })))
                .catch(e => ws.send(JSON.stringify({ type: 'error', message: e.message })));
            break;

        case 'close':
            handleClose()
                .then(() => ws.send(JSON.stringify({ type: 'closed' })))
                .catch(e => ws.send(JSON.stringify({ type: 'error', message: e.message })));
            break;

        default:
            ws.send(JSON.stringify({ type: 'error', message: 'Unknown command: ' + type }));
    }
}

// HTTP server for health checks
const httpServer = http.createServer((req, res) => {
    if (req.url === '/health') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({
            status: 'ok',
            browser: browser?.isConnected() ? 'connected' : 'disconnected',
            page: page ? 'active' : 'none',
            viewport: `${viewportWidth}x${viewportHeight}`
        }));
    } else if (req.url === '/') {
        res.writeHead(200, { 'Content-Type': 'text/html' });
        res.end(`<html><body style="font-family:monospace;background:#1a1a1a;color:#aaa;padding:20px">
            <h2>MediaExplorer Remote Renderer</h2>
            <p>WebSocket server on port ${PORT}</p>
            <p>Health: <a href="/health" style="color:#6af">/health</a></p>
            <p>Connect via WebSocket: ws://localhost:${PORT}</p>
        </body></html>`);
    } else {
        res.writeHead(404);
        res.end('Not found');
    }
});

// WebSocket server
const wss = new WebSocket.Server({ server: httpServer });

wss.on('connection', (ws, req) => {
    const clientIp = req.socket.remoteAddress;
    ws.isAuthed = !REMOTE_PIN;
    log(`Client connected: ${clientIp}`);

    ws.on('message', (data) => {
        handleMessage(ws, data.toString());
    });

    ws.on('close', () => {
        log(`Client disconnected: ${clientIp}`);
    });

    ws.send(JSON.stringify({
        type: 'hello',
        message: 'MediaExplorer Remote Renderer v1.0',
        viewport: `${viewportWidth}x${viewportHeight}`,
        authRequired: !!REMOTE_PIN
    }));
});

httpServer.listen(PORT, () => {
    console.log(`\n  MediaExplorer Remote Renderer`);
    console.log(`  ============================`);
    console.log(`  WebSocket: ws://localhost:${PORT}`);
    console.log(`  Health:    http://localhost:${PORT}/health`);
    console.log(`\n  For remote access, use ngrok:`);
    console.log(`    ngrok http ${PORT}`);
    console.log(`\n  Ready.\n`);
});

// Graceful shutdown
process.on('SIGINT', async () => {
    log('Shutting down...');
    await handleClose();
    process.exit(0);
});

process.on('SIGTERM', async () => {
    log('Shutting down...');
    await handleClose();
    process.exit(0);
});
