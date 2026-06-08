# MediaExplorer 0.55.10 - dev branch


![](/Images/logo.png)

## 这是什么？

MediaExplorer 是一个为 Windows 10 Mobile（W10M，版本 15063+）开发的业余浏览器项目，**不使用**系统 WebView 或 Chakra 引擎。它使用自定义 HTML 解析器、支持选择器/层叠/flexbox 的 CSS 引擎、基于 [NiL.JS](https://github.com/nilproject/NiL.JS) 的 JavaScript 运行时，以及基于 XAML 的渲染器。

约 35 个文件，约 2 万行代码。Pre-alpha。博物馆级别作品。

## 截图

![](/Images/sshot01.png)
![](/Images/sshot02.png)
![](/Images/sshot03.png)


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

## 状态

- **Pre-alpha。** 所有功能均高度未完成。
- **NiL.JS 2.6集成(netstandard2.0 → 1.4)** | **✅ 完成**(w10m15063完全构建)

这是一个自制的浏览器引擎——不是生产级产品，也不打算替代 Edge 或 Chrome。它的存在是为了证明：渲染网页不一定需要 Chromium。

## 开发里程碑

- **2026.06.07 — v0.55.0** D3.js 力导向图（诺基亚设计档案馆）渲染为实时 XAML 元素 — 无需 Skia、SvgImageSource 或 WebView。SVG 元素（circle、line、rect、path、text、g）映射为原生 UWP UIElement（Ellipse、Line、Rectangle、Path、TextBlock、Canvas），在 VirtualizingRenderer 管线中实现样式级联（fill、stroke、stroke-width、opacity）和 viewBox 缩放。
- **2026.06.05 — v0.50.0** 首次在 UWP 上通过 NiL.JS 成功执行 d3.js：力模拟初始化，tick 函数触发，DOM SVG 节点创建。限制：此时 D3.js DOM 输出尚未渲染到屏幕（v0.55 已解决）。
- **2026.05.xx — v0.42.8** NiL.JS 运行时从 netstandard2.0 移植到 .NET Native 1.4（兼容 W10M 15063）。添加 ES Modules 解析支持。实现 MutationObserver。

## 已知问题

- 源代码由 AI 生成（"AI 混合体"），除原始 UDAIE-A WebView 代码外
- 未在任何 W10M 设备上测试
- 部分网站白屏（dzen.ru、ya.ru）
- ES Modules 运行时错误仍在修复中
- 不支持私有字段（`#name`）

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

[m][e] 2026 年 6 月 08 日

![](/Images/footer.png)
