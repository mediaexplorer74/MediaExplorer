# MediaExplorer 0.38.61 - dev branch (museum build; pre-alpha)

![](/Images/logo.png)


## About / Об этой "музейной" штучке
MediaExplorer (another strange "WebView" codename) is planned as alternative Browser for old sweet Windows 10 Mobile (W10M, >15063). 

Based on my old fork of https://github.com/UDAIE-A/WEBVIEW . 


## Abstract / What's this? / Чё это?!

MediaExplorer is an autobiographical thing, the idea of which arose somewhere in 2021, about 5 years ago (although my nickname of the same name is approx. 40 years old, he has been since the days of the introduction of Internet Explorer and Media Player). The essence of the project is the following version of a web browser for old-school Winphones, so that it is not based on Chakra (MS's htmlEDGE engine). In the era of the powerful heyday of AI, sometimes I strain all sorts of DeepSeek in the "background" mode-and with a plan to bring the project at least to the opening of simple sites like DuckDickGo :)

MediaExplorer -- автобиографичная вещь, идея которой возникла где-то в 2021 году, лет так 5 назад (хотя одноименному нику-то моему лет так почти 40, он со времен появления Internet Explorer и Media Player). Суть проекта -- исследовательский вариант веб-браузера для олдскульных винфонов, да такой, чтоб не базировался на Чакре (майковском движке htmlEDGE). В эпоху мощного расцвета ИИ иногда в "фоновом режиме" напрягаю всякие DeepSeek-и c планом довести проект хоть до открытия простеньких сайтов типа DuckDickGo :)

For more tech. info see /Doc folder (Plans, Summeries on partially completed "dev phases").


## Screenshots

![](/Images/sshot01.png)
![](/Images/sshot02.png)
![](/Images/sshot03.png)
![](/Images/sshot04.png)
![](/Images/sshot05.png)


## Status / Статус

INIT STATE: The project is in a very early stage and not yet available for distribution.

НАЧАЛО: Очень сыро, на уровне ранней пре-альфы. По сути "музейный варик", чисто вдохновить кого-то нестандартно подойти к разработке браузеров и не делать из Chromuim недостижимый фетиш. 

Project status: This is a homemade browser engine (HTML/CSS/JS) for W10M without using the system WebView. Its own HTML parser, CSS engine with selectors/cascade/flexbox, JavaScript runtime in NiL.JS, XAML renderer. About 35 files, ~20k+ lines of code.

Состояние проекта: Это самодельный браузерный движок (HTML/CSS/JS) для W10M без использования системного WebView. Свой HTML-парсер, CSS-движок с селекторами/каскадом/flexbox, JavaScript-рантайм на NiL.JS, XAML-рендерер. Около 35 файлов, ~20k+ строк кода.


## What's new?

:: Changed things :: 

- Improved NiL.JS (planned to ES Modules / Vite support)
- DevTools Console (Console/DOM/Network/Debug)
- Snapshot button at App Bar -- did usual screnshot / long-weight (big) screenshot and stores it to Pictures/MediaExplorer folder.
- 3 UI modes: HIDED ("App Bar Strip"); SEMI ("Semi-visivle&Expandable App Bar"); FULL (Usual App Bar)
- 3 "Rendering" modes : FULL (JS+HTML); RICH (Lite JS+HTML for Easy Reading+ AI "magic cursor") ; POOR (Hello, old-school FIDO!))) 
- Indicator 2→3px, wider than 30→36px MainPage.xaml is visually more noticeable
- PointerEntered → cursor Hand MainPage.xaml.cs When the cursor is hovered over, it changes to "hand" — it is clear that it is clickable
- Ctrl+L → expand + focus URL MainPage.xaml.cs Classic Browser shortcut
- Ctrl+B → toggle bar MainPage.xaml.cs Quick hide/show panel
- Now the panel also works on the mouse: click on the strip, Ctrl+L for the URL, Ctrl+B for minimizing / expanding.


:: Что изменил ::

- Improved NiL.JS (планируется для поддержки ES Modules / Vite)
-  DevTools (Console/DOM/Network/Debug)
- Разработческая пимпа "Snapshot" в app bar -- делает обычный / долговязый ("большой") скриншот в папке Pictures/MediaExplorer.
- 3 режима UI: Скрытный ("Нижния панель как полоска"); Полу-открытый (Привет, Surface Duo!); Полный (обычная нижняя панель)
- 3 режима рендеринга : Полный (JS+HTML); Обогащённый (Лайтовый JS+HTML для ИИ-режима чтения с "волшебным курсором") ; Обедненный (Привет, FIDO-NET!))) 
- BarStrip высота 8→20px	MainPage.xaml	Полоску видно + легко кликнуть мышью
- Индикатор 2→3px, шире 30→36px	MainPage.xaml	Визуально заметнее
- PointerEntered → курсор Hand	MainPage.xaml.cs	При наведении курсор меняется на «руку» — понятно что кликабельно
- Ctrl+L → expand + фокус URL	MainPage.xaml.cs	Классический шорткат браузеров
- Ctrl+B → toggle bar	MainPage.xaml.cs	Быстрое скрытие/показ панели
- Теперь панель работает и на мышке: клик по полоске, Ctrl+L для URL, Ctrl+B для сворачивания/разворачивания.

## Dev details

### Goal
- Finalize NiL.JS 2.6 parser for Vite bundles + Polish UI (Toast, Navigation, Settings) and address runtime errors.

### Constraints & Preferences
- Сборка через msbuild.
- NiL.JS подключен через ProjectReference.
- Nokia Design Archive (568KB Vite bundle) — главный бенчмарк.
- Decision: Patch NiL.JS parser directly; fix UI/Nav issues in MediaExplorer.

### Progress
- NiL.JS - important parsing improvements

### Done
- Full Vite Bundle Parse ✅: 568KB bundle parses without syntax errors (fixed import( spacing, IIFE after function, const{x} without space, async arrow await).
- Toast Notifications ✅: Added ShowToast() (5s duration, 60px above App Bar), triggered on Snapshot/Copy.
- MessageOverlay ✅: Renamed ErrorOverlay → MessageOverlay (generic, supports icons/titles).
- Status Bar Toggle ✅: Added toggle in Settings, persists via StatusBarVisible key.
- Navigation Fix ✅: Fixed GoBack()/GoForward() loop; AddHistory now called before UpdateState. Full cycle (Nokia → Ya.ru → Back → Forward) works.
- Module Logs ✅: import("_") now logs as "Vite feature detection" instead of "failed".
- querySelectorAll ✅: Implemented in HostDocument with MatchesSelector (supports tag/attribute selectors).

### In Progress
- Runtime Error: Investigating TypeError: Type "<_nilInit>b__236_5" can not be created with new keyword.
- Log Noise: Reducing System.InvalidOperationException spam in JavaScriptEngine.cs.

### Blocked
(none)

### Key Decisions
- Navigation History: NavigateAsync accepts addToHistory flag; Back/Forward pass false to prevent re-adding URLs.
- Toast Positioning: Margin="12,12,12,60" ensures visibility even when DevTools is open.
- Log Cleanup: Special case for spec == "_" in ModuleLoader to distinguish expected Vite probes from real failures.

### Next Steps
- Fix new keyword error: Resolve Type "<_nilInit>b__236_5" can not be created with new keyword in NiL.JS runtime.
- Reduce Log Noise: Suppress or filter InvalidOperationException spam in JavaScriptEngine.cs.
-  Private Fields: Investigate support for #name syntax (12 occurrences in bundle).

### Critical Context
- Vite Bundle: Now parses fully; runtime errors are the primary blocker.
- Error Pattern: Type "<_nilInit>b__236_5" can not be created with new keyword suggests anonymous lambda types are being instantiated via new in JS, which NiL.JS rejects.
- Log Spam: ~315 empty catch blocks in JavaScriptEngine.cs log InvalidOperationException, cluttering output during Vite execution.


## Problems / Knows Bugs / Limitaions

- This src is 100 % "neuro-slop" made by AI (except UDAIE-A's WebView src code, ported from WP8 to UWP)!
- Not tested on any W10M device , no any appx yet.
- All experimental features (retro-futuristic UI, AI support, etc.) are HIGHLY UNFINISHED (недопилены, по-русски говоря!)


## Credits / Благодарности
- https://github.com/UDAIE-A Developer of original WEBVIEW for Windows Phone 8.1
- https://github.com/UDAIE-A/WEBVIEW WebView, experimental alternative browser (wp8, not uwp)

## ..
As is. No support. RnD only. DIY.


## .
[m][e] May, 20 2026


![](/Images/footer.png)

[ https://www.youtube.com/watch?v=WqC0xVLGdAY ]
