# MediaExplorer 2.0-alpha — ai_hub branch

![](/Images/logo.png)

## 这是什么？

MediaExplorer 是一个为 Windows 10 Mobile（W10M，版本 15063+）开发的业余浏览器项目，**不使用**系统 WebView 或 Chakra 引擎。它使用自定义 HTML 解析器、支持选择器/层叠/flexbox 的 CSS 引擎、基于 [NiL.JS](https://github.com/nilproject/NiL.JS) 的 JavaScript 运行时，以及基于 XAML 的渲染器。

**v1.5 — AI Hub** — 可滚动工具菜单（收藏夹、历史、AI 摘要、截图、复制、电子书模式、DevTools、设置、Home、Engine、Remote Session）。当网站无法被 NiL.JS 渲染时，**Smart Fallback** 自动获取页面，发送到 AI 连接器（OpenRouter API），并在 Hub 的 AI 面板中显示摘要。

约 50 个文件，约 3 万行代码。当前属于早期 **v2.0-alpha** 阶段，建立在已完成的 v1.5 架构之上。

## 截图

![](/Images/sshot01.png)
![](/Images/sshot02.png)
![](/Images/sshot03.png)


## 功能
- AppBar Situational Size, Hybrid Search, Multi-Engine Architecture, Advanced AI Connector, Remote Rendering [Playwright], DzenRu Auth2 Tweak
- **自定义渲染引擎** — HTML 解析器、CSS 层叠、flexbox、XAML 渲染器
- **JavaScript** — NiL.JS 运行时，支持 ES Modules
- **AI Hub** — 可滚动工具菜单：收藏夹、历史、AI 摘要、截图、复制、电子书模式、DevTools、设置、Home、Engine、Remote Session
- **Remote Rendering [Playwright]** — 可配置 server URL、viewport、load delay、PIN / token 字段、connect/reconnect/disconnect、remote text fetch，以及基于远程页面文本的 AI 摘要
- **Smart Fallback** — 自动检测渲染失败 → 获取页面 → 发送到 AI 连接器 → 在阅读模式中显示摘要
- **AI 连接器预设** — Rich (GPT-4o)、Poor (Ministral-8B)、Asceti (Gemma-4 免费)、Smart (自动链式)。每个连接器独立 API 密钥。
- **起始仪表板** — 快速拨号网格（6 个固定站点）+ 最近访问列表
- **历史记录** — 自动记录导航，按日期分组，一键清除
- **收藏夹** — 添加当前页面，逐条删除
- **开发者工具** — 控制台、DOM 检查器、网络标签、调试日志
- **3 种 UI 模式** — 隐藏、半展开、完整
- **3 种电子书模式** — Rich、Poor、Asceti
- **磁盘缓存** — 带优先队列的资源缓存
- **MutationObserver** — DOM 变化时增量重渲染
- **截图** — 单页或长页截图保存到 Pictures/MediaExplorer
- **键盘快捷键** — Ctrl+L（地址栏）、Ctrl+B（切换栏）、Ctrl+Home（仪表板）
- **智能手机卡片模式** — 自动检测窄屏，可滑动卡片
- **Nokia Design Archive 集成** — 从 JS 全局变量提取数据

## 状态

- **v2.0-alpha 开发中。** Plan 8 已完成；Plan 09 正在推进：Hub 打磨、Dashboard 打磨、RemoteRender settings/session UX 与 Automatic Rescue Path。
- **v1.5 架构已在此前完成。** AI Hub、起始仪表板、历史/收藏夹、AI 连接器、Smart Fallback Renderer。
- **数据提取保持完好** — 留作未来 SkiaSharp 渲染器使用。

## 开发里程碑

- **2026.06.14 — v1.5.0** AI Hub、起始仪表板、历史/收藏夹、AI 连接器（Rich/Poor/Asceti/Smart）、Smart Fallback Renderer。
- **2026.06.14 — v1.1.0** 电子书模式、markdown-to-html、charset detection、表格改进、CSS 边缘情况。
- **2026.06.13 — v1.0.10** 通用网页渲染。Hacker News 完整渲染。
- **2026.06.12 — v1.0.0** 阶段 R+S+T。卡片布局、链接路由。
- **2026.06.07 — v0.55.0** D3.js 力导向图。
- **2026.06.05 — v0.50.0** 首次 d3.js 在 UWP 上通过 NiL.JS 执行。
- **2026.05.xx — v0.42.8** NiL.JS 移植到 .NET Native 1.4。

## 测试

MediaExplorer 与 Nokia Design Archive 配合最佳。进行一般网页测试时，推荐以下网站（按兼容性排序）：

| 网站 | 类型 | 说明 |
|------|------|------|
| [hackaday.com](https://hackaday.com) | 博客/ESP | 文本 + 图片，轻量 JS |
| [arstechnica.com](https://arstechnica.com) | 新闻 | 重量级 React，但有内容 |
| [wikipedia.org](https://wikipedia.org) | 维基 | 复杂 HTML/CSS，良好负载测试 |
| [archive.org](https://archive.org) | 档案馆 | 与 Nokia Design Archive 精神相似 |
| [musicbrainz.org](https://musicbrainz.org) | 数据库 | 文本数据，SPA |
| [openlibrary.org](https://openlibrary.org) | 图书 | 图片 + 文本，中等 JS |
| [librivox.org](https://librivox.org) | 有声书 | 简单 HTML，良好稳定性测试 |
| [indiehackers.com](https://indiehackers.com) | 社区 | React SPA，内容丰富 |
| [producthunt.com](https://producthunt.com) | 初创公司 | 重量级 SPA，良好压力测试 |
| [news.ycombinator.com](https://news.ycombinator.com) | 极简主义 | **完整渲染** — 橙色头部、30条新闻、投票箭头、可点击链接 |

**提示：** 在 Lumia 950 上从轻量网站开始（Hackaday、Wikipedia、LibriVox）。重量级 SPA（Reddit、Product Hunt）可作为压力测试，但在 NiL.JS 上可能会出错。

## 已知问题

- 源代码由 AI 生成（"AI 混合体"），除原始 UDAIE-A WebView 代码外
- 未在任何 W10M 设备上测试
- 部分网站白屏（dzen.ru、ya.ru）
- ES Modules 运行时错误仍在修复中
- 不支持私有字段（`#name`）
- SVG→XAML 渲染已放弃 — 复杂图形/时间线推迟到 SkiaSharp（v1.1+）

## 致谢

- [UDAIE-A/WEBVIEW](https://github.com/UDAIE-A/WEBVIEW) — Windows Phone 8.1 原始 WebView
- [NiL.JS](https://github.com/nilproject/NiL.JS) — JavaScript 引擎

## 文档

请参阅 `/Doc` 文件夹中的开发计划和会话总结。

## 贡献

**召唤所有复古计算爱好者！** 如果你的 Lumia 950/1020 正在抽屉里吃灰，或者你只是喜欢"不需要 2GB Chromium 就能打开网页"这个想法——这个项目需要你。

- **开发者：** Fork、修复、提交 PR。代码很乱，但很真诚。每一行都是拼出来的。
- **W10M 测试者：** 在你的设备上试试，报告什么问题。你的硬件才是真正的测试平台。
- **CSS/JS 极客：** 如果你知道为什么 `calc(100% - 16px)` 在这里不工作——你已经知道该做什么了。

## 问题与反馈

发现某个网站渲染不对？手机上报错了？有功能建议？

→ **[提交 Issue](https://github.com/mediaexplorer74/MediaExplorer/Issues)**

请包含：URL、你期望看到什么、实际看到了什么。截图很有帮助。

---

按原样提供。不提供支持。仅用于研究。自己动手。

[m][e] 2026 年 6 月 20 日

![](/Images/footer.png)