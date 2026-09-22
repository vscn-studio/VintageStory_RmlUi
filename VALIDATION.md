# 1.0.2 release validation notes — 2026-09-22

Release 1.0.2 includes runtime system-font fallback. Commit `ddaa4b51b6b80f6b9d7c236e56c99c1d03274f0e` removes the bundled Noto font, adds TTC face-index support, patches the RmlUi missing-glyph path, and raises the native ABI to 2. All four platform native libraries must therefore be rebuilt from this revision.

The current native bundle contains Windows x64, Linux x64, macOS x64, and macOS arm64. Linux x64 was rebuilt under WSL Ubuntu 24.04, Windows x64 was rebuilt with MSVC, and both passed the native C ABI smoke test. The two macOS libraries came from the successful GitHub Actions run for commit `2ea9ff7`; their architecture, dependency, signing, and native smoke steps passed on the target runners. Linux ARM64 is excluded because the official Vintage Story Linux client has no ARM64 distribution.

The managed build was refreshed after the committed-text input fix. The headless integration run passed **111** checks, and the full Windows run passed **123** checks, including the input-diagnostics document, committed IME text with Ctrl, AltGr text, and macOS Command+A mapping. This verifies the event adapter with API doubles; it does not verify physical IME composition or candidate windows on Linux/macOS.

Windows x64 passed the C ABI smoke check and all **123** managed/integration/OpenGL checks in the current rebuild. The recorded Linux x64 run passed the previous **121** checks; its native smoke and GLX/Xvfb results remain valid, while the new diagnostics-document checks have not been rerun under WSL.

| Host | Managed/game | Native | Graphics |
|---|---|---|---|
| Windows x64 | .NET SDK 10.0.301, VS API 1.22.3.0 | MSVC 19.51 | NVIDIA RTX 4070 Laptop, OpenGL 3.3 Core |
| Ubuntu 24.04 x64 under WSL | .NET SDK 10.0.401, VS API 1.22.7.0 | GCC 13.3 | Xvfb, Mesa 25.2.8 llvmpipe, OpenGL 4.5 Core |

New checks cover the four supported OS/process-architecture mappings, rejection of unsupported platforms, AltGr committed text, the input-diagnostics document, and Command+A selection through the real native text widget (platform mapping simulated on each test host). Existing framebuffer, Chinese glyph, premultiplied image, GL state, lifecycle, callback and GuiDialog adapter checks continue to pass. macOS Command behavior has **not** been tested on physical macOS input events.

Linux uses the game's shipped `libglfw.so.3` and `libSkiaSharp.so`. The local binary's highest required symbol versions are `GLIBC_2.38` and `GLIBCXX_3.4.29`; all dynamic dependencies resolved in Ubuntu 24.04. Mesa emitted device-probing EGL/Zink warnings before selecting llvmpipe; the GL checks then passed without OpenGL errors or RmlUi parser/font/resource warnings. This is a software GLX test, not native Wayland/EGL or hardware Linux driver validation.

Portable build, native artifact manifests, local platform ZIPs, and a four-RID combined ZIP are provided. The four-platform GitHub Actions native workflow completed successfully for commit `2ea9ff7`. macOS deployment target, ad-hoc signing, process-architecture loading, and forward-compatible test context passed their CI/native steps but remain unverified inside a real Mac game process.

Full test logs: `build/windows-tests.log` and `build/linux-full.log`. Neither run launched a real Vintage Story world. Remaining acceptance work is real archive loading, original GUI/ImGui coexistence, physical OS IME composition and candidate-window positioning, macOS Command/input behavior, Retina/physical DPI and window modes, Linux hardware drivers and native Wayland/EGL, text undo/redo history, and extended world transitions. Server-side UI remains disabled. See [platform details](PLATFORMS.zh-CN.md) and [Director gaps](DIRECTOR_MIGRATION_GAPS.zh-CN.md).

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

