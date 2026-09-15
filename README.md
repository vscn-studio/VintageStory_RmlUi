# Vintage Story RmlUi

[English README](README.en.md) · [平台说明](PLATFORMS.zh-CN.md) · [验证记录](VALIDATION.md)

`VSRmlUi` 是 Vintage Story 的客户端 UI 基础库。它通过 C# API 接入 RmlUi，让模组使用 RML 和 RCSS 制作窗口、模态对话框和 HUD。

当前版本为 **1.0.0**，目标环境为 Vintage Story 1.22、.NET 10 和 OpenGL 3.3+。支持 Windows、Linux、macOS 的构建流程；仓库当前提供 Windows/Linux 原生库，macOS 需要自行构建。

## 接入模组

在 `modinfo.json` 中添加依赖：

```json
"dependencies": {
  "vsrmlui": "1.0.0"
}
```

引用 `artifacts/sdk/VSRmlUi.dll`，并将引用的 `Private` 设为 `false`。UI 操作必须在客户端线程执行。

```csharp
using VSRmlUi;

var ui = api.ModLoader.GetModSystem<RmlUiModSystem>().Service;
if (ui is null) return;

using var page = ui.LoadDocument("mymod", "mymod:dialog/settings.rml");
page.GetElementById("save")!.On("click", _ =>
{
    var name = page.GetElementById("name")!.Value;
    page.GetElementById("status")!.Text = $"你好，{name}";
});
page.Show();
```

完整示例见 [`examples/`](examples/)，API 定义见 [`src/VSRmlUi/`](src/VSRmlUi/)。

## 构建

需要 Python 3.10+、.NET 10 SDK、CMake 3.24+ 和对应平台的 C/C++ 工具链，同时准备 Vintage Story 安装目录和指定版本的 RmlUi 源码。

Windows：

```powershell
./build.ps1 -GameDirectory E:/vintagestory/Vintagestory -RmlUiSource E:/vintagestory/RmlUi
```

Linux/macOS：

```sh
python3 build.py --game-directory /path/to/game --rmlui-source /path/to/RmlUi
```

构建脚本会生成原生桥接、基础模组、示例和 SDK；所有原生库齐全后才会生成 `artifacts/vsrmlui_1.0.0.zip`。

## 许可证

本项目原创代码版权归 **VSCN-Studio © 2026**，使用 [MIT 许可证](LICENSE)。RmlUi、FreeType、GLAD、Khronos headers 和 Noto Sans SC 使用各自许可证，详见 [`licenses/`](licenses/) 和 [`licenses/THIRD-PARTY.md`](licenses/THIRD-PARTY.md)。

Vintage Story 是 Anego Studios 的商标。本项目是独立的模组库，与 Anego Studios 无隶属或背书关系。
