// Copyright (c) 2026 VSCN-Studio
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace VSRmlUi;

internal static class KeyboardLocks
{
    // Vintage Story KeyEvent exposes held modifiers, but not lock toggles.
    // Read the OS toggle state for every event so changes outside the game,
    // or before opening the document, are reflected immediately.
    internal static int ReadModifiers() => OperatingSystem.IsWindows() ? ReadWindowsModifiers(GetKeyState) : 0;

    internal static int ReadWindowsModifiers(Func<int, short> getKeyState)
        => ((getKeyState(0x14) & 1) != 0 ? 16 : 0)
            | ((getKeyState(0x90) & 1) != 0 ? 32 : 0)
            | ((getKeyState(0x91) & 1) != 0 ? 64 : 0);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern short GetKeyState(int virtualKey);
}
