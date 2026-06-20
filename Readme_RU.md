# MediaExplorer 2.0-alpha — ai_hub branch


![](/Images/logo.png)

## Что это?

MediaExplorer — это хобби-браузер для Windows 10 Mobile (W10M, сборка 15063+), созданный **без** использования системного WebView или движка Chakra. Использует собственный HTML-парсер, CSS-движок с селекторами/каскадом/flexbox, JavaScript-рантайм на базе [NiL.JS](https://github.com/nilproject/NiL.JS) и рендерер на XAML.

**v1.5 — AI Hub** — прокручиваемое меню инструментов (Избранное, История, AI Summary, Скриншот, Копирование, E-book, DevTools, Настройки, Home, Engine, Remote Session). Когда сайт не может отрендериться, **Smart Fallback** автоматически загружает страницу, отправляет в AI-коннектор (OpenRouter API) и показывает summary в AI-панели Hub.

~50 файлов, ~30k+ строк кода. Ранняя стадия **v2.0-alpha**, выросшая из завершённой архитектуры v1.5.

## Скриншоты

![](/Images/sshot01.png)
![](/Images/sshot02.png)
![](/Images/sshot03.png)


## Возможности
- AppBar Situational Size, Hybrid Search, Multi-Engine Architecture, Advanced AI Connector, Remote Rendering [Playwright], DzenRu Auth2 Tweak
- **Собственный движок рендеринга** — HTML-парсер, CSS-каскад, flexbox, XAML-рендерер
- **JavaScript** — NiL.JS с поддержкой ES Modules (Vite bundles parse; D3v4/v5 support)
- **AI Hub** — Прокручиваемое меню: Избранное, История, AI Summary, Скриншот, Копирование, E-book, DevTools, Настройки, Home, Engine, Remote Session
- **Automatic Rescue Path** — Сломанный локальный рендер теперь классифицируется (empty render, block/challenge, code junk, minimal text, network-like), предлагает rescue-варианты, запоминает rescue preference по сайту и умеет забывать их по требованию
- **Smart Fallback** — Авто-детекция сломанного рендера → загрузка страницы → отправка в AI-коннектор → summary в режиме чтения
- **AI Коннекторы** — Rich (GPT-4o), Poor (Ministral-8B), Asceti (Gemma-4 бесплатно), Smart (авто-цепочка). Разные API-ключи.
- **Стартовый дашборд** — Сетка быстрого доступа (6 закреплённых сайтов) + последние посещения
- **История** — Автозапись навигации, группировка по датам, очистка
- **Избранное** — Добавление текущей страницы, удаление по элементу
- **DevTools** — Консоль, DOM-инспектор, Network, Debug-лог
- **3 режима UI** — Скрытый, Полуоткрытый, Полный
- **3 e-book режима** — Rich, Poor, Asceti
- **Дисковый кэш** — Кеширование ресурсов с приоритетной очередью
- **MutationObserver** — Инкрементальный ре-рендер при изменениях DOM
- **Скриншот** — Скриншот (обычный или длинный) в Pictures/MediaExplorer
- **Горячие клавиши** — Ctrl+L (URL), Ctrl+B (панель), Ctrl+Home (дашборд)
- **Карточный режим** — Автоопределение узкого экрана, свайпаемые карточки
- **Nokia Design Archive** — Извлечение entries/collections/stories из JS-глобалов

## Статус

- **v2.0-alpha в разработке.** Plan 8 завершён; Plan 09 идёт: полировка Hub, полировка Dashboard, RemoteRender settings/session UX и Automatic Rescue Path.
- **Архитектура v1.5 была завершена ранее.** AI Hub, Стартовый дашборд, История/Избранное, AI-коннекторы, Smart Fallback Renderer.
- **Извлечение данных сохранено** — `__graphData`, `__entries` (722), `__stories` (230), `__collections` (33) для будущего SkiaSharp-рендерера.

## Вехи разработки

- **2026.06.14 — v1.5.0** AI Hub, Стартовый дашборд, История/Избранное, AI-коннекторы (Rich/Poor/Asceti/Smart), Smart Fallback Renderer.
- **2026.06.14 — v1.1.0** E-book режимы, markdown-to-html, charset detection, улучшения таблиц, CSS edge cases.
- **2026.06.13 — v1.0.10** Рендеринг веб-страниц. Hacker News полностью рендерится.
- **2026.06.12 — v1.0.0** Фазы R+S+T. Карточный режим, маршрутизация ссылок.
- **2026.06.07 — v0.55.0** D3.js force-directed граф.
- **2026.06.05 — v0.50.0** Первая d3.js-оценка на UWP через NiL.JS.
- **2026.05.xx — v0.42.8** NiL.JS портирован на .NET Native 1.4.

## Тестирование

MediaExplorer лучше всего работает с Nokia Design Archive. Для общего тестирования веба рекомендуются эти сайты (по совместимости):

| Сайт | Тип | Примечания |
|------|-----|------------|
| [hackaday.com](https://hackaday.com) | Блог/ESP | Текст + картинки, лёгкий JS |
| [arstechnica.com](https://arstechnica.com) | Новости | Тяжёлый React, но есть контент |
| [wikipedia.org](https://wikipedia.org) | Wiki | Сложный HTML/CSS, хорошая нагрузка |
| [archive.org](https://archive.org) | Архив | Похожий дух на Nokia Design Archive |
| [musicbrainz.org](https://musicbrainz.org) | База данных | Текстовые данные, SPA |
| [openlibrary.org](https://openlibrary.org) | Книги | Картинки + текст, умеренный JS |
| [librivox.org](https://librivox.org) | Аудиокниги | Простой HTML, хорошая стабильность |
| [indiehackers.com](https://indiehackers.com) | Сообщество | React SPA, контентный |
| [producthunt.com](https://producthunt.com) | Стартапы | Тяжёлый SPA, хорошая нагрузка |
| [news.ycombinator.com](https://news.ycombinator.com) | Минимализм | **Полностью рендерится** — оранжевый хедер, 30 новостей, стрелки голосования, кликабельные ссылки |

**Совет:** Начните с лайтвейт-сайтов (Hackaday, Wikipedia, LibriVox) на Lumia 950. Тяжёлые SPA (Reddit, Product Hunt) полезны как стресс-тест, но могут сломаться на NiL.JS.

## Известные проблемы

- Исходник на 100% «нейро-слоп» от ИИ (кроме оригинального кода UDAIE-A WebView)
- Не тестировался ни на одном W10M-устройстве
- Белый экран на некоторых сайтах (dzen.ru, ya.ru)
- Ошибки runtime ES Modules всё ещё исправляются
- Приватные поля (`#name`) не поддерживаются
- SVG→XAML рендеринг отброшен — сложные графы/таймлайны отложены до SkiaSharp (v1.1+)

## Благодарности

- [UDAIE-A/WEBVIEW](https://github.com/UDAIE-A/WEBVIEW) — Оригинальный WebView для Windows Phone 8.1
- [NiL.JS](https://github.com/nilproject/NiL.JS) — JavaScript-движок

## Документация

См. папку `/Doc` — планы разработки и итоги сессий.

## Участие в проекте

**Всем фанатам ретро-ОС!** Если у тебя в ящике пылится Lumia 950/1020, или тебе просто нравится идея браузера, которому не нужно 2 ГБ Chromium ради одной вкладки — этот проект ждёт тебя.

- **Разработчики:** Fork, фикс, PR. Код грязный, но честный. Каждая строка выстрадана.
- **W10M-тестеры:** Запусти на своём девайсе, расскажи что сломалось. Твоё железо — настоящий тест-бенч.
- **CSS/JS гики:** Если ты знаешь, почему `calc(100% - 16px)` тут не работает — ты уже понял, что делать.

## Баги и предложения

Нашёл сайт, который криво рендерится? Упало на телефоне? Есть идея?

→ **[Создай Issue](https://github.com/mediaexplorer74/MediaExplorer/Issues)**

Укажи: URL, что ожидал, что получил. Скриншоты помогают.

---

Как есть. Без поддержки. Только RnD. Сделай сам.

[m][e] 15 июня 2026

![](/Images/footer.png)
