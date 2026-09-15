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
        ui.CreateView = document => new GameDialog(api, host, document);
        try
        {
            using var window = ui.LoadDocumentFromString("gamechecks", "<rml><body><input id='entry' class='text' type='text' /></body></rml>", "gamechecks:dialog/window.rml");
            var view = (GameDialog)window.View!;
            window.Show();
            check(view.Focused && view.PrefersUngrabbedMouse && view.CaptureAllInputs(), "game window owns focus and unlocks cursor");
            window.GetElementById("entry")!.Focus(); window.Call(3, 800, 600, 1); window.Call(4);
            var typing = new KeyEvent { KeyChar = '中' }; view.OnKeyPress(typing);
            view.OnKeyPress(new KeyEvent { KeyChar = '\ud83d' }); view.OnKeyPress(new KeyEvent { KeyChar = '\ude42' });
            check(typing.Handled && window.GetElementById("entry")!.Value.Contains("中🙂"), "game text events preserve Chinese and surrogate pairs");
            view.OnKeyPress(new KeyEvent { KeyChar = '@', CtrlPressed = true, AltPressed = true });
            check(window.GetElementById("entry")!.Value.EndsWith("@"), "AltGr committed text reaches the control");
            var commandA = new KeyEvent { KeyCode = (int)GlKeys.A, CommandPressed = true };
            window.Call(9, KeyMap.Convert(GlKeys.A), KeyMap.Modifiers(commandA, macOS: true));
            window.Call(11, text: "replacement");
            check(window.GetElementById("entry")!.Value == "replacement", "macOS Command+A selects all through the native text widget");
            check((KeyMap.Modifiers(commandA, macOS: false) & 1) == 0, "Windows Meta does not become Control");
            using var hud = ui.LoadDocumentFromString("gamechecks", "<rml><body>HUD</body></rml>", "gamechecks:dialog/hud.rml", new() { Mode = RmlWindowMode.Hud });
            hud.Show(); var hudView = (GameDialog)hud.View!;
            check(!hudView.Focused && !hudView.PrefersUngrabbedMouse && !hudView.CaptureAllInputs() && !hudView.ShouldReceiveMouseEvents(), "HUD does not capture focus, cursor or game input");
            check(!hudView.OnEscapePressed() && hud.IsVisible, "HUD remains visible on game Escape broadcast");
            using (RmlRenderScope.Enter(RmlDrawTarget.Offscreen))
                check(!view.ShouldReceiveRenderEvents() && !hudView.ShouldReceiveRenderEvents(), "screen windows and HUDs are excluded from offscreen passes");
            check(hudView.ShouldReceiveRenderEvents(), "screen drawing resumes after offscreen scope");
            window.InputFilter = input => input.Kind == RmlInputKind.MouseDown;
            var filtered = new MouseEvent(799, 599, EnumMouseButton.Left, 0); view.OnMouseDown(filtered);
            check(filtered.Handled, "synchronous input filter prevents game fallthrough");
            window.InputFilter = null;
            check(ui.TryGetDocument(view, out var found) && found == window && window.Host == view, "host document lookup works both ways");

            using var modal = ui.LoadDocumentFromString("gamechecks", "<rml><body>Modal</body></rml>", "gamechecks:dialog/modal.rml", new() { Mode = RmlWindowMode.Modal, CloseOnEscape = false });
            modal.Show(); var modalView = (GameDialog)modal.View!;
            check(modalView.Focused && !view.Focused && modalView.DisableMouseGrab, "modal takes focus from another RmlUi window");
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
