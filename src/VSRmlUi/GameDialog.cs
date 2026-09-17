// Copyright (c) 2026 VSCN-Studio
// SPDX-License-Identifier: MIT

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VSRmlUi;

internal sealed class GameDialog : GuiDialog, IDocumentView
{
    private readonly RmlDocument document;
    private readonly GameHost host;
    private readonly Func<int> lockModifiers;
    private readonly HashSet<int> pressedKeys = [];
    private readonly HashSet<int> pressedButtons = [];
    private char? highSurrogate;
    private bool disposed;
    private bool closing;
    internal GameDialog(ICoreClientAPI api, GameHost host, RmlDocument document) : this(api, host, document, KeyboardLocks.ReadModifiers) { }
    internal GameDialog(ICoreClientAPI api, GameHost host, RmlDocument document, Func<int> lockModifiers) : base(api)
    { this.document = document; this.host = host; this.lockModifiers = lockModifiers; document.Runtime.AttachHost(this, document); }
    // A visible, input-disabled document is a passive HUD. Merely returning
    // false from input handlers leaves it counted in ClientMain.DialogsOpened,
    // which prevents mouse capture outside immersive mouse mode.
    private bool Interactive => document.Options.Mode != RmlWindowMode.Hud
        && (document.Options.Input.ReceiveMouse || document.Options.Input.ReceiveKeyboard);
    private bool Modal => Interactive && document.Options.Mode == RmlWindowMode.Modal;
    public override string ToggleKeyCombinationCode => null!;
    public override string DebugName => $"RmlUi/{document.OwnerModId}/{document.SourcePath}";
    public override double DrawOrder => document.Options.DrawOrder;
    public override double InputOrder => document.Options.InputOrder;
    public override EnumDialogType DialogType => Interactive ? EnumDialogType.Dialog : EnumDialogType.HUD;
    public override bool Focusable => Interactive;
    public override bool PrefersUngrabbedMouse => Interactive && document.Options.UnlockMouse && document.Options.Input.UnlockMouse;
    public override bool DisableMouseGrab => Modal;
    public override bool CaptureRawMouse() => Interactive && document.Options.Input.ReceiveMouse;
    public override bool CaptureAllInputs() => !disposed && opened && focused && Interactive && (document.Options.Input.ReceiveKeyboard || document.Options.Input.ReceiveMouse);
    public override bool OnEscapePressed()
    {
        if (!Interactive) return false;
        if (document.Filter(new(RmlInputKind.KeyDown, Key: (int)GlKeys.Escape))) return true;
        return document.Options.CloseOnEscape && TryClose();
    }
    public override bool ShouldReceiveRenderEvents() => !disposed && opened && document.IsVisible && document.DrawTarget == RmlRenderScope.Current;
    public override bool ShouldReceiveMouseEvents() => !disposed && opened && Interactive && document.Options.Input.ReceiveMouse;
    public override bool ShouldReceiveKeyboardEvents() => !disposed && opened && focused && Interactive && document.Options.Input.ReceiveKeyboard;
    public bool Open() => TryOpen(Interactive && document.Options.FocusOnOpen);
    internal void RestoreFocus() { if (!disposed && opened) capi.Gui.RequestFocus(this); }
    internal bool PrimaryButtonDown => capi.Input.MouseButton.Left;
    public void Close() => TryClose();
    public override bool TryClose()
    {
        if (closing) return true;
        closing = true;
        try
        {
            UnFocus();
            bool result = base.TryClose();
            if (!document.IsDisposed && document.IsVisible) document.Close();
            return result;
        }
        finally { closing = false; }
    }
    public override void UnFocus()
    {
        if (!document.IsDisposed && (focused || pressedKeys.Count != 0 || pressedButtons.Count != 0))
        {
            foreach (int key in pressedKeys) document.Call(10, key);
            foreach (int button in pressedButtons) document.Call(7, button);
            document.Call(15);
        }
        document.CancelInput();
        pressedKeys.Clear(); pressedButtons.Clear(); highSurrogate = null;
        base.UnFocus();
    }
    public override void OnRenderGUI(float deltaTime)
    {
        if (!ShouldReceiveRenderEvents()) return;
        try
        {
            var size = document.Runtime.Dimensions();
            document.UpdateViewport();

            document.Runtime.DrainEvents();
            if (document.IsDisposed || !document.IsVisible) return;
            document.Call(4);
            MouseOverCursor = Interactive && document.Call(14) != 0 ? host.CurrentCursor : null;
        }
        catch (Exception ex)
        {
            capi.Logger.Error("[vsrmlui] Closing failed document {0}: {1}", document.SourcePath, ex);
            if (!document.IsDisposed) document.Close();
        }
    }
    private int Modifiers()
    {
        var keys = capi.Input.KeyboardKeyStateRaw;
        bool Down(GlKeys key) => (int)key < keys.Length && keys[(int)key];
        return (Down(GlKeys.LControl) || Down(GlKeys.RControl) ? 1 : 0) | (Down(GlKeys.LShift) || Down(GlKeys.RShift) ? 2 : 0)
            | (Down(GlKeys.LAlt) || Down(GlKeys.RAlt) ? 4 : 0) | (Down(GlKeys.LWin) || Down(GlKeys.RWin) ? 8 : 0) | lockModifiers();
    }
    private static int Button(EnumMouseButton button) => button switch { EnumMouseButton.Left => 0, EnumMouseButton.Right => 1, EnumMouseButton.Middle => 2, _ => -1 };
    public override void OnMouseMove(MouseEvent args)
    {
        if (args.Handled || !ShouldReceiveMouseEvents()) return;
        if (document.Filter(new(RmlInputKind.MouseMove, args.X, args.Y, Modifiers: Modifiers()))) { args.Handled = true; return; }
        args.Handled = document.Call(5, args.X - document.Viewport.X, args.Y - document.Viewport.Y, Modifiers()) != 0 || Modal || document.CapturedPointer is not null;
        MouseOverCursor = host.CurrentCursor;
        document.Runtime.DrainEvents();
    }
    public override void OnMouseDown(MouseEvent args)
    {
        if (args.Handled || !ShouldReceiveMouseEvents()) return;
        bool hover = document.Call(5, args.X - document.Viewport.X, args.Y - document.Viewport.Y, Modifiers()) != 0;
        int button = Button(args.Button);
        if (document.Filter(new(RmlInputKind.MouseDown, args.X, args.Y, button, Modifiers: Modifiers()))) { pressedButtons.Add(button); args.Handled = true; return; }
        if (hover || Modal)
        {
            capi.Gui.RequestFocus(this);
            if (button >= 0) { pressedButtons.Add(button); document.Call(6, button, Modifiers()); }
            args.Handled = true;
        }
        document.Runtime.DrainEvents();
    }
    public override void OnMouseUp(MouseEvent args)
    {
        if (!ShouldReceiveMouseEvents()) return;
        int button = Button(args.Button);
        bool captured = pressedButtons.Remove(button);
        if (document.Filter(new(RmlInputKind.MouseUp, args.X, args.Y, button, Modifiers: Modifiers()))) { args.Handled = true; return; }
        if (args.Handled && !captured) return;
        bool hover = document.Call(5, args.X - document.Viewport.X, args.Y - document.Viewport.Y, Modifiers()) != 0;
        if (button >= 0 && (captured || hover || Modal)) document.Call(7, button, Modifiers());
        args.Handled |= captured || hover || Modal;
        document.Runtime.DrainEvents();
    }
    public override void OnMouseWheel(MouseWheelEventArgs args)
    {
        if (args.IsHandled || !ShouldReceiveMouseEvents()) return;
        if (document.Filter(new(RmlInputKind.Wheel, capi.Input.MouseX, capi.Input.MouseY, Wheel: args.deltaPrecise, Modifiers: Modifiers()))) { args.SetHandled(true); return; }
        bool hover = document.Call(5, capi.Input.MouseX - document.Viewport.X, capi.Input.MouseY - document.Viewport.Y, Modifiers()) != 0;
        if (hover || Modal) { document.Call(8, Modifiers(), value: -args.deltaPrecise); args.SetHandled(true); }
        document.Runtime.DrainEvents();
    }
    private int Modifiers(KeyEvent args) => KeyMap.Modifiers(args, OperatingSystem.IsMacOS()) | lockModifiers();
    public override void OnKeyDown(KeyEvent args)
    {
        if (args.Handled || !ShouldReceiveKeyboardEvents()) return;
        int key = KeyMap.Convert((GlKeys)args.KeyCode);
        if (document.Filter(new(RmlInputKind.KeyDown, Key: args.KeyCode, Modifiers: Modifiers(args)))) { args.Handled = true; return; }
        bool consumed = false;
        if (key != 0) { pressedKeys.Add(key); consumed = document.Call(9, key, Modifiers(args)) != 0; }
        if (args.KeyCode == (int)GlKeys.Escape && document.Options.CloseOnEscape && !consumed)
        { args.Handled = true; document.Close(); return; }
        // Focused windows own keyboard input; HUDs never enter this path.
        // This also prevents GuiManager from delivering the same key twice.
        args.Handled = true;
        document.Runtime.DrainEvents();
    }
    public override void OnKeyUp(KeyEvent args)
    {
        if (!ShouldReceiveKeyboardEvents()) return;
        int key = KeyMap.Convert((GlKeys)args.KeyCode);
        bool captured = pressedKeys.Remove(key);
        if (document.Filter(new(RmlInputKind.KeyUp, Key: args.KeyCode, Modifiers: Modifiers(args)))) { args.Handled = true; return; }
        if (args.Handled && !captured) return;
        args.Handled |= (key != 0 && document.Call(10, key, Modifiers(args)) != 0) || document.Call(13) != 0 || Modal;
        document.Runtime.DrainEvents();
    }
    public override void OnKeyPress(KeyEvent args)
    {
        if (args.Handled || !ShouldReceiveKeyboardEvents()) return;
        if (ignoreNextKeyPress) { ignoreNextKeyPress = false; args.Handled = true; return; }
        // KeyChar is the game's committed text event. Keep it independent from
        // the physical modifier state: Linux IMEs and AltGr may report modifiers
        // while still producing a valid Unicode character.
        if (char.IsControl(args.KeyChar)) return;
        char ch = args.KeyChar;
        if (char.IsHighSurrogate(ch)) { highSurrogate = ch; args.Handled = true; return; }
        string text = char.IsLowSurrogate(ch) && highSurrogate.HasValue ? new string([highSurrogate.Value, ch]) : char.IsSurrogate(ch) ? "" : ch.ToString();
        highSurrogate = null;
        if (text.Length != 0) args.Handled = document.Call(11, text: text) != 0 || document.Call(13) != 0 || Modal;
        document.Runtime.DrainEvents();
    }
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        TryClose();
        capi.Gui.LoadedGuis.Remove(this);
        base.Dispose();
    }
}

internal static class KeyMap
{
    internal static int Modifiers(KeyEvent args, bool macOS)
    {
        // RmlUi's text widget uses ctrl_key for these shortcuts on every OS.
        bool editCommand = macOS && args.CommandPressed && (GlKeys)args.KeyCode is GlKeys.A or GlKeys.C or GlKeys.X or GlKeys.V or GlKeys.Z or GlKeys.Y;
        return (args.CtrlPressed || editCommand ? 1 : 0) | (args.ShiftPressed ? 2 : 0) | (args.AltPressed ? 4 : 0) | (args.CommandPressed ? 8 : 0);
    }
    internal static int Convert(GlKeys key)
    {
        if (key is >= GlKeys.A and <= GlKeys.Z) return 12 + key - GlKeys.A;
        if (key is >= GlKeys.Number0 and <= GlKeys.Number9) return 2 + key - GlKeys.Number0;
        if (key is >= GlKeys.Keypad0 and <= GlKeys.Keypad9) return 51 + key - GlKeys.Keypad0;
        if (key is >= GlKeys.F1 and <= GlKeys.F24) return 107 + key - GlKeys.F1;
        return key switch
        {
            GlKeys.Space => 1, GlKeys.BackSpace => 69, GlKeys.Tab => 70, GlKeys.Clear => 71, GlKeys.Enter => 72,
            GlKeys.Pause => 73, GlKeys.CapsLock => 74, GlKeys.Escape => 81, GlKeys.PageUp => 86, GlKeys.PageDown => 87,
            GlKeys.End => 88, GlKeys.Home => 89, GlKeys.Left => 90, GlKeys.Up => 91, GlKeys.Right => 92, GlKeys.Down => 93,
            GlKeys.PrintScreen => 97, GlKeys.Insert => 98, GlKeys.Delete => 99, GlKeys.LWin => 175, GlKeys.RWin => 176,
            GlKeys.NumLock => 131, GlKeys.ScrollLock => 132, GlKeys.LShift => 138, GlKeys.RShift => 139,
            GlKeys.LControl => 140, GlKeys.RControl => 141, GlKeys.LAlt => 142, GlKeys.RAlt => 143,
            GlKeys.Semicolon => 38, GlKeys.Plus => 39, GlKeys.Comma => 40, GlKeys.Minus => 41, GlKeys.Period => 42,
            GlKeys.Slash => 43, GlKeys.Tilde => 44, GlKeys.BracketLeft => 45, GlKeys.BackSlash => 46, GlKeys.BracketRight => 47, GlKeys.Quote => 48,
            GlKeys.KeypadEnter => 61, GlKeys.KeypadMultiply => 62, GlKeys.KeypadAdd => 63, GlKeys.KeypadSubtract => 65,
            GlKeys.KeypadDecimal => 66, GlKeys.KeypadDivide => 67, _ => 0
        };
    }
}
