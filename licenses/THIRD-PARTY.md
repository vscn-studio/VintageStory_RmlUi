# Third-party components

The compact tool theme references Dear ImGui's visual conventions (flat controls,
compact form rows and grouped settings). The RML/RCSS implementation is original;
no third-party extension code or screenshot assets are included. Dear ImGui:
Copyright (c) 2014-2026 Omar Cornut, MIT. See `Dear-ImGui-MIT.txt`.
Reference: https://github.com/ocornut/imgui/wiki/Useful-Extensions.

- **LunaSVG 3.5.0**, MIT, Copyright (c) 2020–2025 Samuel Ugochukwu.
  Used by the enabled RmlUi SVG plugin to rasterize bundled SVG icons.
  Source: https://github.com/sammycage/lunasvg/releases/tag/v3.5.0.
  See `LunaSVG-MIT.txt`.
- **Tabler Icons**, MIT, Copyright (c) 2018–2026 The Tabler Authors.
  The bundled outline SVG subset is stored under `src/VSRmlUi/assets/vsrmlui/icons/tabler/`.
  Source: https://github.com/tabler/tabler-icons.
  See `Tabler-MIT.txt`.

- **RmlUi**, MIT, Copyright (c) 2008–2014 CodePoint Ltd, Shift Technology Ltd and contributors; Copyright (c) 2019–2026 The RmlUi Team and contributors.
  Source: https://github.com/mikke89/RmlUi at `3045e6e3510425ef2870f7647b3f59d3ae9970f5`.
  See `RmlUi-MIT.txt`. Local build copies adapt the GL3 renderer to composite into the host framebuffer instead of framebuffer 0, format select values within the dropdown content area, and resolve system fonts when glyphs are missing. The upstream checkout is not modified.
- **FreeType 2.13.3**, used under the FreeType License (FTL).
  Source: https://github.com/freetype/freetype/tree/VER-2-13-3.
  This software uses FreeType, Copyright (c) 1996–2024 The FreeType Project (www.freetype.org). All rights reserved.
  See `FreeType-FTL.txt` and `FreeType-LICENSE.txt`.
- **GLAD 2.0.0-beta generated OpenGL loader**, bundled by the RmlUi GL3 backend.
  See `GLAD-LICENSE.txt`, `Khronos-Headers.txt` and `Apache-2.0.txt` for generator and Khronos specification notices.

Vintage Story, its font files, SkiaSharp and OpenTK are supplied by the user's game installation and are not redistributed in the mod packages. System fonts are read from the client machine at runtime; no font data is distributed with VSRmlUi.
