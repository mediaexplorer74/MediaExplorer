# MediaExplorer 0.38.49 - dev branch ("WebView" codename; pre-alpha)

![](/Images/logo.png)


## About / Об этой "музейной" штучке
MediaExplorer (another strange "WebView" codename) is planned as alternative Browser for old sweet Windows 10 Mobile (W10M, >15063). 

Based on my old fork of https://github.com/UDAIE-A/WEBVIEW . 


## Abstract / What's this? / Чё это?!

MediaExplorer is an autobiographical thing, the idea of which arose somewhere in 2021, about 5 years ago (although my nickname of the same name is approx. 40 years old, he has been since the days of the introduction of Internet Explorer and Media Player). The essence of the project is the following version of a web browser for old-school Winphones, so that it is not based on Chakra (MS's htmlEDGE engine). In the era of the powerful heyday of AI, sometimes I strain all sorts of DeepSeek in the "background" mode-and with a plan to bring the project at least to the opening of simple sites like DuckDickGo :)

MediaExplorer -- автобиографичная вещь, идея которой возникла где-то в 2021 году, лет так 5 назад (хотя одноименному нику-то моему лет так почти 40, он со времен появления Internet Explorer и Media Player). Суть проекта -- исследовательский вариант веб-браузера для олдскульных винфонов, да такой, чтоб не базировался на Чакре (майковском движке htmlEDGE). В эпоху мощного расцвета ИИ иногда в "фоновом режиме" напрягаю всякие DeepSeek-и c планом довести проект хоть до открытия простеньких сайтов типа DuckDickGo :)

For more tech. info see /Doc folder (Plans, Summeries on partially completed "dev phases").


## Screenshots

![](/Images/snapshot01.png)
![](/Images/snapshot02.png)
![](/Images/snapshot03.png)


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


## Problems / Knows Bugs / Limitaions

- This src is 100 % "neuro-slop" made by AI (except UDAIE-A's WebView src code, ported from WP8 to UWP)!
- Not tested on any W10M device , no any appx yet.
- All experimental features (retro-futuristic UI, AI support, etc.) are HIGHLY UNFINISHED (недопилены, по-русски говоря!)


## Credits / Благодарности
- https://github.com/UDAIE-A Developer of original WEBVIEW for Windows Phone 8.1
- https://github.com/UDAIE-A/WEBVIEW WebView, experimental alternative browser (wp8, not uwp)


## DEV Summary (DEV Progress)

- Goal

Patch NiL.JS 2.6 parser to support modern ES features (import.meta, logical assignment, arrow functions) required by Vite bundles (Nokia Design Archive).


Constraints & Preferences

Сборка через msbuild.

NiL.JS подключен через ProjectReference (не NuGet).

Nokia Design Archive (568KB Vite bundle) — главный бенчмарк.

Decision: Patch NiL.JS parser directly rather than using a regex pre-processor.

- Progress

Done

T-M-001 (Inline Module) ✅ PASS.

T-M-002 (Import/Export) ✅ PASS: Fixed TryGetModule to use request.Initiator.FilePath for base URI resolution.
import.meta Support ✅:

Created NiL.JS/Expressions/ImportMeta.cs (follows NewTarget pattern).

Added parser rules in NiL.JS/Core/Parser.cs and ExpressionTree.cs.

Added ImportMeta property to NiL.JS/Module.cs.

Fixed runtime null issue by initializing _oValue = Dictionary<string, JSValue>.

console.warn/error/info Support ✅: Added methods to HostConsole in JavaScriptEngine.cs.

Logical Assignment (??=, ||=, &&=) ✅:

Created NiL.JS/Expressions/LogicalAssignment.cs.

Added 3 new OperationTypes and parser logic in ExpressionTree.cs.

172 occurrences in Vite bundle now work.

Documentation: Created Doc/Summary_3_9.md, updated Doc/Plan_03.md (Phase 20 status, Vite analysis table).

- In Progress

Arrow Function Fix (Position 58): var G0=e=>{throw TypeError(e)} fails with SyntaxError: Invalid function name. Added ParseArrowFunction in 
FunctionDefinition.cs but encountering build errors (CS0117, CS0200 regarding VariableDescriptor/FunctionInfo).

- Blocked

Async Generator (Position 410): async function*(){} not supported by NiL.JS parser yet.
Private Fields (#name): 12 occurrences in Vite bundle, not yet started.

- Key Decisions

import.meta runtime implementation: Use direct _oValue = Dictionary<string, JSValue> initialization instead of GlobalContext.ProxyValue to avoid null reference issues during property access.

Logical Assignment: Created dedicated LogicalAssignment expression node rather than expanding existing Assignment logic, to handle short-circuit evaluation correctly.

- Next Steps

Fix Arrow Function build errors in FunctionDefinition.cs (resolve VariableDescriptor constructor and FunctionInfo initialization issues).
Add async function* (async generator) support for position 410.
Re-test Nokia Archive to verify Vite bundle execution.

Investigate Private Fields (#name) support if needed.

- Critical Context

Vite Bundle Analysis (main-BE-aXEfW.js, 568KB):

import.meta.url (1) — ✅ Fixed.

??=, ||=, &&= (172) — ✅ Fixed.

async function* (1) — ❌ Blocker at pos 410.

Arrow functions e=>{...} (1) — ❌ Blocker at pos 58 (build in progress).

Private fields #name (12) — ⏳ Lower priority.

NiL.JS Parser Structure: Uses ExpressionTree.cs for operator parsing and FunctionDefinition.cs for function bodies. Arrow functions were previously only supported via ValidateArrow rules in Parser.cs but failed in var declaration context.
ModuleLoader BaseUri Tracking: _moduleBaseUris dictionary added to resolve relative imports using request.Initiator.

- Relevant Files

NiL.JS/Expressions/LogicalAssignment.cs: New class for logical assignment operators.

NiL.JS/Expressions/ImportMeta.cs: New class for import.meta expression.

NiL.JS/Expressions/FunctionDefinition.cs: Being modified to support arrow functions in variable declarations.

NiL.JS/Expressions/ExpressionTree.cs: Modified to parse ||=, &&=, ??= and detect arrow functions.

NiL.JS/Module.cs: Added ImportMeta property initialization.

Engine/ModuleLoader.cs: Fixed relative module resolution.

Engine/JavaScriptEngine.cs: Added console.warn/error/info.

Doc/Summary_3_9.md: Session summary for NiL.JS parser patches.

Doc/Plan_03.md: Updated Phase 20 status and Vite bundle analysis.


## ..
As is. No support. RnD only. DIY.


## .
[m][e] May, 20 2026
