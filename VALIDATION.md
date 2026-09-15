# 1.0.0 release validation — 2026-09-14

Both Windows x64 and Linux x64 passed the C ABI smoke check and all **89** managed/integration/OpenGL checks.

| Host | Managed/game | Native | Graphics |
|---|---|---|---|
| Windows x64 | .NET SDK 10.0.301, VS API 1.22.3.0 | MSVC 19.51 | NVIDIA RTX 4070 Laptop, OpenGL 3.3 Core |
| Ubuntu 24.04 x64 under WSL | .NET SDK 10.0.401, VS API 1.22.7.0 | GCC 13.3 | Xvfb, Mesa 25.2.8 llvmpipe, OpenGL 4.5 Core |

New checks cover the five supported OS/process-architecture mappings, rejection of unsupported platforms, AltGr committed text, and Command+A selection through the real native text widget (platform mapping simulated on each test host). Existing framebuffer, Chinese glyph, premultiplied image, GL state, lifecycle, callback and GuiDialog adapter checks continue to pass. macOS Command behavior has **not** been tested on physical macOS input events.

Linux uses the game's shipped `libglfw.so.3` and `libSkiaSharp.so`. The local binary's highest required symbol versions are `GLIBC_2.38` and `GLIBCXX_3.4.29`; all dynamic dependencies resolved in Ubuntu 24.04. Mesa emitted device-probing EGL/Zink warnings before selecting llvmpipe; the GL checks then passed without OpenGL errors or RmlUi parser/font/resource warnings. This is a software GLX test, not native Wayland/EGL or hardware Linux driver validation.

Portable build, native artifact manifests, local platform ZIPs, and a combined Windows/Linux ZIP are provided. The five-platform GitHub Actions workflow is prepared but was not submitted or run. Linux arm64 and macOS x64/arm64 binaries are **not included** in the local packages. macOS deployment target, ad-hoc signing, process-architecture loading, and forward-compatible test context are configured but remain unverified on a Mac.

Full test logs: `build/windows-tests.log` and `build/linux-full.log`. Neither run launched a real Vintage Story world. Mod archive loading in the game, original menus and Director coexistence, IME, Retina/physical DPI and extended world transitions remain acceptance work. See [platform details](PLATFORMS.zh-CN.md) and [Director gaps](DIRECTOR_MIGRATION_GAPS.zh-CN.md).

## Historical 0.1.0 baseline

`./build.ps1` completed successfully with 79 automated checks passing.

Environment: Windows x64, .NET SDK 10.0.301, Vintage Story API 1.22.3.0,
MSVC 19.51, RmlUi 3045e6e, FreeType 2.13.3.
The graphics checks used a hidden OpenGL 3.3 core window on an NVIDIA GeForce RTX 4070 Laptop GPU, driver 576.28.

Verified:

- Managed and native Release builds, with no C# warnings or errors.
- Three native startup/shutdown cycles, document ownership, safe invalid handles, UTF-8 text, input, class/attribute changes and cross-thread rejection.
- Managed event dispatch/unsubscription, document disposal from callbacks, callback cleanup after element destruction.
- Real `GuiDialog` adapter exercised with test doubles: cursor/focus ownership, HUD input passthrough, modal capture, both Escape paths and dialog unregistration.
- Actual OpenGL geometry, Chinese glyphs, relative image loading, premultiplied alpha, GUI scale changes, host framebuffer and binding/state preservation during initialization, rendering and disposal.
- No native parser/font/resource warnings in the validation run.

`artifacts/example-preview.png` is the actual rendered example, not a mockup.

Not yet verified in a running Vintage Story world: native library loading through the game's mod archive loader, original GUI/ImGui coexistence, operating-system IME composition, physical DPI/window-mode combinations and extended play across world changes. The API adapter checks use test doubles and are not a substitute for that in-game acceptance pass.

The 0.1.0 package included only the Windows x64 native binary. Server-side UI initialization remains disabled.

