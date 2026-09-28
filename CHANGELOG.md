# 更新日志

## 1.0.6 (2026-09-28)

- 增加音频分析、加载动画、节点编辑器、旋钮、开关、MIDI/OSC 接口、VR UI 平面、日期选择器和 3D 方向指示器预设及预览。
- 更新滑块、复选框和下拉菜单样式；增加 Tabler 图标与点击反馈。
- 合并经过 ABI smoke 检查的 Windows x64、Linux x64、macOS x64 和 macOS arm64 native 产物。原生 ABI 仍为 2。

## 1.0.5 (2026-09-27)

- 修复游戏未索引 `vsrmlui:icons/` 资源类别，导致内嵌 Tabler SVG 图标报 `Asset not found` 的问题。
- 增加真实游戏资源管理器的图标索引回归检查。
- 修复 RML 翻译 token 使用带占位符的翻译时触发无参数格式化异常的问题。

## 1.0.4 (2026-09-26)

- 更新默认工具 UI 主题、标签页窗口和 Tabler SVG 图标资源。
- 启用 RmlUi SVG 插件并固定 LunaSVG 3.5.0 native 依赖。
- 原生桥接 ABI 保持为 2，但各平台原生库需要重新构建以包含 SVG 支持，不能复用 1.0.3 的二进制。

## 1.0.3 (2026-09-25)

- 修复 Linux/macOS 数字键盘在 Num Lock 开启时可能触发导航键、导致输入框无法正确输入数字或小数点的问题；Num Lock 关闭时仍保留相应导航操作。
- 修复游戏 `FontSettings` 选择系统字体别名（如 `Microsoft YaHei Light`）时，字体解析器可能找不到实际字体的问题。
- 原生桥接 ABI 和上游 RmlUi 修订未变化，继续使用已验证的四平台原生库。

## 1.0.2

- 移除发行包内的 Noto 字体，增加运行时系统字体缺字回退和 TTC face index 支持。
- 更新原生桥接 ABI 至 2，提供 Windows x64、Linux x64、macOS x64 和 macOS arm64 合并包。
