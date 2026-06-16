# MediaExplorer Remote Renderer

Headless browser server for MediaExplorer using Playwright. Streams screenshots to UWP client via WebSocket.

## Quick Start

```bash
cd Src/RemoteRender
npm install
npm start
```

## Remote Access (Ngrok)

```bash
ngrok http 8081
# Use the ngrok URL in MediaExplorer: ws://your-ngrok-url
```

## Protocol

### Client → Server

| Command | Params | Description |
|---------|--------|-------------|
| `render` | `url`, `width`, `height`, `waitMs` | Navigate and render page |
| `screenshot` | — | Take screenshot |
| `click` | `x`, `y` | Click at coordinates |
| `type` | `selector`, `text` | Type into element |
| `press` | `key` | Press keyboard key |
| `scroll` | `deltaY` | Scroll vertically |
| `getText` | — | Extract page text |
| `getTitle` | — | Get page title |
| `close` | — | Close browser |

### Server → Client

| Event | Data | Description |
|-------|------|-------------|
| `screenshot` | `data` (base64 JPEG) | Screenshot image |
| `title` | `text` | Page title |
| `text` | `content` | Page text content |
| `ready` | `title` | Page loaded |
| `error` | `message` | Error message |
| `hello` | `message`, `viewport` | Connection established |
