# Summary_2_12 — Phase 12 (AI Integration: DeepSeek via OpenRouter)

## Goal
Add an AI button to the AppBar that sends current page content to DeepSeek (via OpenRouter API) and displays a summary in a slide-up overlay.

## Changes

### Files modified
- `Engine\OpenRouterClient.cs` — new file, static HTTP client for OpenRouter API
- `MainPage.xaml` — AI button (+1 column), AI overlay panel, API key field in Settings
- `MainPage.xaml.cs` — AI button handler, key persistence, overlay show/hide/copy

---

### 1. OpenRouterClient.cs (new)
Static class with a single `SummarizeAsync(apiKey, pageText)` method:
- POST to `https://openrouter.ai/api/v1/chat/completions`
- Model: `deepseek/deepseek-chat` (DeepSeek V3, cheapest on OpenRouter)
- System prompt: "Summarize in 3-5 concise bullet points"
- Temperature 0.3, max 500 tokens
- Parses JSON response via `Windows.Data.Json` (no Newtonsoft dependency)
- Returns summary string or error message

**Cost per call:** ~$0.0018 (3000 input + 150 output tokens)

---

### 2. MainPage.xaml changes

**AppBar** (6 columns):
| Col | Button | Glyph |
|-----|--------|-------|
| 0 | Back | `SymbolIcon Back` |
| 1 | Forward | `SymbolIcon Forward` |
| 2 | URL omnibox | — |
| 3 | Go | `&#xE721;` |
| **4** | **AI** (new) | `&#xE8F1;` |
| 5 | Settings | `&#xE713;` |

**AI overlay panel:**
- Title bar ("Thinking..." / "AI Summary")
- Scrollable TextBlock for result
- Copy button + Close button

**Settings overlay** — added before Clear Cache:
```
OpenRouter API Key
[TextBox: sk-or-v1-...]
```

---

### 3. MainPage.xaml.cs changes

| Method | Role |
|--------|------|
| `AiButton_Click` | Load key → extract page text via `BrowserHost.GetTextContent()` (fallback: `LiteElement.CollectText()`) → truncate at 16KB → call OpenRouter → show result |
| `ShowAiResult(text, isLoading)` | Toggle AI overlay, set title/result text, manage copy button visibility |
| `AiCloseButton_Click` | Hide overlay |
| `AiCopyButton_Click` | Copy result to clipboard via `DataPackage` |
| `LoadAiKey` / `SaveAiKey` | Persist key in `ApplicationData.LocalSettings["OpenRouterKey"]` |

**Text extraction** uses `_browser.GetTextContent()` which already walks `LiteElement.SelfAndDescendants()` collecting text — no new DOM traversal code needed.

---

## Build result
- **0 errors expected** (UWP requires VS)
- **~95 lines added** across 3 files (new `OpenRouterClient.cs` + ~50 lines XAML + ~45 lines C#)

## Next
Phase 11 (UI Polish) — loading spinner, swipe nav, reading mode, URL autocomplete, error page styling, clear cache button.
