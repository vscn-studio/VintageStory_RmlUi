# Vintage Story RmlUi

[English README](README.en.md) · [更新日志](CHANGELOG.md) · [平台说明](PLATFORMS.zh-CN.md) · [验证记录](VALIDATION.md)

`VSRmlUi` 是 Vintage Story 的客户端 UI 基础库。它通过 C# API 接入 RmlUi，让模组使用 RML 和 RCSS 制作窗口、模态对话框和 HUD。

当前版本为 **1.0.3**，目标环境为 Vintage Story 1.22、.NET 10 和 OpenGL 3.3+。构建流程支持 Windows、Linux、macOS；合并包包含 `win-x64`、`linux-x64`、`osx-x64` 和 `osx-arm64`。官方 Vintage Story Linux 客户端没有 ARM64 发行版，因此不打包 Linux ARM64 native。

## 接入模组

在 `modinfo.json` 中添加依赖：

```json
"dependencies": {
  "vsrmlui": "1.0.3"
}
```

引用 `artifacts/sdk/VSRmlUi.dll`，并将引用的 `Private` 设为 `false`。UI 操作必须在客户端线程执行。

对于 VS Director 等双端模组，客户端和服务器都需要安装 RmlUi 前置包。RmlUi 声明为 `Universal` 且 `requiredOnClient: true`，以便进服时进入客户端缺失模组下载清单；仅声明 `dependencies` 不会让进服下载流程自动补齐此前置。自动下载还需要模组库提供对应版本。UI 运行时只在客户端启动；`requiredOnServer: false` 允许其他纯客户端模组在服务器未安装 RmlUi 时使用它。

版本号和 native 文件独立管理。普通托管代码、测试或文档 commit 可能因为当前 CI 的路径触发规则而运行 native job，但这只是重新执行构建，不代表必须替换 native 文件。只要 `native/`、C ABI、链接依赖、目标架构和固定的上游 RmlUi 修订没有变化，就可以复用已有 native 产物；修改这些内容时才需要重新构建并更新对应平台文件。1.0.2 基于 commit `ddaa4b51b6b80f6b9d7c236e56c99c1d03274f0e`，加入系统字体运行时回退并将 native ABI 升为 2。1.0.3 只改动托管代码，继续使用相同桥接源码和四个平台的原生库。

发行包不再携带 Noto 或其他字体文件。启动时优先读取游戏字体资产；遇到缺字时由 SkiaSharp 在本机字体中查找覆盖该 Unicode 字符的字体，并以内存数据交给 RmlUi。TTC/OTC 字体的 face index 会一并保留。Linux 主机若没有覆盖所需语言的系统字体，会记录一次可操作的警告并显示缺字；安装对应字体（例如 Noto CJK）后重启客户端即可。

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

## 默认主题与工作台布局

在 RML 的 `<head>` 中引用 `vsrmlui:dialog/theme.rcss` 即可使用默认的黑灰与亮蓝工具主题。控件高度统一为 30dp；普通按钮采用直角，图标按钮只在悬停时显示背景。工作台窗口使用 `vs-window vs-workbench`，内部放置 `vs-titlebar` 和 `vs-workbench-body`，主体可用 `vs-sidebar`、`vs-editor`、`vs-inspector` 构成导航、编辑区和属性区。无需状态信息时可以省略 `vs-statusbar`。完整结构见 [`example.rml`](examples/VSRmlUi.Example/assets/vsrmluiexample/dialog/example.rml)。

在窗口元素上添加 `vs-theme-night`、`vs-theme-day` 或 `vs-theme-contrast` 可切换夜间、日间和高对比配色；移除这些类即恢复默认主题。运行时可通过 `page.GetElementById("panel")!.SetClass("vs-theme-night", true)` 切换，内置颜色和文件夹弹窗会继承父页面当前的主题。日间主题采用灰白表面与暖棕强调色。

紧凑工具窗口可使用 `vs-window vs-tabbed-window`，依次放入标题栏、`vs-tabs`、`vs-tab-content` 和 `vs-actions`。标签页仅顶部使用 4dp 圆角，宽度随文字而定；内容切换由模组绑定事件。完整示例见 [`tabbed-tool.rml`](examples/VSRmlUi.Example/assets/vsrmluiexample/dialog/tabbed-tool.rml)。

主题还提供 `vs-primary`、`vs-ghost`、`vs-danger`、`vs-icon-button` 按钮样式，以及 `vs-toolbar`、`vs-segmented`、`vs-list`、`vs-list-item`、`vs-field` 和 `vs-row`。选中项添加 `active` 类。需要颜色、时间、滑块控件时再引用 `controls.rcss`。两种窗口及内置弹窗的各主题预览见 [`PREVIEWS.md`](PREVIEWS.md)。

## 基础控件

`RmlControls.ColorPicker`、`TimePicker`、`Slider` 可生成嵌入表单的 RML。页面应在自身样式之后引用 `vsrmlui:dialog/controls.rcss`，并为 `.rml-picker` 设置适合表单的宽度。

## Tabler SVG 图标

RmlUi 的 SVG 插件已启用，内置 LunaSVG 3.5.0 作为渲染器。Tabler outline 图标以稳定路径提供，例如：

```rml
<svg class="vs-icon" src="vsrmlui:icons/tabler/search.svg" aria-label="搜索" />
<svg class="vs-icon" src="vsrmlui:icons/tabler/settings.svg" aria-label="设置" />
```

图标文件位于 `src/VSRmlUi/assets/vsrmlui/icons/tabler/`，当前包含 `search`、`settings`、`x`、`folder`、`plus`、`check`、`chevron-down`、`download`、`player-play`、`device-floppy`、`arrow-left`、`trash`、`info-circle`、`palette`、`moon`、`sun`、`adjustments-horizontal`、`layout-sidebar` 和 `layout-sidebar-right`。建议用固定的 `.vs-icon` 尺寸，并把图标放在 `vs-icon-button` 中；按钮默认无边框，仅悬停时显示背景。

C# 代码可使用 `RmlIcons.Search`、`RmlIcons.Settings`、`RmlIcons.Close` 等常量拼接 RML，避免散落路径字符串。

SVG 插件需要 native 构建下载固定版本的 LunaSVG。发布包包含对应的许可证和 Tabler MIT 版权说明。

加载文档后绑定选择器：

```csharp
RmlControls.BindColorPicker(page, "fog-color", hex => config.FogColor = hex);
RmlControls.BindTimePicker(page, "day-time", time => SetTime(time), includeSeconds: false);
```

颜色选择器点击色块打开纯 RmlUi 经典布局模态窗口：48 个基本颜色、16 个自定义颜色、可拖拽的色相/饱和度色谱和亮度条、RGB/HSL/HEX 同步输入、透明度滑块及原颜色/新颜色预览。不调用 Windows 原生对话框。“确定”提交草稿；取消或 ESC 放弃，父窗口关闭时子窗口也关闭。自定义颜色保存到客户端 `ModConfig/vsrmlui-colors.json`（添加自定义色板独立于颜色草稿的取消操作）。`BindColorPicker` 的可选 `dialogOpened` 回调可用于宿主管理窗口层级，`allowAlpha: false` 隐藏透明度并限制为 RGB。也可直接调用 `RmlColorDialog.Show(parent, hex, accepted, allowAlpha)`。

时间选择器支持时、分以及可选秒的滑块和文字输入。`TimePicker` 与 `BindTimePicker` 的 `includeSeconds` 参数应一致。时间值是一天内的时刻，不用于多日时长。无效输入不会触发配置更新；监听器随文档销毁释放。`Slider` 是原生 `input[type=range]`，通过元素的 `change` 事件读取 `Value`，支持小数步长。

## 文件夹选择器

`RmlFolderDialog` 提供纯 Rml UI 本机文件夹浏览器，可用于配置回放、视频等输出目录：

```csharp
RmlFolderDialog.Show(page, api, config.OutputDirectory, path =>
{
    config.OutputDirectory = path;
    page.GetElementById("output-directory")!.Value = path;
});
```

支持用户目录、磁盘、上级目录、直接输入地址和子目录列表。目录读取在后台执行，每次最多显示 2000 个子目录；不可访问的目录会显示错误且不能选择。确认返回绝对路径，取消或 Esc 不修改配置；父窗口关闭时浏览器同步关闭。它只选择现有目录，不移动文件或写入调用方配置。

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

构建脚本会生成原生桥接、基础模组、示例和 SDK；所有原生库齐全后会生成 `artifacts/vsrmlui_1.0.3.zip`。如果示例也成功构建，还会生成可单独安装的 `artifacts/vsrmlui-test_1.0.3.zip`。先安装主模组，再安装测试模组；进入客户端后按 **Ctrl+F9**（macOS 使用游戏显示的对应修饰键）打开输入诊断窗口。窗口包含单行/多行文本、数字、下拉框、复选框、滑块和事件日志，可用于检查 IME 提交字符、AltGr、Emoji、粘贴以及编辑快捷键。

## 许可证

本项目原创代码版权归 **VSCN-Studio © 2026**，使用 [MIT 许可证](LICENSE)，项目版权声明见 [COPYRIGHT.txt](COPYRIGHT.txt)。RmlUi、FreeType、GLAD 和 Khronos headers 使用各自许可证，详见 [`licenses/`](licenses/) 和 [`licenses/THIRD-PARTY.md`](licenses/THIRD-PARTY.md)。发行包包含版权声明、MIT 许可证、第三方归属说明及完整第三方许可文本。

Vintage Story 是 Anego Studios 的商标。本项目是独立的模组库，与 Anego Studios 无隶属或背书关系。
