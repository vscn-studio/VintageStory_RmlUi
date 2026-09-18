# 1.0.0 平台支持与合并包

首版原生桥接按游戏**进程**架构加载：`win-x64`、`linux-x64`、`osx-x64` 和 `osx-arm64`。官方 Vintage Story Linux 客户端没有 ARM64 发行版，因此不提供 `linux-arm64`。Linux 使用系统 OpenGL/GLX loader；macOS 使用系统 OpenGL.framework，最低部署版本为 11.0。macOS 的 x64 进程（包括 Rosetta 下运行的游戏）加载 `osx-x64`，原生 arm64 游戏加载 `osx-arm64`。

`build.py --native-only` 不需要 Vintage Story 文件，会构建原生桥接并运行 C ABI、UTF-8、事件、句柄和三次生命周期 smoke test。Windows PowerShell 用户可用 `./build.ps1 -NativeOnly`；Linux/macOS 直接使用 `python3 build.py --native-only`。完整托管测试仍需要对应平台的游戏 API、SkiaSharp 和 GLFW 文件：通过 `--game-directory` 与 `--game-native-directory` 指定。

每个平台分别编译原生库作为构建中间产物，发布时输出主模组 `vsrmlui_1.0.0.zip`；示例构建成功时再输出输入诊断模组 `vsrmlui-test_1.0.0.zip`。默认收集 `artifacts/native`，也可用 `--bundle-native` 指定收集目录。本次必须包含 `win-x64` 和 `linux-x64`；其他平台如已构建验证会一并加入。缺少 Windows/Linux 时拒绝生成 ZIP。每个 native 目录的 manifest 记录 RmlUi 提交、桥接源码指纹和二进制 SHA-256，防止混用不同桥接版本。macOS 构建使用 ad-hoc codesign；是否需要额外开发者签名和公证，需按实际游戏加载方式及发行渠道验收。

GitHub Actions 的 `native.yml` 配置了 Windows x64、Linux x64、macOS x64 和 macOS arm64 原生构建和上传任务，同时报告 `ldd`/`otool` 依赖。提交 `2ea9ff7` 的运行记录中四个 native job 均成功；这些任务只验证 native ABI，不等于对应平台已完成真实游戏验收。

## 构建命令

```sh
# Linux/macOS：准备本机 C++ 编译器、CMake 3.24+、Ninja 和 Python 3.10+
python3 build.py --native-only --rmlui-source ../RmlUi

# 完整模组与示例；game 目录直接包含 VintagestoryAPI.dll
python3 build.py --prepare-only --game-directory /path/to/game --rmlui-source ../RmlUi

# Linux 无显示器，使用 Xvfb 实际执行 OpenGL 测试（需要 Mesa/GLFW 依赖）
xvfb-run -a python3 build.py --prepare-only --game-directory /path/to/game

# 收齐各平台 artifacts/native 目录后，仅合包，无需再次编译或运行测试
python3 build.py --package-only --bundle-native /path/to/collected-native
```

PowerShell 等价参数为 `-GameDirectory`、`-RmlUiSource`、`-GameNativeDirectory`、`-BundleNative`、`-NativeOnly`、`-PrepareOnly`、`-PackageOnly`、`-HeadlessTests`、`-SkipTests`。
`--headless-tests` 只跳过 OpenGL 测试，保留托管和原生测试；`--skip-tests` 不代表通过验证。SDK 与测试都应运行在目标架构上，脚本会拒绝将其他架构的构建误标为目标 RID。

FreeType 和 RmlUi 静态链接，不随模组复制游戏的 GLFW 或 SkiaSharp。测试项目从指定的游戏目录复制这两者的目标平台原生依赖。macOS 游戏若将原生依赖置于 `.app/Contents/Frameworks`，应额外传入 `--game-native-directory`；`--game-directory` 仍指向托管 DLL 所在目录。`DYLD_LIBRARY_PATH`/`LD_LIBRARY_PATH` 可用于游戏依赖自己的二级库，但不应指向其他架构文件。

## 发布状态与边界

- Windows x64：本机 native smoke 和 123 项完整托管/集成/OpenGL 检查通过，包括 NVIDIA OpenGL 实际渲染。
- Linux x64：本机 Ubuntu 24.04 / WSL native smoke 和此前 121 项完整托管/集成/OpenGL 检查通过，使用 Xvfb + Mesa llvmpipe OpenGL 4.5；新输入诊断文档的 Linux 重跑尚未完成。没有验证 Linux 硬件显卡驱动或真实游戏世界。
- Linux ARM64：官方客户端没有对应发行版，不在 CI 矩阵和合并包中。
- macOS x64/arm64：CI 构建、架构检查、依赖检查、ad-hoc 签名和 native smoke 通过；没有在物理 Mac 上运行托管、OpenGL 或真实游戏验收。
- 全部平台仍需真实 Vintage Story 世界内验收；测试用的 `GuiDialog` 由 API 替身驱动。

本地 Linux 二进制使用 GCC 13/Ubuntu 24.04，引用最高 `GLIBC_2.38`，不能当成兼容所有发行版的包。CI 的 x64 任务选择 Ubuntu 22.04 以降低发行版基线；其实际最低 glibc/libstdc++ 版本须由构建后的依赖报告确认。Windows 需要游戏环境的 Visual C++ runtime。macOS 11.0 是原生库的部署目标，不代表游戏/.NET 10 支持所有 macOS 11 机器。

Linux 渲染器使用 `libGL.so.1` / `glXGetProcAddressARB`；原生 Wayland/EGL loader 尚未实现，XWayland/GLX 以实际游戏上下文为准。macOS 测试窗口请求 forward-compatible OpenGL 4.1 Core；Retina 的帧缓冲/鼠标坐标一致性仍需游戏内验收。Command+A/C/X/V 会映射到 RmlUi 编辑修饰键；当前 RmlUi 文本控件没有 Undo/Redo 历史，因此 Command/Ctrl+Z/Y 尚未提供撤销/重做，也不提供 Director 的编辑历史。当前 `KeyChar` 提交文本会保留带 Ctrl/Alt 的 Unicode 字符，已覆盖上次的 Linux IME/AltGr 丢字问题；IME composition、候选框定位、物理 Mac 输入事件和完整 macOS 文本导航仍未实现。

## 当前交付状态

2026-09-18 文件夹浏览器更新：本次本地合并包使用已重新构建验证的 Windows/Linux x64 原生库。此前 macOS 产物的源码指纹不匹配，未混入新包；下方四平台合包记录属于此前构建。

版本已固定为 1.0.0。当前本地合并包包含四个 native RID：Windows/Linux x64、macOS x64 和 macOS arm64。构建示例后会额外产生 `vsrmlui-test_1.0.0.zip`，其中的 F9 输入诊断窗口用于真实游戏内检查 IME、键盘修饰键、文本控件和鼠标操作；它依赖主模组包。旧版本 ZIP（包括旧的双平台 multi 包及示例包）移至 `build/previous-packages/`，不再放在当前发布目录中。

CI 默认执行四平台原生构建，这些是合包输入而不是可安装的模组分包。完整合包任务需要一台配置好 Python、.NET 10、MSVC/CMake、游戏目录的 Windows self-hosted runner，标签为 `vsrmlui-packaging`，环境变量 `VS_GAME_DIRECTORY` 指向游戏安装目录。手动运行工作流并启用 `package_release`，它会等待四平台 native 任务通过，下载全部产物、构建托管 DLL 和示例，并输出 `vsrmlui_1.0.0.zip` 与 `vsrmlui-test_1.0.0.zip`。未配置该 runner 时，先运行 native 任务，再把产物下载到本机 `artifacts/native`，用 `-PackageOnly` 合包。
