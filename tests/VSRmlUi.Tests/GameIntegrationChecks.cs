// Copyright (c) 2026 VSCN-Studio
// SPDX-License-Identifier: MIT

using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using VSRmlUi;

internal static class GameIntegrationChecks
{
    // Exercise the real GuiDialog subclass with small API doubles, without a game world.
    internal static void Run(RmlRuntime ui, Action<bool, string> check)
    {
        var loaded = new List<GuiDialog>();
        var opened = new List<GuiDialog>();
        var logger = Proxy<ILogger>((_, _) => null);
        var input = Proxy<IInputAPI>((method, _) => method.Name == "get_KeyboardKeyStateRaw" ? new bool[256] : Default(method.ReturnType));
        var gui = Proxy<IGuiAPI>((method, arguments) =>
        {
            switch (method.Name)
            {
                case "get_LoadedGuis": return loaded;
                case "get_OpenedGuis": return opened;
                case "RegisterDialog": loaded.AddRange((GuiDialog[])arguments![0]!); break;
                case "TriggerDialogOpened": opened.Add((GuiDialog)arguments![0]!); break;
                case "TriggerDialogClosed": opened.Remove((GuiDialog)arguments![0]!); break;
                case "RequestFocus":
                    var focus = (GuiDialog)arguments![0]!;
                    foreach (var dialog in loaded.ToArray()) if (dialog != focus) dialog.UnFocus();
                    focus.Focus(); break;
            }
            return Default(method.ReturnType);
        });
        var api = Proxy<ICoreClientAPI>((method, _) => method.Name switch { "get_Gui" => gui, "get_Input" => input, "get_Logger" => logger, _ => Default(method.ReturnType) });
        var host = new GameHost(api);
        bool numLock = false;
        ui.CreateView = document => new GameDialog(api, host, document, () => numLock ? 32 : 0);
        try
        {
            using var window = ui.LoadDocumentFromString("gamechecks", "<rml><body><input id='entry' class='text' type='text' /></body></rml>", "gamechecks:dialog/window.rml");
            var view = (GameDialog)window.View!;
            check(view.InputOrder == 0.5, "default document preserves native dialog input priority");
            window.Show();
            check(view.Focused && view.PrefersUngrabbedMouse && view.CaptureAllInputs(), "game window owns focus and unlocks cursor");
            window.GetElementById("entry")!.Focus(); window.Call(3, 800, 600, 1); window.Call(4);
            var typing = new KeyEvent { KeyChar = '中' }; view.OnKeyPress(typing);
            view.OnKeyPress(new KeyEvent { KeyChar = '\ud83d' }); view.OnKeyPress(new KeyEvent { KeyChar = '\ude42' });
            check(typing.Handled && window.GetElementById("entry")!.Value.Contains("中🙂"), "game text events preserve Chinese and surrogate pairs");
            var imeCommitted = new KeyEvent { KeyChar = '界', CtrlPressed = true }; view.OnKeyPress(imeCommitted);
            check(imeCommitted.Handled && window.GetElementById("entry")!.Value.EndsWith("🙂界"), "committed IME text is not dropped with modifiers");
            view.OnKeyPress(new KeyEvent { KeyChar = '@', CtrlPressed = true, AltPressed = true });
            check(window.GetElementById("entry")!.Value.EndsWith("@"), "AltGr committed text reaches the control");
            var commandA = new KeyEvent { KeyCode = (int)GlKeys.A, CommandPressed = true };
            window.Call(9, KeyMap.Convert(GlKeys.A), KeyMap.Modifiers(commandA, macOS: true));
            window.Call(11, text: "replacement");
            check(window.GetElementById("entry")!.Value == "replacement", "macOS Command+A selects all through the native text widget");
            check((KeyMap.Modifiers(commandA, macOS: false) & 1) == 0, "Windows Meta does not become Control");
            check(KeyboardLocks.ReadWindowsModifiers(key => key == 0x90 ? (short)1 : (short)0) == 32,
                "Windows Num Lock toggle maps to RmlUi KM_NUMLOCK");
            check(KeyboardLocks.ReadWindowsModifiers(_ => unchecked((short)0x8000)) == 0,
                "holding a lock key does not substitute for its toggle state");
            var entry = window.GetElementById("entry")!;
            void Key(GlKeys key)
            {
                view.OnKeyDown(new KeyEvent { KeyCode = (int)key });
                view.OnKeyUp(new KeyEvent { KeyCode = (int)key });
            }
            void Middle()
            {
                entry.Value = "1234"; entry.Focus(); window.Call(3, 800, 600, 1);
                Key(GlKeys.Home); Key(GlKeys.Right); Key(GlKeys.Right);
            }
            numLock = true;
            for (int digit = 0; digit <= 9; digit++)
            {
                Middle();
                Key(GlKeys.Keypad0 + digit); view.OnKeyPress(new KeyEvent { KeyChar = (char)('0' + digit) });
                check(entry.Value == "12" + digit + "34", $"Num Lock on: keypad {digit} inserts at the existing caret");
            }
            Middle(); Key(GlKeys.KeypadDecimal); view.OnKeyPress(new KeyEvent { KeyChar = '.' });
            check(entry.Value == "12.34", "keypad decimal inserts without deleting the following digit");
            Middle();
            foreach (int digit in new[] { 7, 1, 4, 6 })
            {
                Key(GlKeys.Keypad0 + digit); view.OnKeyPress(new KeyEvent { KeyChar = (char)('0' + digit) });
                window.Call(3, 800, 600, 1);
            }
            check(entry.Value == "12714634", "consecutive keypad digits preserve insertion order and caret position");
            numLock = false;
            foreach (var pair in new[] { (GlKeys.Keypad7, "X1234"), (GlKeys.Keypad1, "1234X"), (GlKeys.Keypad4, "1X234"), (GlKeys.Keypad6, "123X4") })
            {
                Middle(); Key(pair.Item1); view.OnKeyPress(new KeyEvent { KeyChar = 'X' });
                check(entry.Value == pair.Item2, $"Num Lock off: {pair.Item1} retains navigation");
            }
            Middle(); Key(GlKeys.KeypadDecimal);
            check(entry.Value == "124", "Num Lock off: keypad decimal retains Delete");
            Middle(); Key(GlKeys.Number7); view.OnKeyPress(new KeyEvent { KeyChar = '7' });
            check(entry.Value == "12734", "top-row digits are independent of Num Lock");
            view.UnFocus(); numLock = true; view.Focus(); Middle(); Key(GlKeys.Keypad7); view.OnKeyPress(new KeyEvent { KeyChar = '7' });
            check(entry.Value == "12734", "lock changes while unfocused are respected without reopening the document");
            // Linux and macOS do not expose the lock-toggle state through the
            // game API. The host must defer dual-use keypad navigation until
            // the committed text event proves that the key inserted a digit.
            ui.CreateView = document => new GameDialog(api, host, document, () => 0, deferKeypadNavigation: true);
            using var crossPlatform = ui.LoadDocumentFromString("gamechecks", "<rml><body><input id='entry' class='text' type='text' /></body></rml>", "gamechecks:dialog/cross-platform.rml");
            var crossPlatformView = (GameDialog)crossPlatform.View!;
            var crossPlatformEntry = crossPlatform.GetElementById("entry")!;
            crossPlatform.Show(); crossPlatformEntry.Value = "1234"; crossPlatformEntry.Focus(); crossPlatform.Call(3, 800, 600, 1);
            crossPlatform.Call(9, KeyMap.Convert(GlKeys.End));
            crossPlatformView.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Keypad7 });
            crossPlatformView.OnKeyPress(new KeyEvent { KeyChar = '7' });
            crossPlatformView.OnKeyUp(new KeyEvent { KeyCode = (int)GlKeys.Keypad7 });
            check(crossPlatformEntry.Value == "12347", "cross-platform Num Lock on keypad digit inserts text without navigation");
            crossPlatformEntry.Value = "1234"; crossPlatformEntry.Focus(); crossPlatform.Call(3, 800, 600, 1);
            crossPlatform.Call(9, KeyMap.Convert(GlKeys.Home));
            crossPlatformView.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Keypad7 });
            crossPlatformView.OnKeyUp(new KeyEvent { KeyCode = (int)GlKeys.Keypad7 });
            crossPlatformView.OnKeyPress(new KeyEvent { KeyChar = 'X' });
            check(crossPlatformEntry.Value == "X1234", "cross-platform Num Lock off keypad navigation remains available");
            crossPlatform.Dispose();
            using var hud = ui.LoadDocumentFromString("gamechecks", "<rml><body>HUD</body></rml>", "gamechecks:dialog/hud.rml", new() { Mode = RmlWindowMode.Hud });
            hud.Show(); var hudView = (GameDialog)hud.View!;
            check(!hudView.Focused && !hudView.PrefersUngrabbedMouse && !hudView.CaptureAllInputs() && !hudView.ShouldReceiveMouseEvents(), "HUD does not capture focus, cursor or game input");
            check(!hudView.OnEscapePressed() && hud.IsVisible, "HUD remains visible on game Escape broadcast");
            using (RmlRenderScope.Enter(RmlDrawTarget.Offscreen))
                check(!view.ShouldReceiveRenderEvents() && !hudView.ShouldReceiveRenderEvents(), "screen windows and HUDs are excluded from offscreen passes");
            check(hudView.ShouldReceiveRenderEvents(), "screen drawing resumes after offscreen scope");
            foreach (var mode in new[] { RmlWindowMode.Window, RmlWindowMode.Modal })
            {
                using var passive = ui.LoadDocumentFromString("gamechecks", "<rml><body>Visible panel</body></rml>", "gamechecks:dialog/passive.rml", new() { Mode = mode });
                passive.Show(); var passiveView = (GameDialog)passive.View!;
                passiveView.UnFocus();
                passive.Options.Input.ReceiveMouse = false;
                passive.Options.Input.ReceiveKeyboard = false;
                check(passiveView.DialogType == EnumDialogType.HUD && !passiveView.Focusable && !passiveView.DisableMouseGrab
                    && !passiveView.PrefersUngrabbedMouse && !passiveView.CaptureRawMouse() && !passiveView.CaptureAllInputs(),
                    $"disabled {mode} panel becomes HUD and does not block mouse capture");
                check(passive.IsVisible && passiveView.ShouldReceiveRenderEvents() && !passiveView.OnEscapePressed(),
                    $"passive {mode} panel still renders and does not intercept Escape");
                passive.Options.Input.ReceiveKeyboard = true;
                check(passiveView.DialogType == EnumDialogType.Dialog && passiveView.Focusable,
                    $"keyboard-only {mode} panel remains an interactive dialog");
                passive.Options.Input.ReceiveKeyboard = false;
                passive.Options.Input.ReceiveMouse = true;
                check(passiveView.DialogType == EnumDialogType.Dialog && passiveView.ShouldReceiveMouseEvents()
                    && passiveView.DisableMouseGrab == (mode == RmlWindowMode.Modal),
                    $"reenabling {mode} restores interaction and original modality");
            }
            window.Show();
            window.InputFilter = input => input.Kind == RmlInputKind.MouseDown;
            var filtered = new MouseEvent(799, 599, EnumMouseButton.Left, 0); view.OnMouseDown(filtered);
            check(filtered.Handled, "synchronous input filter prevents game fallthrough");
            window.InputFilter = null;
            check(ui.TryGetDocument(view, out var found) && found == window && window.Host == view, "host document lookup works both ways");
            using (var overlay = ui.LoadDocumentFromString("gamechecks", "<rml><body/></rml>", "gamechecks:dialog/pause.rml", new() { InputOrder = -0.1, DrawOrder = 0.99 }))
                check(((GameDialog)overlay.View!).InputOrder < 0 && ((GameDialog)overlay.View!).DrawOrder > 0.89, "pause overlay receives input before and renders above the native menu");

            using var modal = ui.LoadDocumentFromString("gamechecks", "<rml><body>Modal</body></rml>", "gamechecks:dialog/modal.rml", new() { Mode = RmlWindowMode.Modal, CloseOnEscape = false });
            modal.Show(); var modalView = (GameDialog)modal.View!;
            check(modalView.Focused && !view.Focused && modalView.DisableMouseGrab, "modal takes focus from another RmlUi window");
            using (var dock = ui.LoadDocumentFromString("gamechecks", "<rml><body>Inspector</body></rml>", "gamechecks:dialog/dock.rml", new() { FocusOnOpen = false, CloseOnEscape = false }))
            {
                dock.Show(); var dockView = (GameDialog)dock.View!;
                check(modalView.Focused && !dockView.Focused && dockView.ShouldReceiveMouseEvents(), "docked inspector opens interactively without stealing modal focus");
                dock.Close(); dock.Show();
                check(modalView.Focused && !dockView.Focused, "reopening an inspector preserves modal focus");
            }
            var mouse = new MouseEvent(799, 599, EnumMouseButton.Left, 0); modalView.OnMouseDown(mouse);
            check(mouse.Handled, "modal captures clicks outside document geometry");
            var esc = new KeyEvent { KeyCode = (int)GlKeys.Escape }; modalView.OnKeyDown(esc);
            check(esc.Handled && modal.IsVisible && !modalView.OnEscapePressed(), "CloseOnEscape=false survives key and game broadcast paths");
            modal.Dispose(); window.Show();
            view.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Escape });
            check(!window.IsVisible && !view.IsOpened() && hud.IsVisible, "Escape closes focused window and preserves HUD");
            window.Dispose(); hud.Dispose();
            check(loaded.Count == 0 && opened.Count == 0, "disposed documents unregister game dialogs");
        }
        finally { ui.CreateView = null; }
    }
    private static object? Default(Type type) => type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);
    private static T Proxy<T>(System.Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, ApiProxy>(); ((ApiProxy)(object)proxy).Handler = handler; return proxy;
    }
    public class ApiProxy : DispatchProxy
    {
        public System.Func<MethodInfo, object?[]?, object?> Handler = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
}
