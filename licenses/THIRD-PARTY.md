# Third-party components

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
