# 更新日志

## 1.0.3 (2026-09-25)

- 修复 Linux/macOS 数字键盘在 Num Lock 开启时可能触发导航键、导致输入框无法正确输入数字或小数点的问题；Num Lock 关闭时仍保留相应导航操作。
- 修复游戏 `FontSettings` 选择系统字体别名（如 `Microsoft YaHei Light`）时，字体解析器可能找不到实际字体的问题。
- 原生桥接 ABI 和上游 RmlUi 修订未变化，继续使用已验证的四平台原生库。

## 1.0.2

- 移除发行包内的 Noto 字体，增加运行时系统字体缺字回退和 TTC face index 支持。
- 更新原生桥接 ABI 至 2，提供 Windows x64、Linux x64、macOS x64 和 macOS arm64 合并包。
