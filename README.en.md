# RmlUi for Vintage Story

[中文 README](README.md) · [Platform notes](PLATFORMS.zh-CN.md) · [Validation record](VALIDATION.md)

`VSRmlUi` is a client-side UI foundation for [Vintage Story](https://www.vintagestory.at/). It exposes RmlUi documents through a small C# API, so mods can build windows, modal dialogs, and HUDs with RML and RCSS.

The current release is **1.0.0**, targeting Vintage Story 1.22, .NET 10, and OpenGL 3.3+. Native loading and build scripts support Windows, Linux, and macOS. The repository currently contains Windows and Linux native artifacts; macOS binaries must be built separately.

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
  "vsrmlui": "1.0.0"
}
```

Reference `artifacts/sdk/VSRmlUi.dll` with `Private` set to `false`, and target the same .NET version as the game. A minimal client-side example is:

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

The scripts build the native bridge, managed mod, example, tests, and SDK. Packaging produces `artifacts/vsrmlui_1.0.0.zip` only when all required native libraries are available. Platform-specific CI and bundling details are documented in `PLATFORMS.zh-CN.md`.

## Repository layout

- `src/VSRmlUi/` — managed mod and public API
- `native/` — C++ RmlUi bridge
- `examples/` — example Vintage Story mod
- `tests/` — native, packaging, and integration checks
- `licenses/` — notices and licenses for bundled third-party components

## License and notices

Original VSRmlUi code is released under the [MIT License](LICENSE) © 2026 VSCN-Studio. RmlUi, FreeType, GLAD, Khronos headers, and Noto Sans SC are distributed under their respective licenses; see [`licenses/THIRD-PARTY.md`](licenses/THIRD-PARTY.md) and the files in `licenses/` for required notices.

Vintage Story is a trademark of Anego Studios. This project is an independent mod library and is not affiliated with or endorsed by Anego Studios.

Copyright (c) 2026 VSCN-Studio.
