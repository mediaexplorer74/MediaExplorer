# MediaExplorer 1.0.10 — main branch

![](/Images/logo.png)

## 这是什么？

MediaExplorer 是一个为 Windows 10 Mobile（W10M，版本 15063+）开发的业余浏览器项目，**不使用**系统 WebView 或 Chakra 引擎。它使用自定义 HTML 解析器、支持选择器/层叠/flexbox 的 CSS 引擎、基于 [NiL.JS](https://github.com/nilproject/NiL.JS) 的 JavaScript 运行时，以及基于 XAML 的渲染器。

**v1.0 针对 [Nokia Design Archive](https://nokiadesignarchive.aalto.fi/) 进行了优化** — 一个拥有 722+ 条目、33 个收藏集、230 个故事和 91 个关键词的博物馆网站，记录了诺基亚的设计历史。MediaExplorer 从网站的 JavaScript 包中提取存档数据，将其渲染为交互式可滑动卡片，包含图片、收藏集标签、关键词标签以及指向阿尔托大学仓库的链接。

约 40 个文件，约 2.5 万行代码。v1.0 发布版。

## 截图

![](/Images/sshot01.png)
![](/Images/sshot02.png)
![](/Images/sshot03.png)
![](/Images/sshot04.png)


## 功能

- **自定义渲染引擎** — HTML 解析器、CSS 层叠、flexbox、XAML 渲染器
- **JavaScript** — NiL.JS 运行时，支持 ES Modules（Vite bundles parse; D3v4/v5 support）
- **开发者工具** — Console、DOM 检查器、Network 标签、Debug 日志
- **3 种 UI 模式** — 隐藏（条状）、半展开、完整（标准应用栏）
- **3 种渲染模式** — 完整（JS+CSS）、丰富（CSS 无 JS）、极简（纯文本）
- **磁盘缓存** — 带优先队列的资源缓存
- **MutationObserver** — DOM 变化时增量重渲染
- **截图按钮** — 截图（单页或长页）保存到 Pictures/MediaExplorer
- **键盘快捷键** — Ctrl+L（聚焦地址栏）、Ctrl+B（切换应用栏）
- **智能手机卡片模式** — 自动检测窄屏幕（<600px），以可滑动卡片形式显示存档内容，支持详情报卡、分类目录和链接路由
- **Nokia Design Archive 集成** — 从 JS 全局变量提取 __entries、__collections、__stories；渲染为交互式卡片，包含类型标签、收藏集标签和历史引用

## 状态

- **v1.0 发布。** 阶段 R（精简）、S（智能手机适配）、T（文本/图像/链接详情视图）已完成。
- **SVG→XAML 桥已放弃** — 经过 18+ 次开发会话，架构不匹配被认定为在 Win SDK 15063 上无法修复。替换为为窄屏设计的文本/卡片渲染。
- **数据提取保持完好** — `__graphData`（755 节点 + 1647 链接）、`__entries`（722）、`__stories`（230）、`__collections`（33）留作未来 SkiaSharp 渲染器使用。

## 开发状态（2026年6月13日）

### 关键决策
- **SVG→XAML 已移除** — 滚动时内容重复、架构不匹配、对 Lumia 来说太重
- **卡片模式** — 窄屏透明替代方案：可滑动卡片，包含条目详情、类别目录、收藏集筛选
- **链接路由** — 内部条目 URL（/entry/E0001）→ 卡片模式；外部 URL → 通过 Launcher 打开系统浏览器
- **数据提取** — 保留供未来 SkiaSharp Canvas2D 渲染器使用
- **通用网页渲染（v1.1）** — 自定义 RenderTreeBuilder 渲染真实网站。首次成功渲染：Hacker News，含30条新闻、橙色头部、投票箭头、可点击链接和页脚

### 关键文件
`MainPage.xaml.cs` — 卡片模式、导航、链接路由
`Engine/BrowserApi.cs` — ExtractEntriesJson/ExtractCollectionsJson/ExtractStoriesJson
`Engine/Core/RenderTreeBuilder.cs` — HTML→RenderObject 树、UA 样式、HTML 展示属性
`Engine/Core/RenderBox.cs` — Flex/block/inline 布局引擎
`Engine/Core/VirtualizingRenderer.cs` — 基于 Canvas 的虚拟化渲染器，支持懒加载图片
`Engine/CssLoader.cs` — CSS 层叠、选择器、媒体查询、伪类
`AGENTS.md` — 自动化循环命令

这是一个自制的浏览器引擎——不是生产级产品，也不打算替代 Edge 或 Chrome。它的存在是为了证明：渲染网页不一定需要 Chromium。

## 开发里程碑

- **2026.06.13 — v1.0.10** 通用网页渲染。Hacker News 完整渲染：橙色头部、30条新闻、投票箭头、可点击链接、页脚。CSS 伪类（`:link`/`:visited`）、HTML 展示属性（`bgcolor`、`width`）、SVG 图标。
- **2026.06.12 — v1.0.0** 阶段 R+S+T 完成。智能手机卡片布局、条目详情视图、类别目录、链接路由。SVG→XAML 桥放弃。
- **2026.06.07 — v0.55.0** D3.js 力导向图（诺基亚设计档案馆）渲染为实时 XAML 元素。
- **2026.06.05 — v0.50.0** 首次在 UWP 上通过 NiL.JS 成功执行 d3.js。
- **2026.05.xx — v0.42.8** NiL.JS 运行时移植到 .NET Native 1.4（兼容 W10M 15063）。

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

[m][e] 2026 年 6 月 14 日

![](/Images/footer.png)