# RmlUi for Vintage Story

[中文 README](README.md) · [Changelog](CHANGELOG.md) · [Platform notes](PLATFORMS.zh-CN.md) · [Validation record](VALIDATION.md)

`VSRmlUi` is a client-side UI foundation for [Vintage Story](https://www.vintagestory.at/). It exposes RmlUi documents through a small C# API, so mods can build windows, modal dialogs, and HUDs with RML and RCSS.

The current release is **1.0.3**, targeting Vintage Story 1.22, .NET 10, and OpenGL 3.3+. Native loading and build scripts support Windows, Linux, and macOS. The merged package contains `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64`; the official Vintage Story Linux client has no ARM64 distribution, so Linux ARM64 native is not packaged.

## Features

- Load RML from mod assets or strings and manage documents independently.
- Window, modal, and HUD display modes with focus, mouse, keyboard, and Escape handling.
- CSS-like styling through RCSS, localization through Vintage Story language keys, and shared font/image caches.
- UTF-8 and Unicode input, including clipboard operations provided by the game.
- Native RmlUi integration with managed lifetime and stale-handle checks.

## Using VSRmlUi in a mod

Add the dependency to `modinfo.json`:

```json
"dependencies": {
  "vsrmlui": "1.0.3"
}
```

Reference `artifacts/sdk/VSRmlUi.dll` with `Private` set to `false`, and target the same .NET version as the game.

For universal mods such as VS Director, install the RmlUi dependency on both the client and server. RmlUi declares `Universal` and `requiredOnClient: true` so it appears in the client's missing-mod download list when joining a server; declaring `dependencies` alone does not add it to that download list. Automatic downloads also require the matching version to be available on the mod database. The UI runtime starts only on the client; `requiredOnServer: false` lets other client-only mods use it without a server installation.

The managed release version and native artifacts are tracked independently. A commit that changes only managed code, tests, or documentation may still run the native job because of the current CI path filters, but it does not require new native files. Existing native artifacts remain valid while `native/`, the C ABI, linked dependencies, target architectures, and the pinned upstream RmlUi revision are unchanged. Rebuild native artifacts when any of those inputs change. Release 1.0.2 is based on commit `ddaa4b51b6b80f6b9d7c236e56c99c1d03274f0e`; it removes the bundled Noto font, adds runtime system-font fallback and TTC face-index support, and raises the native ABI to 2. Release 1.0.3 changes only managed code and reuses the same bridge source and four platform native libraries.

The package contains no font files. Startup first uses the game's font assets; when a glyph is missing, SkiaSharp finds a local system font and passes its data to RmlUi in memory. Linux installations without a font covering the requested language log one actionable warning and render the glyph as missing until the client is restarted after installing a suitable font such as Noto CJK.

A minimal client-side example is:

```csharp
using VSRmlUi;

var ui = api.ModLoader.GetModSystem<RmlUiModSystem>().Service;
if (ui is null) return;

using var page = ui.LoadDocument("mymod", "mymod:dialog/settings.rml");
page.GetElementById("save")!.On("click", _ =>
{
    var name = page.GetElementById("name")!.Value;
    page.GetElementById("status")!.Text = $"Hello, {name}";
});
page.Show();
```

All UI operations must run on the client thread. Use `api.Event.EnqueueMainThreadTask` when a background task needs to update a document. See the Chinese README and `examples/` for the complete API and resource layout.

## Shared Theme

Link `vsrmlui:dialog/theme.rcss` for the compact black and bright-blue tool theme. A `vs-window vs-workbench` contains a `vs-titlebar` and `vs-workbench-body`; use `vs-sidebar`, `vs-editor`, and `vs-inspector` inside the body. Add `vs-statusbar` only when status information is needed. The [workbench RML](examples/VSRmlUi.Example/assets/vsrmluiexample/dialog/example.rml) shows the full layout. A separate [tabbed tool RML](examples/VSRmlUi.Example/assets/vsrmluiexample/dialog/tabbed-tool.rml) shows terminal-style top tabs, content, and an action row.

## Tabler SVG Icons

The RmlUi SVG plugin is enabled and uses pinned LunaSVG 3.5.0. Tabler outline icons are available at stable paths:

```rml
<svg class="vs-icon" src="vsrmlui:icons/tabler/search.svg" aria-label="Search" />
<svg class="vs-icon" src="vsrmlui:icons/tabler/settings.svg" aria-label="Settings" />
```

The bundled subset lives in `src/VSRmlUi/assets/vsrmlui/icons/tabler/` and includes search, settings, window controls, folders, editing, playback, download, palette and layout icons. Use a fixed `.vs-icon` size and place icons in `vs-icon-button`; icon buttons have no resting border and show a surface only on hover. License notices for LunaSVG and Tabler are included with the project.

C# code can use stable constants such as `RmlIcons.Search`, `RmlIcons.Settings`, and `RmlIcons.Close` when composing RML instead of repeating path strings.

Add `vs-theme-night`, `vs-theme-day`, or `vs-theme-contrast` to the window element for alternate lighting and contrast. The day theme uses gray-white surfaces and a warm brown accent. Built-in color and folder dialogs inherit the parent page's current variant. The theme also provides button variants (`vs-primary`, `vs-ghost`, `vs-danger`, `vs-icon-button`), tabs, segmented controls, toolbars, lists, and form rows. Add `active` to the selected navigation or tab item and bind the switching behavior in your mod. Link `controls.rcss` as well for the reusable color, time, and slider controls. See [preview instructions](PREVIEWS.md) for the rendered theme matrix.

`RmlControls.ColorPicker` and `BindColorPicker` provide an inline swatch and hex field. Clicking the swatch opens a classic dark color dialog built entirely in RmlUi: 48 basic colors, 16 custom slots, draggable hue/saturation spectrum and luminance strip, synchronized RGB/HSL/HEX fields, alpha slider, and original/new previews. OK commits the draft; Cancel or Escape discards it. Custom slots are saved separately in `ModConfig/vsrmlui-colors.json`. Pass `allowAlpha: false` for RGB-only fields, or use `RmlColorDialog.Show(parent, hex, accepted, allowAlpha)` directly. The optional `dialogOpened` callback exposes the child document for host window-stack integration. No Windows native dialog is invoked.

## Folder browser

`RmlFolderDialog.Show(parent, api, initialPath, path => config.OutputDirectory = path)` opens a local folder picker built with Rml UI. It supports drives, the user directory, parent navigation, typed addresses, and subfolders. Directory enumeration runs in the background and displays at most 2,000 entries per folder. Unreadable directories cannot be selected. Confirmation returns an absolute path; cancellation or Escape leaves the caller's value unchanged. Closing the parent also closes the picker. The picker selects existing folders and does not move files or persist configuration.

## Building

Requirements: Python 3.10+, .NET 10 SDK, CMake 3.24+, and a native toolchain. The build expects a local Vintage Story installation and a checkout of the pinned RmlUi source revision.

Windows:

```powershell
./build.ps1 -GameDirectory E:/vintagestory/Vintagestory -RmlUiSource E:/vintagestory/RmlUi
```

Linux or macOS:

```sh
python3 build.py --game-directory /path/to/game --rmlui-source /path/to/RmlUi
```

The build creates `artifacts/vsrmlui_1.0.3.zip` after all native libraries are available. A successful example build also creates `artifacts/vsrmlui-test_1.0.3.zip`. Install the main mod and then the test mod; press **Ctrl+F9** (use the modifier shown by the game on macOS) in the client to open the input diagnostics window. It includes single-line and multiline text, number/select/checkbox/range controls, and an event log for IME committed text, AltGr, emoji, paste, and editing shortcuts.

The scripts build the native bridge, managed mod, example, tests, and SDK. Packaging produces `artifacts/vsrmlui_1.0.3.zip` only when all required native libraries are available. Platform-specific CI and bundling details are documented in `PLATFORMS.zh-CN.md`.

## Repository layout

- `src/VSRmlUi/` — managed mod and public API
- `native/` — C++ RmlUi bridge
- `examples/` — example Vintage Story mod
- `tests/` — native, packaging, and integration checks
- `licenses/` — notices and licenses for bundled third-party components

## License and notices

The release packages include [COPYRIGHT.txt](COPYRIGHT.txt), the project MIT license, and the complete `licenses/` directory, including third-party attribution and license texts.

Original VSRmlUi code is released under the [MIT License](LICENSE) © 2026 VSCN-Studio. RmlUi, FreeType, GLAD, and Khronos headers are distributed under their respective licenses; see [`licenses/THIRD-PARTY.md`](licenses/THIRD-PARTY.md) and the files in `licenses/` for required notices.

Vintage Story is a trademark of Anego Studios. This project is an independent mod library and is not affiliated with or endorsed by Anego Studios.

Copyright (c) 2026 VSCN-Studio.
